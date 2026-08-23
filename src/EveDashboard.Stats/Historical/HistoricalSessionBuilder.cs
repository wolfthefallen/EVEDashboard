namespace EveDashboard.Stats.Historical;

using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.Core.Storage;
using EveDashboard.Stats.Writer;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IHistoricalSessionBuilder"/> over the session SQLite store.
/// </summary>
/// <remarks>
/// <para>
/// The effective window is the requested window widened to cover every merged
/// source in full - a source is never truncated; its <c>log</c> rows are copied
/// verbatim (role included) under the new session id.
/// </para>
/// <para>
/// A parsed event is written only when its pilot is in the roster, no declined
/// session and no merged source owns that pilot's time (a merged source's rows are
/// copied, so re-parsing its span would double count), and - for Tick / Crit -
/// the ore is known to the SDE (the live writer's rule). Parsed rows carry the
/// roster role and the same timestamp / volume conventions as live rows
/// (<see cref="MiningEventRow"/>).
/// </para>
/// <para>
/// Time accounting is delegated to <see cref="SessionTimelineSynthesizer"/>; the
/// sources are archived with <c>merged_into</c> pointing at the new session, all
/// inside one <c>BEGIN IMMEDIATE</c> transaction.
/// </para>
/// </remarks>
public sealed class HistoricalSessionBuilder : IHistoricalSessionBuilder
{
    private readonly ISqliteDatabase _db;
    private readonly ISdeLookup _sde;
    private readonly ILogger<HistoricalSessionBuilder> _log;

    /// <summary>Construct the builder.</summary>
    /// <param name="db">Session SQLite database.</param>
    /// <param name="sde">SDE lookup for <c>volume_m3</c>.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public HistoricalSessionBuilder(ISqliteDatabase db, ISdeLookup sde, ILogger<HistoricalSessionBuilder> log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sde = sde ?? throw new ArgumentNullException(nameof(sde));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<HistoricalSessionResult> BuildAsync(HistoricalSessionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Session name must not be blank.", nameof(request));
        if (request.Roster.Count == 0) throw new ArgumentException("The roster must contain at least one pilot.", nameof(request));
        if (request.WindowStop < request.WindowStart) throw new ArgumentException("WindowStop must not precede WindowStart.", nameof(request));
        if (request.IdleMinutes is < 1 or > 60) throw new ArgumentException("IdleMinutes must be 1..60.", nameof(request));
        if (request.SessionIdsToMerge.Distinct(StringComparer.Ordinal).Count() != request.SessionIdsToMerge.Count)
        {
            throw new ArgumentException("SessionIdsToMerge contains duplicates.", nameof(request));
        }

