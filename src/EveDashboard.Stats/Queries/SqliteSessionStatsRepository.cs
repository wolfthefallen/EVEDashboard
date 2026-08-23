namespace EveDashboard.Stats.Queries;

using System.Collections.Immutable;
using System.Data.Common;
using System.Globalization;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ISessionStatsRepository"/> implementation backed by an
/// injected <see cref="ISqliteDatabase"/>. Runs five SQL queries per snapshot
/// (existence check, active duration, session aggregates, per-pilot
/// aggregates, per-(pilot,type_id) units totals) and assembles the result into
/// immutable record snapshots, calling <see cref="IIskCalculator"/> per pilot.
/// </summary>
/// <remarks>
/// Holds no mutable state — safe to register as a singleton in DI alongside
/// the singleton <see cref="ISqliteDatabase"/> (both are wired up at
/// <c>App.xaml.cs OnStartup</c>). ISK figures are computed via
/// <see cref="IIskCalculator"/>; the <c>EmptySnapshot</c> fallback keeps its
/// <c>0.0</c> literals because no session means no ISK.
/// </remarks>
public sealed class SqliteSessionStatsRepository : ISessionStatsRepository
{
    private readonly ISqliteDatabase _db;
    private readonly IIskCalculator _iskCalculator;
    private readonly ISettingsRepository _settings;
    private readonly ISdeLookup _sdeLookup;
    private readonly ILogger<SqliteSessionStatsRepository> _log;

