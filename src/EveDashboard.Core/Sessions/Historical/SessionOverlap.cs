namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// How a saved session's pilots relate to the fleet roster the user picked for a
/// historical window.
/// </summary>
public enum FleetMatchKind
{
    /// <summary>Every pilot of the session is in the roster - combining it needs no fleet change.</summary>
    Full,

    /// <summary>The session has pilots the roster lacks - combining it means a union fleet (the roster plus the session's extra pilots).</summary>
    Partial,
}

/// <summary>
/// A saved session that overlaps the requested window AND shares at least one
/// pilot with the roster - i.e. something the user must decide to combine or
/// exclude, because a pilot can only belong to one session at a time.
/// </summary>
/// <param name="Session">The overlapping session.</param>
/// <param name="Kind">Full or partial fleet match.</param>
/// <param name="SharedPilotIds">Pilots present in both the roster and the session, ascending.</param>
/// <param name="ExtraPilots">Session pilots the roster does not have (empty for a <see cref="FleetMatchKind.Full"/> match), in session order.</param>
public sealed record SessionOverlap(
    SessionSpan Session,
    FleetMatchKind Kind,
    IReadOnlyList<long> SharedPilotIds,
    IReadOnlyList<SessionSpanPilot> ExtraPilots);
