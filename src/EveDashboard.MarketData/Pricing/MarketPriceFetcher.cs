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
/// <see cref="IActiveSessionContext.ActiveSessionChanged"/> for session-start
/// prefetch and exposes <see cref="EnsurePricesAsync"/> for synchronous lazy
/// fetches from inside <see cref="IskCalculator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Plan 38: every inserted row is stamped with the active session's
/// <c>session_id</c>. The 60-second throttle is scoped to that session
/// (<c>... WHERE session_id = $session AND type_id IN (...)</c>) for lazy
/// fetches, and is BYPASSED entirely for the session-start fetch
/// (<see cref="FetchForSessionStartAsync"/>) so a new session always captures
/// its own prices from t0. <c>station_id</c> is used only to tell the API which
/// hub to query (and is stored on each row as provenance); it is never a query
/// filter here or in the ISK read.
/// </para>
/// <para>
/// Persistence: returned <see cref="MarketPrice"/> rows are INSERTed (no
/// UPDATE) per <c>docs/market-schema.md</c>. Old rows are immutable history.
/// </para>
/// </remarks>
public sealed class MarketPriceFetcher : IMarketPriceFetcher, IDisposable
{
    private const int ThrottleSeconds = 60;

    private readonly IMarketPriceClient    _client;
    private readonly ISqliteDatabase       _sessionDb;
    private readonly ISdeLookup            _sdeLookup;
    private readonly ISettingsRepository   _settings;
    private readonly IActiveSessionContext _activeSession;
    private readonly ILogger<MarketPriceFetcher> _log;
    private bool _disposed;

    /// <summary>Construct the fetcher.</summary>
    /// <param name="client">Underlying HTTP price client.</param>
    /// <param name="sessionDb">Session SQLite wrapper (NOT the SDE DB) — owns <c>market_data</c>.</param>
    /// <param name="sdeLookup">SDE lookup, used to derive the prefetch type-id list.</param>
    /// <param name="settings">Settings repository, used to read <c>selected_station_id</c>.</param>
    /// <param name="activeSession">Active-session context. Subscribed in the ctor; unsubscribed in <see cref="Dispose"/>.</param>
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

        _log.LogInformation(
            "Prefetching prices: stationId={StationId} typeIdCount={Count} sessionId={SessionId}",
            stationId, typeIds.Count, sessionId);

        // Plan 38: the session-start fetch is unconditional (bypassThrottle:
        // true) so the new session owns accurate prices from t0, and every row
        // is stamped with this session's id.
        var (inserted, throttled) = await FetchAndInsertAsync(typeIds, stationId, sessionId, bypassThrottle: true, ct).ConfigureAwait(false);

        _log.LogInformation(
            "Prefetch complete: inserted {Inserted} rows, skipped {Throttled} throttled.",
            inserted, throttled);
    }

    /// <inheritdoc />
    public async Task EnsurePricesAsync(IReadOnlyList<long> typeIds, long stationId, string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeIds);
        ArgumentNullException.ThrowIfNull(sessionId);
        if (typeIds.Count == 0)
        {
            return;
        }
        await FetchAndInsertAsync(typeIds, stationId, sessionId, bypassThrottle: false, ct).ConfigureAwait(false);
    }

    private async Task<(int Inserted, int Throttled)> FetchAndInsertAsync(
        IReadOnlyList<long> typeIds,
        long                stationId,
        string              sessionId,
        bool                bypassThrottle,
        CancellationToken   ct)
    {
        var distinct = new HashSet<long>(typeIds).ToList();
        List<long> toFetch;
        if (bypassThrottle)
        {
            // Plan 38: session-start fetch — fetch every requested type id
            // regardless of how recently another session fetched it.
            toFetch = distinct;
        }
        else
        {
            var fresh = await GetFreshTypeIdsAsync(distinct, sessionId, ct).ConfigureAwait(false);
            toFetch = distinct.Where(id => !fresh.Contains(id)).ToList();
        }
        if (toFetch.Count == 0)
        {
            _log.LogDebug(
                "Throttled — every requested type id has a market_data row younger than {Seconds} s; skipping HTTP. stationId={StationId}",
                ThrottleSeconds, stationId);
            return (0, distinct.Count);
        }

        var rows = await _client.FetchPricesAsync(stationId, toFetch, ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return (0, distinct.Count - toFetch.Count);
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
        return (rows.Count, distinct.Count - toFetch.Count);
    }

    private async Task<HashSet<long>> GetFreshTypeIdsAsync(
        IReadOnlyList<long> typeIds,
        string              sessionId,
        CancellationToken   ct)
    {
        var fresh = new HashSet<long>();
        if (typeIds.Count == 0)
        {
            return fresh;
        }

        // Build "$tid0, $tid1, ..." parameter placeholders so the IN-clause is
        // safely parameterised (no string interpolation of user input).
        // Plan 38: scope the freshness check to THIS session so a back-to-back
        // session is never starved by a previous session's "fresh" rows.
        // Station is NOT a filter (plan 38 amendment): a session's prices are
        // station-blind on read, so freshness is per (session, type) only.
        var sb = new StringBuilder();
        sb.Append(
            "SELECT type_id, MAX(fetched_at) AS latest FROM market_data " +
            "WHERE session_id = $session AND type_id IN (");
        var parameters = new Dictionary<string, object?> { ["$session"] = sessionId };
        for (int i = 0; i < typeIds.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var name = "$tid" + i.ToString(CultureInfo.InvariantCulture);
            sb.Append(name);
            parameters[name] = typeIds[i];
        }
        sb.Append(") GROUP BY type_id");

        var rows = await _sessionDb.QueryAsync(
            sb.ToString(),
            static r => (TypeId: r.GetInt64(0), Latest: r.GetString(1)),
            parameters,
            ct).ConfigureAwait(false);

        var threshold = DateTimeOffset.UtcNow
            .AddSeconds(-ThrottleSeconds)
            .ToString("O", CultureInfo.InvariantCulture);
        foreach (var row in rows)
        {
            if (string.Compare(row.Latest, threshold, StringComparison.Ordinal) >= 0)
            {
                fresh.Add(row.TypeId);
            }
        }
        return fresh;
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

    /// <summary>Unsubscribe from <see cref="IActiveSessionContext.ActiveSessionChanged"/>. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _activeSession.ActiveSessionChanged -= OnActiveSessionChanged; }
        catch { /* defensive */ }
    }
}
