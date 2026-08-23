namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// One pilot of a saved session as recorded in <c>session_pilot</c>.
/// </summary>
/// <param name="PilotId">EVE character id.</param>
/// <param name="Name">Display name from the <c>pilot</c> table.</param>
/// <param name="Role">The fleet role the session recorded for this pilot.</param>
public sealed record SessionSpanPilot(long PilotId, string Name, PilotRole Role);

/// <summary>
/// A saved session reduced to what overlap analysis needs: its full wall-clock
/// span (first <c>session_time.start_time</c> to last <c>stop_time</c>, pauses
/// included) and its pilots.
/// </summary>
/// <param name="SessionId">Session id (sha256 hex PK).</param>
/// <param name="Name">Session name as stored.</param>
/// <param name="Start">Wall-clock start of the session (UTC).</param>
/// <param name="Stop">Wall-clock end of the session (UTC). An in-flight session reports the moment the query ran.</param>
/// <param name="Pilots">Every <c>session_pilot</c> row of the session.</param>
/// <remarks>
/// The span deliberately includes pause windows: while a pilot belonged to this
/// session - paused or not - no OTHER session may claim their mining for that
/// time, otherwise the same ore would be counted twice.
/// </remarks>
public sealed record SessionSpan(
    string SessionId,
    string Name,
    DateTimeOffset Start,
    DateTimeOffset Stop,
    IReadOnlyList<SessionSpanPilot> Pilots);
