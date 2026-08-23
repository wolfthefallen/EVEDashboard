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
}
