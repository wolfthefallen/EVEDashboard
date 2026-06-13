namespace EveDashboard.MarketData.Pricing;

using System.Globalization;
using System.Text;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IMarketPriceFetcher"/>. Subscribes to
/// <see cref="IActiveSessionContext.ActiveSessionChanged"/> for the session-start
/// (and resume) prefetch, and to <see cref="ISettingsRepository.SettingChanged"/>
/// so a mid-session pricing-station change re-prices the running session. Exposes
/// <see cref="EnsurePricesAsync"/> for the live ISK calculator's lazy, gaps-only
/// fetches.
/// </summary>
/// <remarks>
/// <para>
/// Every inserted row is stamped with the active session's <c>session_id</c>
/// (prices are session-bound; the ISK read keys on <c>session_id</c> +
/// <c>type_id</c> only, station-blind). <c>station_id</c> tells the API which hub
/// to query and is kept on each row as provenance.
/// </para>
/// <para>
/// Fetch policy:
/// <list type="bullet">
///   <item><description>
///     <b>Session start / resume</b> (<see cref="FetchForSessionStartAsync"/>, via
///     <see cref="IActiveSessionContext.ActiveSessionChanged"/> into Running):
///     unconditional full pull of every refined-material type id at the selected
///     station.
///   </description></item>
///   <item><description>
///     <b>Pricing-station change</b> while Running (the <c>selected_station_id</c>
///     <see cref="ISettingsRepository.SettingChanged"/>): the same unconditional
///     full pull at the new hub, so the session re-prices to it. The 15-minute rule
///     is bypassed (every material is re-fetched and re-stamped).
///   </description></item>
///   <item><description>
///     <b>Live lazy</b> (<see cref="EnsurePricesAsync"/>, from
///     <see cref="IskCalculator"/>): fetches ONLY materials with no usable price
///     for the active <see cref="PriceMode"/> ("gaps"), and only when no fetch was
///     attempted for that (session, type) within the last 15 minutes. A
///     fully-priced session makes zero live fetches.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The 15-minute throttle is tracked in an in-memory attempt map (no sentinel rows
/// are ever written to <c>market_data</c>); it is stamped on every fetch attempt,
/// including ones the API returns nothing for, so a genuinely-priceless material is
/// retried at most once per 15 minutes rather than every tick.
/// </para>
/// <para>
/// Persistence: returned <see cref="MarketPrice"/> rows are INSERTed (no UPDATE);
/// old rows are immutable history (see <c>docs/market-schema.md</c>).
/// </para>
/// </remarks>
public sealed class MarketPriceFetcher : IMarketPriceFetcher, IDisposable
{
    /// <summary>
    /// An uncovered (no usable price for the active mode) material is re-fetched at
    /// most once per this window per (session, type id) -- decision 1.1 = D. A
    /// material that already has a usable price is never re-fetched by the live path.
    /// </summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(15);

    private readonly IMarketPriceClient    _client;
    private readonly ISqliteDatabase       _sessionDb;
    private readonly ISdeLookup            _sdeLookup;
    private readonly ISettingsRepository   _settings;
    private readonly IActiveSessionContext _activeSession;
    private readonly ILogger<MarketPriceFetcher> _log;
    private bool _disposed;

    private readonly object _attemptLock = new();

    // (sessionId, typeId) -> last fetch ATTEMPT time (UTC). Stamped on every
    // attempt, even when the API returned no data, so a genuinely-priceless
    // material is retried at most once per RetryWindow instead of every tick
    // (decision 1.1 = D). In-memory only: a restart re-runs the unconditional start
    // prefetch (decision 1.3 = B), re-stamping everything. No sentinel row is ever
    // written to market_data (user-confirmed: no null-holder rows in the DB).
    private readonly Dictionary<(string SessionId, long TypeId), DateTimeOffset> _lastAttempt = new();