    /// <summary>
    /// Construct a repository over the given SQLite database.
    /// </summary>
    /// <param name="db">SQLite database wrapper. Typically the singleton
    ///     <c>SqliteDatabase</c> registered at app startup against the
    ///     session SQLite file.</param>
    /// <param name="iskCalculator">Calculator that turns per-pilot mined units into ISK.</param>
    /// <param name="settings">Settings repository — read once per snapshot for refining yield, price mode, and selected station.</param>
    /// <param name="sdeLookup">SDE lookup — resolves refined-material type ids to display names for the "Refined output" section.</param>
    /// <param name="log">Logger for diagnostics — Debug per call, Warning
    ///     when called with a missing <c>sessionId</c>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if any argument is null.
    /// </exception>
    public SqliteSessionStatsRepository(
        ISqliteDatabase db,
        IIskCalculator iskCalculator,
        ISettingsRepository settings,
        ISdeLookup sdeLookup,
        ILogger<SqliteSessionStatsRepository> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(iskCalculator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sdeLookup);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _iskCalculator = iskCalculator;
        _settings = settings;
        _sdeLookup = sdeLookup;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<SessionStats> GetSessionStatsAsync(
        string sessionId,
        DateTimeOffset asOf,
        bool allowLazyFetch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        // Query 0: existence check - on a missing session, log Warning and return empty SessionStats.
        var sidOnly = new Dictionary<string, object?> { ["$sid"] = sessionId };
        var exists = await _db.QueryScalarAsync<long>(SessionExistsSql, sidOnly, ct);
        if (exists == 0)
        {
            _log.LogWarning(
                "GetSessionStatsAsync called with sessionId={SessionId} but no such row in the session table - returning empty stats. No active session could be found.",
                sessionId);
            return EmptySnapshot(sessionId, asOf);
        }

        var asOfIso = asOf.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var p = new Dictionary<string, object?>
        {
            ["$sid"]  = sessionId,
            ["$asOf"] = asOfIso,
        };

        // Query 1: active duration in seconds (QueryAsync<double> with an explicit
        // mapper, since QueryScalarAsync<double> is not in the wrapper's tested scalar-type set)
        var activeRows = await _db.QueryAsync(
            ActiveDurationSql,
            static reader => reader.GetDouble(0),
            p,
            ct);
        var activeSeconds = activeRows[0]; // SQL uses COALESCE(SUM(...), 0) - always one row
        var totalTimeMined = TimeSpan.FromSeconds(activeSeconds);

        // Query 2: session-level aggregates (single row)
        var sessionRows = await _db.QueryAsync(
            SessionAggregatesSql,
            MapAggregateRow,
            p,
            ct);
        var sessionRow = sessionRows[0];

        // Rates default to 0 when the denominator is 0.
        var sessionM3PerSec = activeSeconds > 0
            ? sessionRow.TotalM3 / activeSeconds
            : 0.0;
        var sessionCritChance = (sessionRow.CritCount + sessionRow.TickCount) > 0
            ? (double)sessionRow.CritCount / (sessionRow.CritCount + sessionRow.TickCount)
            : 0.0;

        // Query 3: per-pilot aggregates (LEFT JOIN ensures empty pilots show up)
        var pilotRows = await _db.QueryAsync(
            PerPilotSql,
            MapPerPilotRow,
            p,
            ct);

        // Query 4: per-(pilot, type_id) units totals — drives ISK calc.
        // A single query returns BOTH total_units and crit_units per
        // (pilot, type_id) so the two-dict IIskCalculator overload can derive Total
        // ISK and Crit ISK in one pass per pilot (shared price-cache, exact math).
        var perPilotPerType = await _db.QueryAsync(
            PerPilotPerTypeWithCritsSql,
            static r => (
                PilotId:    r.GetInt64(0),
                TypeId:     r.GetInt64(1),
                Units:      r.GetInt64(2),
                CritUnits:  r.GetInt64(3)),
            p,
            ct);

        // Read settings ONCE per snapshot so every per-pilot ISK call sees the same values.
        var refiningYieldPercent = await _settings.GetRefiningYieldPercentAsync(ct);
        var priceMode            = await _settings.GetPriceModeAsync(ct);
        var stationId            = await _settings.GetSelectedStationIdAsync(ct);

        var pilotIsk     = new Dictionary<long, double>();
        var pilotCritIsk = new Dictionary<long, double>();
        // Per-pilot refined output (name-resolved, sorted) + the
        // session-wide sum of every pilot's refined map (mirrors how session ISK
        // is the sum of per-pilot ISK — no second ISK call).
        var pilotRefined     = new Dictionary<long, IReadOnlyList<RefinedMaterialAmount>>();
        var sessionRefinedMap = new Dictionary<long, long>();
        // Per-material ISK summed across pilots, parallel to the quantity
        // map, so the session-wide refined list carries ISK alongside quantity.
        var sessionRefinedIskMap = new Dictionary<long, double>();
        foreach (var pilotGroup in perPilotPerType.GroupBy(r => r.PilotId))
        {
            var typeIdToUnits     = pilotGroup.ToDictionary(r => r.TypeId, r => r.Units);
            var typeIdToCritUnits = pilotGroup
                .Where(r => r.CritUnits > 0)
                .ToDictionary(r => r.TypeId, r => r.CritUnits);
            var iskResult = await _iskCalculator
                .ComputeAsync(typeIdToUnits, typeIdToCritUnits, stationId, sessionId, allowLazyFetch, refiningYieldPercent, priceMode, ct)
                .ConfigureAwait(false);
            pilotIsk[pilotGroup.Key]     = iskResult.TotalIsk;
            pilotCritIsk[pilotGroup.Key] = iskResult.CritIsk;

            pilotRefined[pilotGroup.Key] = BuildRefinedList(iskResult.RefinedQuantitiesByMaterial, iskResult.IskByMaterial);
            foreach (var (matId, qty) in iskResult.RefinedQuantitiesByMaterial)
            {
                sessionRefinedMap[matId] = sessionRefinedMap.GetValueOrDefault(matId) + qty;
            }
            foreach (var (matId, isk) in iskResult.IskByMaterial)
            {
                sessionRefinedIskMap[matId] = sessionRefinedIskMap.GetValueOrDefault(matId) + isk;
            }
        }
        var sessionRefined = BuildRefinedList(sessionRefinedMap, sessionRefinedIskMap);

        var sessionTotalIsk     = pilotIsk.Values.Sum();
        var sessionTotalCritIsk = pilotCritIsk.Values.Sum();
        var sessionCritIskPercent = sessionTotalIsk > 0
            ? sessionTotalCritIsk / sessionTotalIsk
            : 0.0;
        var activeHours = activeSeconds / 3600.0;
        var sessionIskPerHour = activeHours > 0 ? sessionTotalIsk / activeHours : 0.0;

        var byCharacter = new Dictionary<long, CharacterStats>(pilotRows.Count);
        foreach (var row in pilotRows)
        {
            var pilotM3PerSec = activeSeconds > 0
                ? row.TotalM3 / activeSeconds
                : 0.0;
            var pilotCritChance = (row.CritCount + row.TickCount) > 0
                ? (double)row.CritCount / (row.CritCount + row.TickCount)
                : 0.0;

            var pilotTotalIsk = pilotIsk.GetValueOrDefault(row.PilotId, 0.0);
            var pilotIskPerHour = activeHours > 0 ? pilotTotalIsk / activeHours : 0.0;
            var pilotTotalCritIsk = pilotCritIsk.GetValueOrDefault(row.PilotId, 0.0);
            var pilotCritIskPercent = pilotTotalIsk > 0
                ? pilotTotalCritIsk / pilotTotalIsk
                : 0.0;

            byCharacter[row.PilotId] = new CharacterStats(
                PilotId:            row.PilotId,
                LastEventAt:        row.LastEventAt,
                TotalM3Mined:       row.TotalM3,
                M3PerSecond:        pilotM3PerSec,
                TotalIskMined:      pilotTotalIsk,
                IskPerHour:         pilotIskPerHour,
                TotalIskFromCrits:  pilotTotalCritIsk,
                CritIskPercent:     pilotCritIskPercent,
                TotalM3FromCrits:   row.CritM3,
                CritCount:          row.CritCount,
                CritChance:         pilotCritChance,
                Role:               PilotRoleExtensions.Parse(row.Role))
            {
                RefinedOutput = pilotRefined.GetValueOrDefault(row.PilotId, Array.Empty<RefinedMaterialAmount>()),
            };
        }

        _log.LogDebug(
            "GetSessionStatsAsync(sessionId={SessionId}, asOf={AsOf}) -> {PilotCount} pilots, sessionIsk={SessionIsk}",
            sessionId,
            asOf,
            byCharacter.Count,
            sessionTotalIsk);

        return new SessionStats(
            SessionId:          sessionId,
            TotalTimeMined:     totalTimeMined,
            LastCalculated:     asOf,
            TotalM3Mined:       sessionRow.TotalM3,
            M3PerSecond:        sessionM3PerSec,
            TotalIskMined:      sessionTotalIsk,
            IskPerHour:         sessionIskPerHour,
            TotalIskFromCrits:  sessionTotalCritIsk,
            CritIskPercent:     sessionCritIskPercent,
            TotalM3FromCrits:   sessionRow.CritM3,
            CritCount:          sessionRow.CritCount,
            CritChance:         sessionCritChance,
            ByCharacter:        byCharacter)
        {
            RefinedOutput = sessionRefined,
        };
    }

    /// <summary>
    /// Turn a refined-material quantity map into a display list — name
    /// resolved via the SDE (falling back to <c>#&lt;typeId&gt;</c>), sorted by
    /// quantity descending. Each row's <see cref="RefinedMaterialAmount.Isk"/> is
    /// taken from <paramref name="iskMap"/>, defaulting to <c>0</c> for a
    /// material with no captured price. Returns an empty list for an empty
    /// <paramref name="qtyMap"/>.
    /// </summary>
    /// <param name="qtyMap">Refined-material type id -> yield-applied quantity.</param>
    /// <param name="iskMap">Refined-material type id -> yield-applied ISK value; a material absent here values at 0.</param>
    private IReadOnlyList<RefinedMaterialAmount> BuildRefinedList(
        IReadOnlyDictionary<long, long> qtyMap,
        IReadOnlyDictionary<long, double> iskMap)
    {
        if (qtyMap.Count == 0) return Array.Empty<RefinedMaterialAmount>();
        var list = new List<RefinedMaterialAmount>(qtyMap.Count);
        foreach (var (typeId, qty) in qtyMap)
        {
            var name = _sdeLookup.TryGetRefinedMaterialName(typeId)
                       ?? string.Create(CultureInfo.InvariantCulture, $"#{typeId}");
            list.Add(new RefinedMaterialAmount(typeId, name, qty)
            {
                Isk = iskMap.GetValueOrDefault(typeId, 0.0),
            });
        }
        // Quantity descending, ties broken by type_id ascending — a deterministic
        // order that matches RefinedOutputAggregator.SumAcross so the live/single
        // path and the compare/lifetime rollup never disagree on tied materials
        // (List.Sort is an unstable introsort, hence the explicit tie-break).
        list.Sort(static (a, b) =>
        {
            var byQty = b.Quantity.CompareTo(a.Quantity);
            return byQty != 0 ? byQty : a.TypeId.CompareTo(b.TypeId);
        });
        return list;
    }

    /// <summary>Empty snapshot returned when the requested session id is not found.</summary>
    private static SessionStats EmptySnapshot(string sessionId, DateTimeOffset asOf) =>
        new(
            SessionId:          sessionId,
            TotalTimeMined:     TimeSpan.Zero,
            LastCalculated:     asOf,
            TotalM3Mined:       0.0,
            M3PerSecond:        0.0,
            TotalIskMined:      0.0,
            IskPerHour:         0.0,
            TotalIskFromCrits:  0.0,
            CritIskPercent:     0.0,
            TotalM3FromCrits:   0.0,
            CritCount:          0,
            CritChance:         0.0,
            ByCharacter:        ImmutableDictionary<long, CharacterStats>.Empty);

    private static AggregateRow MapAggregateRow(DbDataReader reader) =>
        new(
            TotalM3:    reader.GetDouble(0),
            CritM3:     reader.GetDouble(1),
            CritCount:  reader.GetInt64(2),
            TickCount:  reader.GetInt64(3));

    private static PerPilotRow MapPerPilotRow(DbDataReader reader) =>
        new(
            PilotId:    reader.GetInt64(0),
            TotalM3:    reader.GetDouble(1),
            CritM3:     reader.GetDouble(2),
            CritCount:  reader.GetInt64(3),
            TickCount:  reader.GetInt64(4),
            LastEventAt: reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(5),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            Role:       reader.GetString(6));   // sp.role is appended to the PerPilotSql SELECT.

    private const string SessionExistsSql =
        "SELECT 1 FROM session WHERE session_id = $sid LIMIT 1";

    private const string ActiveDurationSql = """
        SELECT COALESCE(
            SUM((julianday(COALESCE(stop_time, $asOf)) - julianday(start_time)) * 86400),
            0)
        FROM session_time
        WHERE session_id = $sid AND is_pause = 0
        """;

    // Log-reading aggregate queries read from eligible_log (the CTE filters
    // by role per RoleEligibilityCte.Sql). The $sid parameter is consumed by the CTE;
    // the outer SELECT no longer needs its own WHERE session_id = $sid clause.
    private const string SessionAggregatesSql = RoleEligibilityCte.Sql + """

        SELECT
            COALESCE(SUM(volume_m3), 0)                                          AS total_m3,
            COALESCE(SUM(CASE WHEN event_kind = 'Crit' THEN volume_m3 END), 0)    AS crit_m3,
            COALESCE(SUM(CASE WHEN event_kind = 'Crit' THEN 1 ELSE 0 END), 0)     AS crit_count,
            COALESCE(SUM(CASE WHEN event_kind = 'Tick' THEN 1 ELSE 0 END), 0)     AS tick_count
        FROM eligible_log
        """;

    // Returns per-(pilot, type_id) BOTH the total mined units and the
    // crit-only subset in a single query so the repository can call the two-dict
    // IIskCalculator overload once per pilot (shared price-cache; exact crit
    // ISK with no proportional rounding drift).
    private const string PerPilotPerTypeWithCritsSql = RoleEligibilityCte.Sql + """

        SELECT
            pilot_id,
            type_id,
            COALESCE(SUM(units), 0)                                              AS total_units,
            COALESCE(SUM(CASE WHEN event_kind = 'Crit' THEN units ELSE 0 END), 0) AS crit_units
        FROM eligible_log
        WHERE type_id IS NOT NULL AND units IS NOT NULL
        GROUP BY pilot_id, type_id
        """;

    // The outer LEFT JOIN keeps Cyno/Combat pilots visible in byCharacter
    // (their stats are all zeros because eligible_log filtered their rows out),
    // and the SELECT carries sp.role so CharacterStats.Role can be populated.
    private const string PerPilotSql = RoleEligibilityCte.Sql + """

        SELECT
            sp.pilot_id,
            COALESCE(SUM(el.volume_m3), 0)                                          AS total_m3,
            COALESCE(SUM(CASE WHEN el.event_kind = 'Crit' THEN el.volume_m3 END), 0) AS crit_m3,
            SUM(CASE WHEN el.event_kind = 'Crit' THEN 1 ELSE 0 END)                 AS crit_count,
            SUM(CASE WHEN el.event_kind = 'Tick' THEN 1 ELSE 0 END)                 AS tick_count,
            MAX(el.eve_timestamp)                                                   AS last_event,
            sp.role                                                                  AS role
        FROM session_pilot sp
        LEFT JOIN eligible_log el ON el.session_id = sp.session_id AND el.pilot_id = sp.pilot_id
        WHERE sp.session_id = $sid
        GROUP BY sp.pilot_id, sp.role
        ORDER BY sp.pilot_id
        """;

    private sealed record AggregateRow(
        double  TotalM3,
        double  CritM3,
        long    CritCount,
        long    TickCount);

    private sealed record PerPilotRow(
        long             PilotId,
        double           TotalM3,
        double           CritM3,
        long             CritCount,
        long             TickCount,
        DateTimeOffset?  LastEventAt,
        string           Role);   // Source role string from session_pilot.role.
}
