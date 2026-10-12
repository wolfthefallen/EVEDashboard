namespace EveDashboard.MarketData.Pricing;

using System.Globalization;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IHistoricalMarketPricer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Step 1 - averages: every <c>market_data</c> row captured at the user's
/// currently selected pricing station with a <c>fetched_at</c> inside the window
/// is grouped by type; each side (buy / sell) is averaged over the rows where it
/// is non-null (a side with no values stays null). One row per averaged type is
/// INSERTed for the new session, stamped <c>fetched_at = windowStop</c> - "the
/// price as of the end of the period". Rows at other stations are ignored on
/// purpose: hub prices are not comparable and live sessions never blend them.
/// </para>
/// <para>
/// Step 2 - gaps: <see cref="IMarketPriceFetcher.EnsurePricesAsync"/> is then asked
/// for every refined material. It treats the averaged rows as coverage and
/// fetches only the materials that still lack a usable price for the active
/// price mode, at today's values, stamped with the new session id - the same
/// gap-fill rule a live session uses. The result tells the caller which path
/// covered what so the UI can say "priced with today's values" when it must.
/// </para>
/// <para>
/// Source sessions' rows are read, never altered or deleted.
/// </para>
/// </remarks>
public sealed class HistoricalMarketPricer : IHistoricalMarketPricer
{
    private readonly ISqliteDatabase _db;
    private readonly ISdeLookup _sde;
    private readonly ISettingsRepository _settings;
    private readonly IMarketPriceFetcher _fetcher;
    private readonly ILogger<HistoricalMarketPricer> _log;

