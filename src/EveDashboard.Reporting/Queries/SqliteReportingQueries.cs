namespace EveDashboard.Reporting.Queries;

using System.Data.Common;
using System.Globalization;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IReportingQueries"/> implementation backed by an injected
/// <see cref="ISqliteDatabase"/> over the session SQLite file. Composes
/// <see cref="ISessionStatsRepository"/>, <see cref="ISdeLookup"/>, and
/// <see cref="IIskCalculator"/> for per-session and per-ore aggregations.
/// </summary>
/// <remarks>
/// Holds no mutable state; safe to register as a singleton in DI alongside the
/// singleton <see cref="ISqliteDatabase"/>.
/// </remarks>
public sealed class SqliteReportingQueries : IReportingQueries
{
    private readonly ISqliteDatabase          _db;
    private readonly ISessionStatsRepository  _statsRepo;
    private readonly ISdeLookup               _sdeLookup;
    private readonly IIskCalculator           _iskCalculator;
    private readonly ISettingsRepository      _settings;
    private readonly ILogger<SqliteReportingQueries> _log;

    /// <summary>Construct a reporting-queries facade over the session SQLite database.</summary>
    /// <param name="db">Session SQLite wrapper.</param>
    /// <param name="statsRepo">Session-stats repository — reused per saved session for total-ISK + active-duration.</param>
    /// <param name="sdeLookup">SDE lookup — for resolving <c>type_id</c> to display name.</param>
    /// <param name="iskCalculator">Plan-18 ISK pipeline; called once per <see cref="GetOreBreakdownAsync"/> with the full per-(type_id) units dict.</param>
    /// <param name="settings">Settings repository — read once per <see cref="GetOreBreakdownAsync"/> for refining yield, price mode, and selected station.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SqliteReportingQueries(
        ISqliteDatabase          db,
        ISessionStatsRepository  statsRepo,
        ISdeLookup               sdeLookup,
        IIskCalculator           iskCalculator,
        ISettingsRepository      settings,
        ILogger<SqliteReportingQueries> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(statsRepo);
        ArgumentNullException.ThrowIfNull(sdeLookup);
        ArgumentNullException.ThrowIfNull(iskCalculator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _statsRepo = statsRepo;
        _sdeLookup = sdeLookup;
        _iskCalculator = iskCalculator;
        _settings = settings;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummaryRow>> GetAllSessionSummariesAsync(CancellationToken ct = default)
    {
        // Step 1: enumerate sessions. Plan 38: sha256 ids are not chronological,
        // so the newest-first ordering is applied below by StartTime (decision
        // 3.2 = A), not by ORDER BY session_id.
        var sessions = await _db.QueryAsync(
            "SELECT session_id, name FROM session",
            static r => (Id: r.GetString(0), Name: r.GetString(1)),
            ct: ct).ConfigureAwait(false);

        if (sessions.Count == 0)
        {
            return Array.Empty<SessionSummaryRow>();
        }

        var asOf = DateTimeOffset.UtcNow;
        var rows = new List<SessionSummaryRow>(sessions.Count);
        foreach (var session in sessions)
        {
            // Per-session metadata: start time + pilot count are cheap;
            // ActiveDuration + TotalIskMined come from the existing snapshot.
            var startTimes = await _db.QueryAsync(
                StartTimeSql,
                static r => r.IsDBNull(0) ? null : r.GetString(0),
                new Dictionary<string, object?> { ["$sid"] = session.Id },
                ct).ConfigureAwait(false);
            var rawStart = startTimes.Count > 0 ? startTimes[0] : null;
            var startTime = rawStart is null
                ? DateTimeOffset.MinValue
                : DateTimeOffset.Parse(
                    rawStart,
                    CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

            var pilotCount = (int)await _db.QueryScalarAsync<long>(
                "SELECT COUNT(*) FROM session_pilot WHERE session_id = $sid",
                new Dictionary<string, object?> { ["$sid"] = session.Id },
                ct).ConfigureAwait(false);

            var snapshot = await _statsRepo
                .GetSessionStatsAsync(session.Id, asOf, allowLazyFetch: false, ct)
                .ConfigureAwait(false);

            rows.Add(new SessionSummaryRow(
                SessionId:      session.Id,
                Name:           session.Name,
                StartTime:      startTime,
                ActiveDuration: snapshot.TotalTimeMined,
                PilotCount:     pilotCount,
                TotalIskMined:  snapshot.TotalIskMined));
        }

        // Plan 38 (decision 3.2 = A): present newest-first by mining start time.
        rows.Sort((a, b) => b.StartTime.CompareTo(a.StartTime));
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PilotSummaryRow>> GetPilotsForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync(
            EnumerateSessionPilotsSql,
            static r => new PilotSummaryRow(r.GetInt64(0), r.GetString(1), r.GetString(2)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct).ConfigureAwait(false);
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OreBreakdownRow>> GetOreBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default)
    {
        var sql = pilotId is null ? OreBreakdownSessionSql : OreBreakdownPerPilotSql;
        var parameters = new Dictionary<string, object?> { ["$sid"] = sessionId };
        if (pilotId is not null)
        {
            parameters["$pid"] = pilotId.Value;
        }

        var raw = await _db.QueryAsync(
            sql,
            static r => (TypeId: r.GetInt64(0), VolumeM3: r.GetDouble(1), Units: r.GetInt64(2)),
            parameters,
            ct).ConfigureAwait(false);

        if (raw.Count == 0)
        {
            return Array.Empty<OreBreakdownRow>();
        }

        // Build the per-(type_id) units dict for the ISK calculator. ONE call
        // returns per-input-type ISK; we read result.IskByInputType per row.
        var unitsByType = new Dictionary<long, long>(raw.Count);
        foreach (var r in raw)
        {
            unitsByType[r.TypeId] = r.Units;
        }

        var stationId            = await _settings.GetSelectedStationIdAsync(ct).ConfigureAwait(false);
        var refiningYieldPercent = await _settings.GetRefiningYieldPercentAsync(ct).ConfigureAwait(false);
        var priceMode            = await _settings.GetPriceModeAsync(ct).ConfigureAwait(false);

        var iskResult = await _iskCalculator
            .ComputeAsync(unitsByType, stationId, sessionId, allowLazyFetch: false, refiningYieldPercent, priceMode, ct)
            .ConfigureAwait(false);

        var rows = new List<OreBreakdownRow>(raw.Count);
        foreach (var r in raw)
        {
            var name = _sdeLookup.TryGetProduct(r.TypeId)?.Name
                       ?? string.Create(CultureInfo.InvariantCulture, $"#{r.TypeId}");
            iskResult.IskByInputType.TryGetValue(r.TypeId, out var isk);
            rows.Add(new OreBreakdownRow(
                TypeId:   r.TypeId,
                TypeName: name,
                VolumeM3: r.VolumeM3,
                Isk:      isk));
        }

        // Already ordered by m^3 desc by the SQL ORDER BY clause, but assign here too
        // to make the contract explicit and to survive any future SQL change.
        rows.Sort(static (a, b) => b.VolumeM3.CompareTo(a.VolumeM3));
        return rows;
    }

    private const string StartTimeSql = """
        SELECT MIN(start_time)
        FROM session_time
        WHERE session_id = $sid AND is_pause = 0
        """;

    // Mirrors PerPilotWidgetCoordinator.EnumerateSessionPilotsSql so live and
    // saved coordinators see the same name + ordering.
    // Plan 35: adds sp.role so PilotSummaryRow carries the current role into Excel Summary.
    private const string EnumerateSessionPilotsSql = """
        SELECT sp.pilot_id, p.name, sp.role
        FROM session_pilot sp
        INNER JOIN pilot p ON p.pilot_id = sp.pilot_id
        WHERE sp.session_id = $sid
        ORDER BY p.name COLLATE NOCASE ASC
        """;

    // Plan 35: ore breakdown queries read from eligible_log (the CTE filters by role
    // per RoleEligibilityCte.Sql). The $sid parameter is consumed by the CTE.
    private const string OreBreakdownSessionSql = RoleEligibilityCte.Sql + """

        SELECT type_id,
               COALESCE(SUM(volume_m3), 0) AS m3,
               COALESCE(SUM(units), 0)     AS units
        FROM eligible_log
        WHERE type_id IS NOT NULL
        GROUP BY type_id
        ORDER BY m3 DESC
        """;

    private const string OreBreakdownPerPilotSql = RoleEligibilityCte.Sql + """

        SELECT type_id,
               COALESCE(SUM(volume_m3), 0) AS m3,
               COALESCE(SUM(units), 0)     AS units
        FROM eligible_log
        WHERE pilot_id = $pid AND type_id IS NOT NULL
        GROUP BY type_id
        ORDER BY m3 DESC
        """;

    /// <inheritdoc />
    public async Task<SessionTimeline> GetSessionTimelineAsync(string sessionId, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync(
            SessionTimelineSql,
            static r => (
                Start:   r.GetString(0),
                Stop:    r.IsDBNull(1) ? null : r.GetString(1),
                IsPause: r.GetInt64(2) != 0),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct).ConfigureAwait(false);

        var sessionStart    = DateTimeOffset.MinValue;
        var activeTotal     = TimeSpan.Zero;
        var pauseWindows    = new List<PauseWindowRow>();
        var sessionStartSet = false;

        foreach (var row in rows)
        {
            var start = DateTimeOffset.Parse(
                row.Start,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

            if (!row.IsPause && !sessionStartSet)
            {
                sessionStart = start;
                sessionStartSet = true;
            }

            if (row.Stop is null) continue; // open interval — saved sessions don't have them, but defensive

            var stop = DateTimeOffset.Parse(
                row.Stop,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

            if (row.IsPause)
            {
                pauseWindows.Add(new PauseWindowRow(start, stop));
            }
            else
            {
                activeTotal += stop - start;
            }
        }

        return new SessionTimeline(sessionStart, activeTotal, pauseWindows);
    }

    private const string SessionTimelineSql = """
        SELECT start_time, stop_time, is_pause
        FROM session_time
        WHERE session_id = $sid
        ORDER BY start_time ASC
        """;
}