    /// <summary>Construct the fetcher.</summary>
    /// <param name="client">Underlying HTTP price client.</param>
    /// <param name="sessionDb">Session SQLite wrapper (NOT the SDE DB) — owns <c>market_data</c>.</param>
    /// <param name="sdeLookup">SDE lookup, used to derive the prefetch type-id list.</param>
    /// <param name="settings">Settings repository: read for <c>selected_station_id</c>, and subscribed via <see cref="ISettingsRepository.SettingChanged"/> so a mid-session station change re-fetches at the new hub.</param>
    /// <param name="activeSession">Active-session context. Subscribed in the ctor for the session-start/resume prefetch; unsubscribed in <see cref="Dispose"/>.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public MarketPriceFetcher(
        IMarketPriceClient    client,
        ISqliteDatabase       sessionDb,
        ISdeLookup            sdeLookup,
        ISettingsRepository   settings,
        IActiveSessionContext activeSession,
        ILogger<MarketPriceFetcher> log)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sessionDb);
        ArgumentNullException.ThrowIfNull(sdeLookup);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(log);
        _client = client;
        _sessionDb = sessionDb;
        _sdeLookup = sdeLookup;
        _settings = settings;
        _activeSession = activeSession;
        _log = log;

        _activeSession.ActiveSessionChanged += OnActiveSessionChanged;
        _settings.SettingChanged += OnSettingChanged;
    }

    /// <inheritdoc />
    public async Task FetchForSessionStartAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var stationId = await _settings.GetSelectedStationIdAsync(ct).ConfigureAwait(false);
        var typeIds = _sdeLookup.GetRefinedMaterialTypeIds();
        if (typeIds.Count == 0)
        {
            _log.LogWarning(
                "Session {SessionId} prefetch: SDE returned 0 refined-material type ids; nothing to fetch.",
                sessionId);
            return;
        }

        // Memory tidiness: only one session is active at a time, so drop attempt
        // entries for any other session. On Resume / station-change sessionId ==
        // current, so nothing is pruned. Not load-bearing for correctness.
        lock (_attemptLock)
        {
            var stale = _lastAttempt.Keys.Where(k => k.SessionId != sessionId).ToList();
            foreach (var key in stale)
            {
                _lastAttempt.Remove(key);
            }
        }

        _log.LogInformation(
            "Prefetching prices: stationId={StationId} typeIdCount={Count} sessionId={SessionId}",
            stationId, typeIds.Count, sessionId);

        // The session-start / resume / station-change fetch is unconditional: fetch
        // every refined-material id (decisions 1.3 = B and 1.4) and stamp the attempt
        // map so the live lazy path is throttled for the whole material set.
        var inserted = await FetchInsertAndStampAsync(
            typeIds, stationId, sessionId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

        _log.LogInformation(
            "Prefetch complete: inserted {Inserted} of {Requested} requested rows.",
            inserted, typeIds.Count);
    }

    /// <inheritdoc />
    public async Task EnsurePricesAsync(
        IReadOnlyList<long> typeIds,
        long                stationId,
        string              sessionId,
        PriceMode           priceMode,
        CancellationToken   ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeIds);
        ArgumentNullException.ThrowIfNull(sessionId);
        if (typeIds.Count == 0)
        {
            return;
        }

        var distinct = new HashSet<long>(typeIds).ToList();
        var latest = await GetLatestPricesAsync(distinct, sessionId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        // Two independent gates per requested id (decisions 1.1 = D, 1.2 = B):
        //  1. Coverage: if the latest captured price is usable for the active mode,
        //     never re-fetch.
        //  2. Attempt throttle: an uncovered id is fetched only if no attempt was
        //     made for (session, type) within RetryWindow.
        var toFetch = new List<long>();
        lock (_attemptLock)
        {
            foreach (var id in distinct)
            {
                if (latest.TryGetValue(id, out var price) && IsCovered(price.Buy, price.Sell, priceMode))
                {
                    continue;
                }
                if (_lastAttempt.TryGetValue((sessionId, id), out var attemptedAt)
                    && (now - attemptedAt) < RetryWindow)
                {
                    continue;
                }
                toFetch.Add(id);
            }
        }

        if (toFetch.Count == 0)
        {
            _log.LogDebug(
                "EnsurePrices: every requested type id is covered or within the {Window} retry window; skipping HTTP. stationId={StationId}",
                RetryWindow, stationId);
            return;
        }

        await FetchInsertAndStampAsync(toFetch, stationId, sessionId, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the latest captured (buy, sell) pair is a usable price for
    /// <paramref name="mode"/> -- i.e. the material is NOT a gap (decision 1.2 = B).
    /// A price is "usable" only when it is non-null and strictly positive; null or
    /// &lt;= 0 is treated as no price. For Split, the pair is covered if EITHER side
    /// is usable, so a Split gap requires BOTH sides to be 0/null.
    /// </summary>
    private static bool IsCovered(double? buy, double? sell, PriceMode mode)
    {
        static bool Usable(double? v) => v is > 0;
        return mode switch
        {
            PriceMode.Sell  => Usable(sell),
            PriceMode.Split => Usable(buy) || Usable(sell),
            _               => Usable(buy),   // Buy (default)
        };
    }

    /// <summary>
    /// Read the latest captured <c>(isk_buy, isk_sell)</c> per requested type id for
    /// the session (station-blind, mirroring the ISK read). Type ids with no row are
    /// absent from the result.
    /// </summary>
    private async Task<Dictionary<long, (double? Buy, double? Sell)>> GetLatestPricesAsync(
        IReadOnlyList<long> typeIds,
        string              sessionId,
        CancellationToken   ct)
    {
        var result = new Dictionary<long, (double? Buy, double? Sell)>();
        if (typeIds.Count == 0)
        {
            return result;
        }

        // Build "$tid0, $tid1, ..." placeholders so the IN-clause is parameterised.
        // Latest row per type within the session; station is NOT a filter (a
        // session's prices are station-blind on read).
        var sb = new StringBuilder();
        sb.Append(
            "SELECT md.type_id, md.isk_buy, md.isk_sell FROM market_data md " +
            "JOIN (SELECT type_id, MAX(fetched_at) AS latest FROM market_data " +
            "WHERE session_id = $session AND type_id IN (");
        var parameters = new Dictionary<string, object?> { ["$session"] = sessionId };
        for (int i = 0; i < typeIds.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var name = "$tid" + i.ToString(CultureInfo.InvariantCulture);
            sb.Append(name);
            parameters[name] = typeIds[i];
        }
        sb.Append(") GROUP BY type_id) lat ON md.type_id = lat.type_id AND md.fetched_at = lat.latest ");
        sb.Append("WHERE md.session_id = $session");

        var rows = await _sessionDb.QueryAsync(
            sb.ToString(),
            static r => (
                TypeId: r.GetInt64(0),
                Buy:  r.IsDBNull(1) ? (double?)null : r.GetDouble(1),
                Sell: r.IsDBNull(2) ? (double?)null : r.GetDouble(2)),
            parameters,
            ct).ConfigureAwait(false);

        foreach (var row in rows)
        {
            // Dict assignment collapses any rare fetched_at tie to last-write-wins;
            // both tied rows are equally "latest", so coverage is unaffected.
            result[row.TypeId] = (row.Buy, row.Sell);
        }
        return result;
    }

    /// <summary>
    /// Fetch the given type ids at the station and INSERT returned rows, stamping
    /// the attempt map for EVERY requested id (including ones the API omits) so the
    /// 15-minute throttle has a timestamp even when no row is written.
    /// </summary>
    /// <returns>The number of rows inserted (i.e. ids the API returned data for).</returns>
    private async Task<int> FetchInsertAndStampAsync(
        IReadOnlyList<long> typeIds,
        long                stationId,
        string              sessionId,
        DateTimeOffset      attemptTime,
        CancellationToken   ct)
    {
        var distinct = new HashSet<long>(typeIds).ToList();
        if (distinct.Count == 0)
        {
            return 0;
        }

        // Stamp the attempt up front for every requested id so a still-unpriced
        // material is throttled to one attempt per RetryWindow (decision 1.1 = D).
        lock (_attemptLock)
        {
            foreach (var id in distinct)
            {
                _lastAttempt[(sessionId, id)] = attemptTime;
            }
        }

        var rows = await _client.FetchPricesAsync(stationId, distinct, ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var row in rows)
        {
            await _sessionDb.ExecuteNonQueryAsync(
                """
                INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell)
                VALUES ($tid, $sid, $session, $fetched, $buy, $sell)
                """,
                new Dictionary<string, object?>
                {
                    ["$tid"]     = row.TypeId,
                    ["$sid"]     = row.StationId,
                    ["$session"] = sessionId,
                    ["$fetched"] = row.FetchedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["$buy"]     = (object?)row.IskBuy ?? DBNull.Value,
                    ["$sell"]    = (object?)row.IskSell ?? DBNull.Value,
                },
                ct).ConfigureAwait(false);
        }
        return rows.Count;
    }

    private async void OnActiveSessionChanged(object? sender, EventArgs e)
    {
        try
        {
            var sid = _activeSession.ActiveSessionId;
            var state = _activeSession.State;
            if (sid is null || state != SessionRunState.Running)
            {
                return;
            }
            await Task.Run(async () =>
            {
                try
                {
                    await FetchForSessionStartAsync(sid).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Session-start prefetch failed for sessionId={SessionId}; continuing.", sid);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive — the handler must not throw back into the event source.
            _log.LogWarning(ex, "MarketPriceFetcher.OnActiveSessionChanged threw; suppressed.");
        }
    }

    private async void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        try
        {
            // Decision 1.4: a mid-session pricing-station change re-fetches all
            // materials at the new hub (only while Running; a change while Paused is
            // picked up by the unconditional refetch on the next Resume).
            if (e.Key != "selected_station_id")
            {
                return;
            }
            var sid = _activeSession.ActiveSessionId;
            if (sid is null || _activeSession.State != SessionRunState.Running)
            {
                return;
            }
            await Task.Run(async () =>
            {
                try
                {
                    await FetchForSessionStartAsync(sid).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Station-change refetch failed for sessionId={SessionId}; continuing.", sid);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive — the handler must not throw back into the event source.
            _log.LogWarning(ex, "MarketPriceFetcher.OnSettingChanged threw; suppressed.");
        }
    }

    /// <summary>
    /// Unsubscribe from <see cref="IActiveSessionContext.ActiveSessionChanged"/> and
    /// <see cref="ISettingsRepository.SettingChanged"/>. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _activeSession.ActiveSessionChanged -= OnActiveSessionChanged; }
        catch { /* defensive */ }
        try { _settings.SettingChanged -= OnSettingChanged; }
        catch { /* defensive */ }
    }
}
