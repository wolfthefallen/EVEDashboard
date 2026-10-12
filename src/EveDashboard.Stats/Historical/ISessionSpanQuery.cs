namespace EveDashboard.Stats.Historical;

using EveDashboard.Core.Sessions.Historical;

/// <summary>
/// Reads the saved, non-archived sessions whose wall-clock span intersects a
/// window - the input for <see cref="OverlapAnalyzer"/> when building a session
/// after the fact.
/// </summary>
public interface ISessionSpanQuery
{
    /// <summary>
    /// Return every non-archived session whose span (first <c>session_time.start_time</c>
    /// to last <c>stop_time</c>, pauses included) overlaps
    /// <paramref name="windowStart"/>..<paramref name="windowStop"/> inclusive, with its pilots.
    /// </summary>
    /// <param name="windowStart">Inclusive window start (UTC).</param>
    /// <param name="windowStop">Inclusive window end (UTC).</param>
    /// <param name="asOf">The instant an in-flight session (open <c>stop_time</c>) is assumed to run until; defaults to now. Exposed for tests.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Overlapping sessions ordered by start. Sessions with no <c>session_time</c> row are skipped (they have no span).</returns>
    Task<IReadOnlyList<SessionSpan>> GetOverlappingAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowStop,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default);

    /// <summary>
    /// Return the spans of specific non-archived sessions (the ones the user
    /// ticked to combine), in start order. Ids that do not exist, are archived, or
    /// have no <c>session_time</c> row are silently absent from the result.
    /// </summary>
    /// <param name="sessionIds">Session ids to look up.</param>
    /// <param name="asOf">The instant an in-flight session is assumed to run until; defaults to now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching spans with their pilots.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sessionIds"/> is null.</exception>
    Task<IReadOnlyList<SessionSpan>> GetSpansAsync(
        IReadOnlyCollection<string> sessionIds,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default);

    /// <summary>
    /// Return every non-archived session's span, with its pilots and their
    /// recorded roles, in start order.
    /// </summary>
    /// <param name="asOf">The instant an in-flight session is assumed to run until; defaults to now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every span the store holds. Sessions with no <c>session_time</c> row are skipped (they have no span).</returns>
    /// <remarks>
    /// A batch build of many days needs the whole set once rather than a query
    /// per day: <see cref="OverlapAnalyzer.Analyze"/> does its own window
    /// filtering, so one result serves every day, and planning the batch before
    /// anything is written keeps the sessions the batch itself creates out of its
    /// own overlap candidates. The same result also answers "what role did this
    /// pilot have last time" via <see cref="PilotRoleHistory.Resolve"/>, so no
    /// second query is needed for that either.
    /// </remarks>
    Task<IReadOnlyList<SessionSpan>> GetAllAsync(
        DateTimeOffset? asOf = null,
        CancellationToken ct = default);
}