        return await _db.WithTransactionAsync(
            async (tx, token) => await BuildInTransactionAsync(tx, request, token).ConfigureAwait(false),
            immediate: true,
            ct).ConfigureAwait(false);
    }

    private async Task<HistoricalSessionResult> BuildInTransactionAsync(ISqliteTransaction tx, HistoricalSessionRequest request, CancellationToken ct)
    {
        // ---- 1. Load the sources and widen the window to cover them. ----
        var sources = new List<Source>();
        foreach (var id in request.SessionIdsToMerge)
        {
            sources.Add(await LoadSourceAsync(tx, id, ct).ConfigureAwait(false));
        }

        var effectiveStart = request.WindowStart;
        var effectiveStop = request.WindowStop;
        foreach (var s in sources)
        {
            if (s.Span.Start < effectiveStart) effectiveStart = s.Span.Start;
            if (s.Span.Stop > effectiveStop) effectiveStop = s.Span.Stop;
        }

        // ---- 2. Identity: same recipe as a live Start (name + persisted start_time). ----
        var startIso = MiningEventRow.FormatTimestamp(effectiveStart);
        var sessionId = SessionId.Compute(request.Name, startIso);
        var exists = await tx.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM session WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = sessionId }, ct).ConfigureAwait(false);
        if (exists > 0)
        {
            throw new InvalidOperationException(
                $"A session named '{request.Name}' starting at {startIso} already exists; pick a different name.");
        }

        // ---- 3. session + pilots. ----
        await tx.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $name, $notes)",
            new Dictionary<string, object?> { ["$sid"] = sessionId, ["$name"] = request.Name, ["$notes"] = request.Notes }, ct).ConfigureAwait(false);

        var rosterById = new Dictionary<long, FleetMember>();
        foreach (var member in request.Roster)
        {
            if (!rosterById.TryAdd(member.PilotId, member)) continue; // duplicate roster entry: first wins
            await tx.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                new Dictionary<string, object?> { ["$pid"] = member.PilotId, ["$name"] = member.Name }, ct).ConfigureAwait(false);
            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $role)",
                new Dictionary<string, object?>
                {
                    ["$sid"] = sessionId, ["$pid"] = member.PilotId, ["$role"] = PilotRoleExtensions.ToCanonicalString(member.Role),
                }, ct).ConfigureAwait(false);
        }

        // ---- 4. Ownership: declined sessions' exclusions + merged sources' spans per pilot. ----
        var ownership = new List<PilotExclusion>(request.Exclusions);
        foreach (var s in sources)
        {
            foreach (var pilotId in s.PilotIds)
            {
                ownership.Add(new PilotExclusion(pilotId, s.Span.Start, s.Span.Stop, s.SessionId));
            }
        }

        // ---- 5. Copy the sources' log rows verbatim. ----
        var copied = 0;
        foreach (var s in sources)
        {
            foreach (var row in s.LogRows)
            {
                await tx.ExecuteNonQueryAsync(
                    InsertLogSql,
                    new Dictionary<string, object?>
                    {
                        ["$sid"] = sessionId, ["$pid"] = row.PilotId, ["$ts"] = row.EveTimestamp, ["$kind"] = row.Kind,
                        ["$type"] = row.TypeId, ["$units"] = row.Units, ["$vol"] = row.VolumeM3, ["$role"] = row.Role,
                    }, ct).ConfigureAwait(false);
                copied++;
            }
        }

        // ---- 6. Write the parsed events that survive the rules. ----
        var parsed = 0; var droppedOwnership = 0; var droppedUnknown = 0; var droppedRoster = 0;
        var qualifyingTimes = new List<DateTimeOffset>();
        foreach (var ev in request.ParsedEvents.OrderBy(e => e.EveTimestamp))
        {
            if (!rosterById.TryGetValue(ev.CharacterId, out var member)) { droppedRoster++; continue; }
            if (ownership.Any(o => o.PilotId == ev.CharacterId && o.Covers(ev.EveTimestamp))) { droppedOwnership++; continue; }
            var qualifying = ev.Kind is MiningEventKind.Tick or MiningEventKind.Crit;
            if (qualifying && ev.TypeId is null) { droppedUnknown++; continue; }

            await tx.ExecuteNonQueryAsync(
                InsertLogSql,
                new Dictionary<string, object?>
                {
                    ["$sid"] = sessionId,
                    ["$pid"] = ev.CharacterId,
                    ["$ts"] = MiningEventRow.FormatTimestamp(ev.EveTimestamp),
                    ["$kind"] = ev.Kind.ToString(),
                    ["$type"] = ev.TypeId,
                    ["$units"] = ev.Amount,
                    ["$vol"] = MiningEventRow.VolumeFor(_sde, ev.TypeId, ev.Amount),
                    ["$role"] = PilotRoleExtensions.ToCanonicalString(member.Role),
                }, ct).ConfigureAwait(false);
            parsed++;
            if (qualifying) qualifyingTimes.Add(ev.EveTimestamp);
        }

        // ---- 7. Timeline. ----
        var rows = SessionTimelineSynthesizer.Build(
            effectiveStart,
            effectiveStop,
            sources.Select(s => s.Span).ToList(),
            sources.SelectMany(s => s.ActiveIntervals).ToList(),
            qualifyingTimes,
            TimeSpan.FromMinutes(request.IdleMinutes));
        foreach (var row in rows)
        {
            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $start, $stop, $pause)",
                new Dictionary<string, object?>
                {
                    ["$sid"] = sessionId,
                    ["$start"] = MiningEventRow.FormatTimestamp(row.Start),
                    ["$stop"] = MiningEventRow.FormatTimestamp(row.Stop),
                    ["$pause"] = row.IsPause ? 1 : 0,
                }, ct).ConfigureAwait(false);
        }

        // ---- 8. Archive the sources. ----
        foreach (var s in sources)
        {
            await tx.ExecuteNonQueryAsync(
                "UPDATE session SET archived = 1, merged_into = $new WHERE session_id = $sid",
                new Dictionary<string, object?> { ["$new"] = sessionId, ["$sid"] = s.SessionId }, ct).ConfigureAwait(false);
        }

        var result = new HistoricalSessionResult(
            sessionId, effectiveStart, effectiveStop,
            copied, parsed, droppedOwnership, droppedUnknown, droppedRoster,
            rows.Count(r => !r.IsPause), rows.Count(r => r.IsPause),
            sources.Select(s => s.SessionId).ToList());

        _log.LogInformation(
            "Built historical session {SessionId} '{Name}' {Start:O}..{Stop:O}: {Copied} copied + {Parsed} parsed log rows ({DroppedOwnership} owned elsewhere, {DroppedUnknown} unknown ore, {DroppedRoster} off-roster dropped); {Active} active / {Pause} pause rows; archived {Archived} source(s).",
            sessionId, request.Name, effectiveStart, effectiveStop, copied, parsed, droppedOwnership, droppedUnknown, droppedRoster,
            result.ActiveIntervals, result.PauseIntervals, sources.Count);
        return result;
    }

    private static async Task<Source> LoadSourceAsync(ISqliteTransaction tx, string sessionId, CancellationToken ct)
    {
        var p = new Dictionary<string, object?> { ["$sid"] = sessionId };
        var header = await tx.QueryAsync(
            "SELECT archived FROM session WHERE session_id = $sid",
            static r => r.GetInt64(0), p, ct).ConfigureAwait(false);
        if (header.Count == 0) throw new InvalidOperationException($"Session '{sessionId}' does not exist; it cannot be combined.");
        if (header[0] != 0) throw new InvalidOperationException($"Session '{sessionId}' is already archived (it was combined before); it cannot be combined again.");

        var times = await tx.QueryAsync(
            "SELECT start_time, stop_time, is_pause FROM session_time WHERE session_id = $sid ORDER BY session_time_id",
            static r => (Start: r.GetString(0), Stop: r.IsDBNull(1) ? null : r.GetString(1), IsPause: r.GetInt64(2) != 0), p, ct).ConfigureAwait(false);
        if (times.Count == 0) throw new InvalidOperationException($"Session '{sessionId}' has no time rows; it cannot be combined.");
        if (times.Any(t => t.Stop is null)) throw new InvalidOperationException($"Session '{sessionId}' is still running; stop it before combining.");

        var intervals = times.Select(t => (Interval: new TimeInterval(ParseUtc(t.Start), ParseUtc(t.Stop!)), t.IsPause)).ToList();
        var span = new TimeInterval(intervals.Min(i => i.Interval.Start), intervals.Max(i => i.Interval.Stop));
        var active = intervals.Where(i => !i.IsPause).Select(i => i.Interval).ToList();

        var pilots = await tx.QueryAsync(
            "SELECT pilot_id FROM session_pilot WHERE session_id = $sid",
            static r => r.GetInt64(0), p, ct).ConfigureAwait(false);

        var logRows = await tx.QueryAsync(
            "SELECT pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role FROM log WHERE session_id = $sid ORDER BY log_id",
            static r => new SourceLogRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                r.GetString(6)),
            p, ct).ConfigureAwait(false);

        return new Source(sessionId, span, active, pilots, logRows);
    }

    private static DateTimeOffset ParseUtc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private const string InsertLogSql =
        "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
        "VALUES ($sid, $pid, $ts, $kind, $type, $units, $vol, $role)";

    private sealed record SourceLogRow(long PilotId, string EveTimestamp, string Kind, long? TypeId, long? Units, double? VolumeM3, string Role);

    private sealed record Source(
        string SessionId,
        TimeInterval Span,
        IReadOnlyList<TimeInterval> ActiveIntervals,
        IReadOnlyList<long> PilotIds,
        IReadOnlyList<SourceLogRow> LogRows);
}
