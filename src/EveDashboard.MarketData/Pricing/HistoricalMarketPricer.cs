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

        var stationId = await _settings.GetSelectedStationIdAsync(ct).ConfigureAwait(false);
        var priceMode = await _settings.GetPriceModeAsync(ct).ConfigureAwait(false);
        var typeIds = _sde.GetRefinedMaterialTypeIds().Distinct().ToList();
        var wanted = typeIds.ToHashSet();

        // ---- Step 1: average the window's captured rows at the selected station. ----
        var captured = await _db.QueryAsync(
            "SELECT type_id, fetched_at, isk_buy, isk_sell FROM market_data WHERE station_id = $st AND session_id <> $sid",
            static r => (
                TypeId: r.GetInt64(0),
                FetchedAt: r.GetString(1),
                Buy: r.IsDBNull(2) ? (double?)null : r.GetDouble(2),
                Sell: r.IsDBNull(3) ? (double?)null : r.GetDouble(3)),
            new Dictionary<string, object?> { ["$st"] = stationId, ["$sid"] = sessionId },
            ct).ConfigureAwait(false);

        var inWindow = captured
            .Where(r => wanted.Contains(r.TypeId))
            .Select(r => (r.TypeId, At: ParseUtc(r.FetchedAt), r.Buy, r.Sell))
            .Where(r => r.At >= windowStart && r.At <= windowStop)
            .ToList();

        var averagedTypes = new HashSet<long>();
        var stamp = windowStop.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        foreach (var group in inWindow.GroupBy(r => r.TypeId))
        {
            var buys = group.Where(r => r.Buy is not null).Select(r => r.Buy!.Value).ToList();
            var sells = group.Where(r => r.Sell is not null).Select(r => r.Sell!.Value).ToList();
            double? avgBuy = buys.Count > 0 ? buys.Average() : null;
            double? avgSell = sells.Count > 0 ? sells.Average() : null;
            if (avgBuy is null && avgSell is null) continue; // nothing usable for this type

            await _db.ExecuteNonQueryAsync(
                "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) VALUES ($tid, $st, $sid, $at, $buy, $sell)",
                new Dictionary<string, object?>
                {
                    ["$tid"] = group.Key,
                    ["$st"] = stationId,
                    ["$sid"] = sessionId,
                    ["$at"] = stamp,
                    ["$buy"] = (object?)avgBuy ?? DBNull.Value,
                    ["$sell"] = (object?)avgSell ?? DBNull.Value,
                },
                ct).ConfigureAwait(false);
            averagedTypes.Add(group.Key);
        }

        // ---- Step 2: gap-fill at today's prices for whatever is still uncovered. ----
        // Rows the fetcher adds are newer than anything averaged, so the ISK
        // calculator will prefer them: a type the fetcher touched counts as
        // "fetched today" even if an averaged row (lacking the side the price
        // mode needs) was written for it. The id watermark tells the two apart.
        var watermark = await _db.QueryScalarAsync<long>(
            "SELECT COALESCE(MAX(marketdata_id), 0) FROM market_data", ct: ct).ConfigureAwait(false);
        await _fetcher.EnsurePricesAsync(typeIds, stationId, sessionId, priceMode, ct).ConfigureAwait(false);

        var fetchedTypes = (await _db.QueryAsync(
            "SELECT DISTINCT type_id FROM market_data WHERE session_id = $sid AND marketdata_id > $mark",
            static r => r.GetInt64(0),
            new Dictionary<string, object?> { ["$sid"] = sessionId, ["$mark"] = watermark },
            ct).ConfigureAwait(false)).ToHashSet();
        var averagedOnly = averagedTypes.Count(t => !fetchedTypes.Contains(t));
        var rowsAveraged = inWindow.Count(r => averagedTypes.Contains(r.TypeId) && !fetchedTypes.Contains(r.TypeId));
        var unpriced = typeIds.Count(t => !averagedTypes.Contains(t) && !fetchedTypes.Contains(t));

        var basis = (averagedOnly, fetchedTypes.Count) switch
        {
            (> 0, 0) => HistoricalPriceBasis.AveragedFromWindow,
            (> 0, > 0) => HistoricalPriceBasis.Mixed,
            _ => HistoricalPriceBasis.FetchedToday,
        };

        _log.LogInformation(
            "Historical pricing for session {SessionId} at station {StationId}: {Averaged} type(s) averaged from {Rows} window row(s), {Fetched} fetched today, {Unpriced} unpriced ({Basis}).",
            sessionId, stationId, averagedOnly, rowsAveraged, fetchedTypes.Count, unpriced, basis);

        return new HistoricalPricingResult(sessionId, stationId, basis, averagedOnly, rowsAveraged, fetchedTypes.Count, unpriced);
    }

    private static DateTimeOffset ParseUtc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