    /// <summary>Construct the pricer.</summary>
    /// <param name="db">Session SQLite database (owner of <c>market_data</c>).</param>
    /// <param name="sde">SDE lookup (refined-material type ids).</param>
    /// <param name="settings">Settings (selected station, price mode).</param>
    /// <param name="fetcher">The live price fetcher, used for the gap-fill.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public HistoricalMarketPricer(
        ISqliteDatabase db,
        ISdeLookup sde,
        ISettingsRepository settings,
        IMarketPriceFetcher fetcher,
        ILogger<HistoricalMarketPricer> log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sde = sde ?? throw new ArgumentNullException(nameof(sde));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<HistoricalPricingResult> PriceAsync(string sessionId, DateTimeOffset windowStart, DateTimeOffset windowStop, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var results = await PriceBatchAsync(
            new[] { new HistoricalPricingTarget(sessionId, windowStart, windowStop) }, ct).ConfigureAwait(false);
        return results[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoricalPricingResult>> PriceBatchAsync(
        IReadOnlyList<HistoricalPricingTarget> targets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        foreach (var target in targets) ArgumentNullException.ThrowIfNull(target.SessionId);
        if (targets.Select(t => t.SessionId).Distinct(StringComparer.Ordinal).Count() != targets.Count)
        {
            throw new ArgumentException("targets contains duplicate session ids.", nameof(targets));
        }
        if (targets.Count == 0) return Array.Empty<HistoricalPricingResult>();

        var stationId = await _settings.GetSelectedStationIdAsync(ct).ConfigureAwait(false);
        var priceMode = await _settings.GetPriceModeAsync(ct).ConfigureAwait(false);
        var typeIds = _sde.GetRefinedMaterialTypeIds().Distinct().ToList();
        var wanted = typeIds.ToHashSet();
        var batchIds = targets.Select(t => t.SessionId).ToHashSet(StringComparer.Ordinal);

        // ---- Step 1: average each window's captured rows at the selected station. ----
        // Rows belonging to ANY session in the batch are ignored, not just the one
        // being priced: the averages this method writes are derived data, and a
        // later day must never average an earlier day's derived row.
        var captured = (await _db.QueryAsync(
            "SELECT type_id, session_id, fetched_at, isk_buy, isk_sell FROM market_data WHERE station_id = $st",
            static r => (
                TypeId: r.GetInt64(0),
                SessionId: r.GetString(1),
                FetchedAt: r.GetString(2),
                Buy: r.IsDBNull(3) ? (double?)null : r.GetDouble(3),
                Sell: r.IsDBNull(4) ? (double?)null : r.GetDouble(4)),
            new Dictionary<string, object?> { ["$st"] = stationId },
            ct).ConfigureAwait(false))
            .Where(r => wanted.Contains(r.TypeId) && !batchIds.Contains(r.SessionId))
            .Select(r => (r.TypeId, At: ParseUtc(r.FetchedAt), r.Buy, r.Sell))
            .ToList();

        var states = new List<TargetState>();
        foreach (var target in targets)
        {
            var inWindow = captured
                .Where(r => r.At >= target.WindowStart && r.At <= target.WindowStop)
                .ToList();

            var state = new TargetState(target, inWindow.Count);
            var stamp = target.WindowStop.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            foreach (var group in inWindow.GroupBy(r => r.TypeId))
            {
                var buys = group.Where(r => r.Buy is not null).Select(r => r.Buy!.Value).ToList();
                var sells = group.Where(r => r.Sell is not null).Select(r => r.Sell!.Value).ToList();
                double? avgBuy = buys.Count > 0 ? buys.Average() : null;
                double? avgSell = sells.Count > 0 ? sells.Average() : null;
                if (avgBuy is null && avgSell is null) continue; // nothing usable for this type

                await InsertRowAsync(group.Key, stationId, target.SessionId, stamp, avgBuy, avgSell, ct).ConfigureAwait(false);
                state.AveragedTypes.Add(group.Key);
                state.RowsAveragedByType[group.Key] = group.Count();
                if (!PriceCoverage.Covers(avgBuy, avgSell, priceMode)) state.Gaps.Add(group.Key);
            }

            // A type with no averaged row at all is a gap too.
            foreach (var id in typeIds)
            {
                if (!state.AveragedTypes.Contains(id)) state.Gaps.Add(id);
            }

            states.Add(state);
        }

        // ---- Step 2: gap-fill at today's prices, fetching each type ONCE for the batch. ----
        await FillGapsAsync(states, stationId, priceMode, ct).ConfigureAwait(false);

        var results = new List<HistoricalPricingResult>();
        foreach (var state in states)
        {
            // Rows the fetcher adds are newer than anything averaged, so the ISK
            // calculator will prefer them: a type the fetcher touched counts as
            // "fetched today" even if an averaged row (lacking the side the price
            // mode needs) was written for it.
            var averagedOnly = state.AveragedTypes.Count(t => !state.FetchedTypes.Contains(t));
            var rowsAveraged = state.RowsAveragedByType
                .Where(kv => !state.FetchedTypes.Contains(kv.Key))
                .Sum(kv => kv.Value);
            var unpriced = typeIds.Count(t => !state.AveragedTypes.Contains(t) && !state.FetchedTypes.Contains(t));

            var basis = (averagedOnly, state.FetchedTypes.Count) switch
            {
                (> 0, 0) => HistoricalPriceBasis.AveragedFromWindow,
                (> 0, > 0) => HistoricalPriceBasis.Mixed,
                _ => HistoricalPriceBasis.FetchedToday,
            };

            _log.LogInformation(
                "Historical pricing for session {SessionId} at station {StationId}: {Averaged} type(s) averaged from {Rows} window row(s), {Fetched} fetched today, {Unpriced} unpriced ({Basis}).",
                state.Target.SessionId, stationId, averagedOnly, rowsAveraged, state.FetchedTypes.Count, unpriced, basis);

            results.Add(new HistoricalPricingResult(
                state.Target.SessionId, stationId, basis, averagedOnly, rowsAveraged, state.FetchedTypes.Count, unpriced));
        }

        return results;
    }

    /// <summary>
    /// Give every target today's price for the materials its window could not
    /// cover, fetching each material at most once for the whole batch.
    /// </summary>
    /// <remarks>
    /// The fetcher is session-scoped and skips whatever a session already has, so
    /// a single carrier session cannot be made to fetch another session's gaps.
    /// Instead each target fetches only the materials no earlier target has
    /// fetched yet - so across the batch every material is pulled once - and then
    /// copies the rest out of the pool those fetches filled. Copies are stamped
    /// with the moment they were fetched, which is the same honest "this is
    /// today's price" provenance the live gap-fill writes.
    /// </remarks>
    private async Task FillGapsAsync(List<TargetState> states, long stationId, PriceMode priceMode, CancellationToken ct)
    {
        var pool = new Dictionary<long, (double? Buy, double? Sell, string FetchedAt)>();

        foreach (var state in states)
        {
            var unfetched = state.Gaps.Where(id => !pool.ContainsKey(id)).ToList();
            if (unfetched.Count > 0)
            {
                var watermark = await _db.QueryScalarAsync<long>(
                    "SELECT COALESCE(MAX(marketdata_id), 0) FROM market_data", ct: ct).ConfigureAwait(false);
                await _fetcher.EnsurePricesAsync(unfetched, stationId, state.Target.SessionId, priceMode, ct).ConfigureAwait(false);

                var inserted = await _db.QueryAsync(
                    "SELECT type_id, fetched_at, isk_buy, isk_sell FROM market_data WHERE session_id = $sid AND marketdata_id > $mark",
                    static r => (
                        TypeId: r.GetInt64(0),
                        FetchedAt: r.GetString(1),
                        Buy: r.IsDBNull(2) ? (double?)null : r.GetDouble(2),
                        Sell: r.IsDBNull(3) ? (double?)null : r.GetDouble(3)),
                    new Dictionary<string, object?> { ["$sid"] = state.Target.SessionId, ["$mark"] = watermark },
                    ct).ConfigureAwait(false);

                foreach (var row in inserted)
                {
                    state.FetchedTypes.Add(row.TypeId);
                    pool[row.TypeId] = (row.Buy, row.Sell, row.FetchedAt);
                }
            }

            foreach (var id in state.Gaps)
            {
                if (state.FetchedTypes.Contains(id)) continue;      // just fetched for this session
                if (!pool.TryGetValue(id, out var price)) continue; // the market had nothing for it
                await InsertRowAsync(id, stationId, state.Target.SessionId, price.FetchedAt, price.Buy, price.Sell, ct)
                    .ConfigureAwait(false);
                state.FetchedTypes.Add(id);
            }
        }
    }

    private Task InsertRowAsync(
        long typeId, long stationId, string sessionId, string fetchedAt, double? buy, double? sell, CancellationToken ct) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) VALUES ($tid, $st, $sid, $at, $buy, $sell)",
            new Dictionary<string, object?>
            {
                ["$tid"] = typeId,
                ["$st"] = stationId,
                ["$sid"] = sessionId,
                ["$at"] = fetchedAt,
                ["$buy"] = (object?)buy ?? DBNull.Value,
                ["$sell"] = (object?)sell ?? DBNull.Value,
            },
            ct);

    /// <summary>Per-target bookkeeping while the batch is being priced.</summary>
    private sealed class TargetState
    {
        public TargetState(HistoricalPricingTarget target, int windowRowCount)
        {
            Target = target;
            WindowRowCount = windowRowCount;
        }

        public HistoricalPricingTarget Target { get; }

        /// <summary>How many captured rows fell inside this target's window, before grouping.</summary>
        public int WindowRowCount { get; }

        /// <summary>Types this target got an averaged row for.</summary>
        public HashSet<long> AveragedTypes { get; } = new();

        /// <summary>How many window rows each averaged type was built from.</summary>
        public Dictionary<long, int> RowsAveragedByType { get; } = new();

        /// <summary>Types whose averaged price does not answer the active mode, so they need today's value.</summary>
        public HashSet<long> Gaps { get; } = new();

        /// <summary>Types this target ended up holding a today-priced row for.</summary>
        public HashSet<long> FetchedTypes { get; } = new();
    }

    private static DateTimeOffset ParseUtc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
