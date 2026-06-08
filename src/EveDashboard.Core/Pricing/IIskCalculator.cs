namespace EveDashboard.Core.Pricing;

using EveDashboard.Core.Settings;

/// <summary>
/// Computes ISK from a typeId-to-units-mined map per the canonical recursive
/// reprocessing algorithm in <c>MEMORY.md -&gt; project_isk_calculation</c>.
/// Implementations live in <c>EveDashboard.MarketData</c>.
/// </summary>
public interface IIskCalculator
{
    /// <summary>Compute total ISK for a set of mined units.</summary>
    /// <param name="typeIdToUnitsMined">Mined units per source type id (ore, gas, etc.). Caller has already aggregated raw event rows.</param>
    /// <param name="stationId">Station to price against (read by the caller from <c>selected_station_id</c>).</param>
    /// <param name="sessionId">Plan 38: owning session's sha256 id. Prices are read ONLY from this session's <c>market_data</c> rows, and any lazy fetch stamps new rows with it.</param>
    /// <param name="allowLazyFetch">Plan 38: <c>true</c> for a LIVE session (may fetch missing prices from the API and persist them); <c>false</c> for Reports / saved sessions (never fetch — a missing price contributes 0).</param>
    /// <param name="refiningYieldPercent">Refining yield percent in the range <c>[50.0, 90.6]</c> per <see cref="ISettingsRepository.GetRefiningYieldPercentAsync"/>.</param>
    /// <param name="priceMode">Buy / Sell / Split per <see cref="ISettingsRepository.GetPriceModeAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed ISK total.</returns>
    /// <remarks>
    /// Implementation MUST follow <c>MEMORY.md -&gt; project_isk_calculation</c>
    /// verbatim — recursive reprocessing, efficiency by source-item category,
    /// floor after the multiply, sum across materials. Any missing price (after
    /// the lazy fetch when <paramref name="allowLazyFetch"/> is <c>true</c>, or
    /// always when <c>false</c>) contributes 0 ISK with a Warning log per the
    /// <c>docs/market-schema.md</c> "ISK gracefully degrades to 0" contract.
    /// </remarks>
    Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        long                            stationId,
        string                          sessionId,
        bool                            allowLazyFetch,
        double                          refiningYieldPercent,
        PriceMode                       priceMode,
        CancellationToken               ct = default);

    /// <summary>
    /// Compute total ISK AND crit-only ISK for the supplied mined units in a
    /// single pass. The crit-subset dict must satisfy
    /// <c>critTypeIdToUnitsMined[t] &lt;= typeIdToUnitsMined[t]</c> per type;
    /// pricing and refining yield are applied identically to both totals.
    /// </summary>
    /// <param name="typeIdToUnitsMined">Total mined units per source type id.</param>
    /// <param name="critTypeIdToUnitsMined">Crit-subset of mined units per source type id. Keys MUST be a subset of <paramref name="typeIdToUnitsMined"/>'s keys; missing keys are treated as 0 crit units for that type.</param>
    /// <param name="stationId">Station to price against.</param>
    /// <param name="sessionId">Plan 38: owning session's sha256 id. Prices are read ONLY from this session's <c>market_data</c> rows, and any lazy fetch stamps new rows with it.</param>
    /// <param name="allowLazyFetch">Plan 38: <c>true</c> for a LIVE session (may fetch + persist missing prices); <c>false</c> for Reports / saved sessions (never fetch — missing price contributes 0).</param>
    /// <param name="refiningYieldPercent">Refining yield percent in the range <c>[50.0, 90.6]</c>.</param>
    /// <param name="priceMode">Buy / Sell / Split.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed ISK total in <see cref="IskCalculationResult.TotalIsk"/> and the crit-subset ISK total in <see cref="IskCalculationResult.CritIsk"/>. <see cref="IskCalculationResult.IskByInputType"/> reflects the FULL units (not the crit subset).</returns>
    /// <remarks>
    /// Plan 36: added so <c>SqliteSessionStatsRepository</c> can derive Crit ISK %
    /// in one per-pilot ISK call rather than two. Implementations share the
    /// market-price lookup cache across both totals — exact math, no
    /// proportional-rounding drift, single round-trip through
    /// <c>IMarketPriceFetcher.EnsurePricesAsync</c> (only when
    /// <paramref name="allowLazyFetch"/> is <c>true</c>).
    /// </remarks>
    Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        IReadOnlyDictionary<long, long> critTypeIdToUnitsMined,
        long                            stationId,
        string                          sessionId,
        bool                            allowLazyFetch,
        double                          refiningYieldPercent,
        PriceMode                       priceMode,
        CancellationToken               ct = default);
}
