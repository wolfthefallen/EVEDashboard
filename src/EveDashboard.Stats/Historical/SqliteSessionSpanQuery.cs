namespace EveDashboard.Stats.Historical;

using System.Globalization;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// SQLite <see cref="ISessionSpanQuery"/> over the session store.
/// </summary>
/// <remarks>
/// The span bounds are computed in C# after parsing, not compared as strings in
/// SQL: <c>session_time</c> timestamps were written by more than one formatter
/// over the app's life (<c>...Z</c> and <c>...+00:00</c> shapes both exist in
/// real stores), and lexicographic comparison across those shapes is not safe.
/// The session table is small (dozens of rows), so reading every non-archived
/// span and filtering here costs nothing measurable.
/// </remarks>
public sealed class SqliteSessionSpanQuery : ISessionSpanQuery
{
    private const string SpansSql = """
        SELECT s.session_id,
               s.name,
               MIN(t.start_time),
               MAX(t.stop_time),
               SUM(CASE WHEN t.stop_time IS NULL THEN 1 ELSE 0 END)
        FROM session s
        JOIN session_time t ON t.session_id = s.session_id
        WHERE s.archived = 0
        GROUP BY s.session_id, s.name
        """;

    private const string PilotsSql = """
        SELECT sp.pilot_id, p.name, sp.role
        FROM session_pilot sp
        JOIN pilot p ON p.pilot_id = sp.pilot_id
        WHERE sp.session_id = $sid
        ORDER BY sp.pilot_id
        """;

    private readonly ISqliteDatabase _db;
    private readonly ILogger<SqliteSessionSpanQuery> _log;

    /// <summary>Construct the query over the session database.</summary>
    /// <param name="db">Session SQLite database.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public SqliteSessionSpanQuery(ISqliteDatabase db, ILogger<SqliteSessionSpanQuery> log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSpan>> GetOverlappingAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowStop,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default) =>
        LoadAsync(s => s.Start <= windowStop && s.Stop >= windowStart, asOf, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSpan>> GetSpansAsync(
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);
        var wanted = new HashSet<string>(sessionIds, StringComparer.Ordinal);
        return LoadAsync(s => wanted.Contains(s.SessionId), asOf, ct);
    }

    /// <summary>Read every non-archived span, keep those <paramref name="keep"/> accepts, attach pilots.</summary>
    private async Task<IReadOnlyList<SessionSpan>> LoadAsync(
        Func<(string SessionId, DateTimeOffset Start, DateTimeOffset Stop), bool> keep,
        DateTimeOffset? asOf,
        CancellationToken ct)
    {
        var now = asOf ?? DateTimeOffset.UtcNow;
        var raw = await _db.QueryAsync(
            SpansSql,
            static r => (
                Id: r.GetString(0),
                Name: r.GetString(1),
                Start: r.GetString(2),
                Stop: r.IsDBNull(3) ? null : r.GetString(3),
                OpenTails: r.GetInt64(4)),
            ct: ct).ConfigureAwait(false);

        var result = new List<SessionSpan>();
        foreach (var row in raw)
        {
            ct.ThrowIfCancellationRequested();
            var start = ParseUtc(row.Start);
            // MAX(stop_time) ignores NULLs, so an in-flight session (open tail)
            // must extend to "now" regardless of what its closed rows say.
            var stop = row.OpenTails > 0 ? now : ParseUtc(row.Stop!);
            if (stop < start)
            {
                _log.LogWarning("Session {SessionId} has stop {Stop:O} before start {Start:O}; treating it as a point in time.", row.Id, stop, start);
                stop = start;
            }
            if (!keep((row.Id, start, stop))) continue;

            var pilots = await _db.QueryAsync(
                PilotsSql,
                static r => new SessionSpanPilot(r.GetInt64(0), r.GetString(1), PilotRoleExtensions.Parse(r.GetString(2))),
                new Dictionary<string, object?> { ["$sid"] = row.Id },
                ct).ConfigureAwait(false);

            result.Add(new SessionSpan(row.Id, row.Name, start, stop, pilots));
        }

        return result.OrderBy(s => s.Start).ThenBy(s => s.SessionId, StringComparer.Ordinal).ToList();
    }

    private static DateTimeOffset ParseUtc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
