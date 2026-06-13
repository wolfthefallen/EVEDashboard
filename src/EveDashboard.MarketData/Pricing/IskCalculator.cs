namespace EveDashboard.MarketData.Pricing;

using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IIskCalculator"/> implementing the canonical recursive
/// reprocessing algorithm in <c>MEMORY.md -&gt; project_isk_calculation</c>.
/// </summary>
/// <remarks>
/// <para>
/// Steps (verbatim, from the canonical reference):
/// <list type="number">
///   <item><description>Recursive reprocessing breakdown at 100% efficiency — accumulator keyed by base material type id.</description></item>
///   <item><description>Apply efficiency by SOURCE-item category. v1 only encounters ore (cat 25); gas has no <c>type_materials</c> so its accumulator entries are 0 already.</description></item>
///   <item><description>Multiply by price (buy / sell / split per <see cref="PriceMode"/>) × weight. <c>weight</c> is always <c>1.0</c> in v1 — collapsed at the call site.</description></item>
///   <item><description>Sum across materials.</description></item>
/// </list>
/// </para>
/// <para>
/// Plan 38: prices are session-bound and station-blind. The price read filters
/// on <c>session_id</c> + <c>type_id</c> only, so a session always reports the
/// prices it captured, regardless of which station is selected now.
/// <c>station_id</c> is fetch-target metadata (which hub to query) plus
/// provenance kept on the row; it is NEVER a read filter.
/// The synchronous lazy fetch runs ONLY when the caller passes
/// <c>allowLazyFetch == true</c> (a LIVE session): the union of needed type ids
/// plus the active <c>priceMode</c> is passed to
/// <see cref="IMarketPriceFetcher.EnsurePricesAsync"/> in a single round-trip
/// before any prices are read. The fetcher fills only mode-aware gaps (a type id
/// with no usable price for the mode), throttled to one attempt per 15 minutes
/// per session, so an already-priced material is never re-fetched. Reports /
/// saved sessions pass <c>false</c> and never fetch. Any still-missing price
/// contributes 0 ISK with a Warning log per the <c>docs/market-schema.md</c>
/// "ISK gracefully degrades to 0" contract.
/// </para>
/// <para>
/// Plan 25 algorithm refactor: the recursive breakdown runs INDEPENDENTLY per
/// top-level input type id, with per-input accumulators rather than a single
/// shared accumulator. This preserves per-input ISK contribution for the
/// Reports per-ore breakdown widget (<see cref="IskCalculationResult.IskByInputType"/>).
/// Floor rounding shifts from per-merged-material to per-(input, material);
/// total ISK can drift by a sub-percent rounding amount vs. the prior
/// implementation. Pre-release rule
/// (<c>MEMORY.md -&gt; feedback_no_migrations_until_release</c>) accepts this.
/// </para>
/// </remarks>
public sealed class IskCalculator : IIskCalculator
{
    private readonly IMarketPriceFetcher _fetcher;
    private readonly ISqliteDatabase     _sessionDb;
    private readonly ISdeLookup          _sdeLookup;
    private readonly ILogger<IskCalculator> _log;

