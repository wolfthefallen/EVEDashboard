namespace EveDashboard.Stats.Queries;

/// <summary>
/// Read-only queries that materialize <see cref="SessionStats"/> snapshots
/// from the live session SQLite database. Consumers (widget ViewModels) call
/// <see cref="GetSessionStatsAsync"/> when
/// <c>ISessionEventNotifier.SessionEventAdded</c> fires.
/// </summary>
/// <remarks>
/// <para>
/// The repository is **pulled, not pushed** — it does not subscribe to the
/// session-event notifier itself. Per plan 13's architecture, widget VMs are
/// the subscribers; the VM calls this method on every event-added fire and
/// caches the returned snapshot for binding.
/// </para>
/// <para>
/// Implementations are expected to be safe for concurrent calls (the SQL
/// substrate uses WAL mode, which permits multiple readers concurrent with
/// the writer). The repository itself holds no mutable state.
/// </para>
/// </remarks>
public interface ISessionStatsRepository
{
    /// <summary>
    /// Compute and return the current <see cref="SessionStats"/> snapshot
    /// for a session.
    /// </summary>
    /// <param name="sessionId">Session id — should exist in the
    ///     <c>session</c> table. If it does not, the implementation logs a
    ///     <c>Warning</c> and returns an empty <see cref="SessionStats"/>
    ///     (per plan 13 decision 8.1). Widget VMs are required to gate this
    ///     call on <c>IActiveSessionContext.State</c> being
    ///     <c>Running</c> or <c>Paused</c>; the empty-result path is purely
    ///     defensive.</param>
    /// <param name="asOf">Wall-clock instant the snapshot is taken at.
    ///     Used as the endpoint for any open <c>session_time</c> row when
    ///     computing active duration, and stamped onto
    ///     <see cref="SessionStats.LastCalculated"/>.</param>
    /// <param name="allowLazyFetch">Plan 38: <c>true</c> for the LIVE session (the ISK calc may fetch + persist missing prices); <c>false</c> for Reports / saved-session reads (never fetch — a missing price contributes 0). This is the seam that keeps Reports from re-pricing a saved session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session's stats snapshot. Per-pilot stats are reachable
    ///     via <see cref="SessionStats.ByCharacter"/>.</returns>
    /// <exception cref="System.Data.Common.DbException">
    /// Bubbles up from the underlying <c>ISqliteDatabase</c> on a SQL error.
    /// Already logged at <c>Error</c> by the database layer.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled.
    /// </exception>
    Task<SessionStats> GetSessionStatsAsync(
        string sessionId,
        DateTimeOffset asOf,
        bool allowLazyFetch,
        CancellationToken ct = default);
}
