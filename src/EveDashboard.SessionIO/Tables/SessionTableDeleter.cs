namespace EveDashboard.SessionIO.Tables;

using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Deletes one session and every row that has a direct <c>session_id</c> FK
/// back to it: <c>session_time</c>, <c>session_pilot</c>, <c>log</c>, then the
/// <c>session</c> row itself. If the session was the result of a combine, the
/// sources archived into it (<c>merged_into</c> = this id) are un-archived first,
/// so deleting a combined session restores the originals. Atomic via raw <c>BEGIN IMMEDIATE</c> /
/// <c>COMMIT</c> / <c>ROLLBACK</c> SQL since <see cref="ISqliteDatabase"/>
/// exposes no transaction API.
/// </summary>
/// <remarks>
/// What is NOT deleted (matching the user-stated rule "no direct session FK =
/// leave alone"):
/// <list type="bullet">
///   <item><c>pilot</c> rows — EVE-stable ids reused across sessions; reachable only via the <c>session_pilot</c> join, not a direct FK.</item>
///   <item><c>market_data</c> rows — the <c>session_id</c> column is a plain TEXT column, NOT an enforced FK, so deleting a session intentionally leaves harmless orphan price rows behind.</item>
///   <item><c>market_stations</c> rows — same reason, plus they are the user-configurable catalog.</item>
///   <item><c>setting</c> rows — settings are user-global, not session-scoped.</item>
/// </list>
/// </remarks>
public sealed class SessionTableDeleter
{
    private readonly ISqliteDatabase _db;
    private readonly ILogger<SessionTableDeleter> _log;

    /// <summary>Construct the deleter over a session SQLite wrapper.</summary>
    /// <param name="db">Session SQLite wrapper to delete from.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public SessionTableDeleter(ISqliteDatabase db, ILogger<SessionTableDeleter> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Delete the session at <paramref name="sessionId"/> and every row whose
    /// table carries a direct FK to it, after un-archiving any sources that were
    /// combined into it. Atomic — any failure rolls back to the
    /// pre-delete state.
    /// </summary>
    /// <param name="sessionId">Session id to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of rows deleted across all four tables. Returns 0 if no session matched (everything else cascade-trivially returns 0 too).</returns>
    public async Task<int> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        // Atomic via WithTransactionAsync (BEGIN IMMEDIATE..COMMIT, ROLLBACK on
        // throw) — holds the connection gate for the whole delete so it cannot
        // interleave with a live session's writes.
        return await _db.WithTransactionAsync(async (tx, _) =>
        {
            // FK-respecting order: child tables first, parent last. PRAGMA
            // foreign_keys = ON (set by SchemaInitializer) would otherwise
            // refuse to delete the session row while session_time / log / etc.
            // still reference it.
            var parameters = new Dictionary<string, object?> { ["$sid"] = sessionId };

            // Deleting a COMBINED session restores its sources: the sessions that
            // were archived with merged_into = this id come back into view, so a
            // combine can always be undone by deleting its result and no mining
            // data becomes unreachable.
            var unarchived = await tx.ExecuteNonQueryAsync(
                "UPDATE session SET archived = 0, merged_into = NULL WHERE merged_into = $sid", parameters, ct).ConfigureAwait(false);
            if (unarchived > 0)
            {
                _log.LogInformation("Un-archived {Count} source session(s) that had been combined into #{SessionId}.", unarchived, sessionId);
            }

            var logsDeleted = await tx.ExecuteNonQueryAsync(
                "DELETE FROM log WHERE session_id = $sid", parameters, ct).ConfigureAwait(false);
            var sessionPilotsDeleted = await tx.ExecuteNonQueryAsync(
                "DELETE FROM session_pilot WHERE session_id = $sid", parameters, ct).ConfigureAwait(false);
            var sessionTimesDeleted = await tx.ExecuteNonQueryAsync(
                "DELETE FROM session_time WHERE session_id = $sid", parameters, ct).ConfigureAwait(false);
            var sessionsDeleted = await tx.ExecuteNonQueryAsync(
                "DELETE FROM session WHERE session_id = $sid", parameters, ct).ConfigureAwait(false);

            _log.LogInformation(
                "Deleted session #{SessionId}: {Sessions} session + {SessionTimes} session_time + {SessionPilots} session_pilot + {Logs} log row(s).",
                sessionId, sessionsDeleted, sessionTimesDeleted, sessionPilotsDeleted, logsDeleted);
            return logsDeleted + sessionPilotsDeleted + sessionTimesDeleted + sessionsDeleted;
        }, immediate: true, ct).ConfigureAwait(false);
    }
}