    /// <summary>Construct the calculator.</summary>
    /// <param name="fetcher">Price fetcher used for the synchronous lazy fetch.</param>
    /// <param name="sessionDb">Session SQLite wrapper. Reads <c>market_data</c>.</param>
    /// <param name="sdeLookup">SDE lookup for <c>type_materials</c> and <c>portion_size</c>.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public IskCalculator(
        IMarketPriceFetcher fetcher,
        ISqliteDatabase     sessionDb,
        ISdeLookup          sdeLookup,
        ILogger<IskCalculator> log)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(sessionDb);
        ArgumentNullException.ThrowIfNull(sdeLookup);
        ArgumentNullException.ThrowIfNull(log);
        _fetcher = fetcher;
        _sessionDb = sessionDb;
        _sdeLookup = sdeLookup;
        _log = log;
    }

    /// <inheritdoc />
    public Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        long                            stationId,
        string                          sessionId,
        bool                            allowLazyFetch,
        double                          refiningYieldPercent,
        PriceMode                       priceMode,
        CancellationToken               ct = default)
        => ComputeAsync(
            typeIdToUnitsMined,
            critTypeIdToUnitsMined: EmptyUnits,
            stationId,
            sessionId,
            allowLazyFetch,
            refiningYieldPercent,
            priceMode,
            ct);

    private static readonly IReadOnlyDictionary<long, long> EmptyUnits =
        new Dictionary<long, long>();

    /// <inheritdoc />
    public async Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        IReadOnlyDictionary<long, long> critTypeIdToUnitsMined,
        long                            stationId,
        string                          sessionId,
        bool                            allowLazyFetch,
        double                          refiningYieldPercent,
        PriceMode                       priceMode,
        CancellationToken               ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeIdToUnitsMined);
        ArgumentNullException.ThrowIfNull(critTypeIdToUnitsMined);
        ArgumentNullException.ThrowIfNull(sessionId);
        if (typeIdToUnitsMined.Count == 0)
        {
            return new IskCalculationResult(
                TotalIsk: 0.0,
                CritIsk:  0.0,
                IskByInputType: new Dictionary<long, double>());
        }

        // Step 1 — recursive reprocessing breakdown at 100% efficiency, ONE
        // accumulator per top-level input type. Plan 25 refactor: preserves
        // per-input contribution for the Reports per-ore widget. Plan 36: a
        // parallel crit-only accumulator runs in lockstep when the caller
        // supplied a non-empty critTypeIdToUnitsMined.
        var perInputAccumulator     = new Dictionary<long, Dictionary<long, long>>(typeIdToUnitsMined.Count);
        var perInputCritAccumulator = new Dictionary<long, Dictionary<long, long>>(typeIdToUnitsMined.Count);
        foreach (var kvp in typeIdToUnitsMined)
        {
            var inputAcc = new Dictionary<long, long>();
            BreakdownRecursive(kvp.Key, kvp.Value, inputAcc);
            perInputAccumulator[kvp.Key] = inputAcc;

            var critAcc = new Dictionary<long, long>();
            if (critTypeIdToUnitsMined.TryGetValue(kvp.Key, out var critUnits) && critUnits > 0)
            {
                BreakdownRecursive(kvp.Key, critUnits, critAcc);
            }
            perInputCritAccumulator[kvp.Key] = critAcc;
        }

        // Step 2 — efficiency selector. v1: every accumulator entry originates
        // from ore (gas has no type_materials -> Step 1 emits no accumulator
        // rows for gas). ore_eff = refiningYieldPercent / 100.
        var oreEff = refiningYieldPercent / 100.0;

        // Synchronous lazy fetch — gather the UNION of all needed material type
        // ids across every per-input accumulator in one round-trip BEFORE
        // re-reading market_data. Crit accumulators are a subset of the full
        // accumulators, so the union is just the full set.
        var allMaterials = new HashSet<long>();
        foreach (var acc in perInputAccumulator.Values)
        {
            foreach (var matId in acc.Keys)
            {
                allMaterials.Add(matId);
            }
        }

        if (allMaterials.Count == 0)
        {
            // No accumulator entries at all (e.g. every input was gas in v1).
            // Every input contributes 0; preserve the per-input keys so callers
            // can still index on them.
            var emptyByInput = new Dictionary<long, double>(typeIdToUnitsMined.Count);
            foreach (var inputTypeId in typeIdToUnitsMined.Keys)
            {
                emptyByInput[inputTypeId] = 0.0;
            }
            return new IskCalculationResult(0.0, 0.0, emptyByInput);
        }

        // Plan 38: only a LIVE session may fetch + persist missing prices.
        // Reports / saved sessions (allowLazyFetch == false) read whatever the
        // session already captured; a missing row contributes 0 (below).
        if (allowLazyFetch)
        {
            await _fetcher.EnsurePricesAsync(allMaterials.ToList(), stationId, sessionId, priceMode, ct).ConfigureAwait(false);
        }

        // Cache prices per material so we don't re-query the same row N times
        // when N inputs share materials (e.g. multiple Veldspar variants both
        // produce Tritanium).
        var priceCache = new Dictionary<long, double?>();

        // Steps 3 + 4 — multiply by price (weight = 1.0 in v1) and sum, per
        // input. Floor is applied per (input, material) pair; small rounding
        // differences vs. a merged-accumulator approach are accepted (plan 25).
        // Plan 36: the crit accumulator is processed in lockstep with the full
        // accumulator using the same priceCache so the price lookup is paid
        // exactly once per material across both totals.
        var totalIsk = 0.0;
        var critIsk  = 0.0;
        var iskByInputType = new Dictionary<long, double>(typeIdToUnitsMined.Count);
        // Plan 43: yield-applied refined output, material type_id -> total units,
        // summed across every input ore. Accumulated BEFORE the price gate so a
        // material with no captured price still reports its quantity (it just
        // contributes 0 ISK). Built from the FULL accumulator only (not crit).
        var refinedByMaterial = new Dictionary<long, long>();
        foreach (var (inputTypeId, inputAcc) in perInputAccumulator)
        {
            var critAcc = perInputCritAccumulator[inputTypeId];
            var perInputIsk = 0.0;
            foreach (var (materialTypeId, rawQuantity) in inputAcc)
            {
                // Plan 43: floor the yield-applied quantity once, up front, and
                // record it (omit a material that floors to 0). This is the same
                // floored value the ISK total uses, so quantity reconciles with ISK.
                var adjustedQty = (long)Math.Floor(rawQuantity * oreEff);
                if (adjustedQty > 0)
                {
                    refinedByMaterial[materialTypeId] =
                        refinedByMaterial.GetValueOrDefault(materialTypeId) + adjustedQty;
                }

                if (!priceCache.TryGetValue(materialTypeId, out var price))
                {
                    price = await ReadPriceAsync(materialTypeId, sessionId, priceMode, ct).ConfigureAwait(false);
                    priceCache[materialTypeId] = price;
                }
                if (price is null)
                {
                    continue;   // No price -> 0 ISK contribution per market-schema.md (quantity already recorded above).
                }
                perInputIsk += adjustedQty * price.Value;   // weight = 1.0 in v1.

                if (critAcc.TryGetValue(materialTypeId, out var critRawQty) && critRawQty > 0)
                {
                    var critAdjustedQty = (long)Math.Floor(critRawQty * oreEff);
                    critIsk += critAdjustedQty * price.Value;
                }
            }
            iskByInputType[inputTypeId] = perInputIsk;
            totalIsk += perInputIsk;
        }

        _log.LogDebug(
            "ComputeAsync(typeIdCount={InCount}, materialCount={MatCount}, critTypeIdCount={CritInCount}) -> ISK={Total} CritISK={Crit} (per-input keys: {KeyCount}, refined materials: {RefinedCount})",
            typeIdToUnitsMined.Count, allMaterials.Count, critTypeIdToUnitsMined.Count, totalIsk, critIsk, iskByInputType.Count, refinedByMaterial.Count);

        return new IskCalculationResult(totalIsk, critIsk, iskByInputType)
        {
            RefinedQuantitiesByMaterial = refinedByMaterial,
        };
    }

    private void BreakdownRecursive(long typeId, long quantity, Dictionary<long, long> acc)
    {
        var materials = _sdeLookup.GetTypeMaterials(typeId);
        if (materials.Count == 0)
        {
            // Base material — stop recursing.
            if (acc.TryGetValue(typeId, out var existing))
            {
                acc[typeId] = existing + quantity;
            }
            else
            {
                acc[typeId] = quantity;
            }
            return;
        }

        var portionSize = _sdeLookup.TryGetProduct(typeId)?.PortionSize ?? 1;
        if (portionSize <= 0) portionSize = 1;
        var batches = quantity / portionSize;
        // Leftover (= quantity % portionSize) intentionally dropped: per
        // MEMORY.md -> project_isk_calculation, units below one full batch
        // remain the original item and contribute 0 ISK in this project.
        if (batches == 0)
        {
            return;
        }
        foreach (var m in materials)
        {
            var rawTotal = m.Quantity * batches;
            BreakdownRecursive(m.MaterialTypeId, rawTotal, acc);
        }
    }

    private async Task<double?> ReadPriceAsync(
        long              typeId,
        string            sessionId,
        PriceMode         priceMode,
        CancellationToken ct)
    {
        // Plan 38 (amended): the price read is session-bound and STATION-BLIND.
        // A session owns its captured prices via session_id; station_id is
        // fetch-target metadata + provenance only, never a read filter. The
        // latest captured price for (session_id, type_id) wins — so a saved
        // session's ISK is the same no matter which station is selected now.
        const string Sql = """
            SELECT isk_buy, isk_sell
            FROM market_data
            WHERE session_id = $session AND type_id = $tid
            ORDER BY fetched_at DESC
            LIMIT 1
            """;
        var rows = await _sessionDb.QueryAsync(
            Sql,
            static r => (
                Buy:  r.IsDBNull(0) ? (double?)null : r.GetDouble(0),
                Sell: r.IsDBNull(1) ? (double?)null : r.GetDouble(1)),
            new Dictionary<string, object?> { ["$tid"] = typeId, ["$session"] = sessionId },
            ct).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            _log.LogWarning(
                "No market_data row for type_id={TypeId} in session {SessionId} after lazy fetch; contributing 0 ISK.",
                typeId, sessionId);
            return null;
        }

        var (buy, sell) = rows[0];
        var price = priceMode switch
        {
            PriceMode.Buy   => buy,
            PriceMode.Sell  => sell,
            PriceMode.Split => buy.HasValue && sell.HasValue
                                ? (buy.Value + sell.Value) / 2.0
                                : (buy ?? sell),
            _               => buy,
        };

        if (price is null)
        {
            _log.LogWarning(
                "market_data row for type_id={TypeId} in session {SessionId} has null price for mode {Mode}; contributing 0 ISK.",
                typeId, sessionId, priceMode);
        }
        return price;
    }
}
