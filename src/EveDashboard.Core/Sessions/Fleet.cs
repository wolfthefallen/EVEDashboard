namespace EveDashboard.Core.Sessions;

/// <summary>
/// A saved, reusable fleet: a named preset of pilots, each pre-assigned a
/// <see cref="PilotRole"/>. A fleet is the only way to start a mining session
/// (plan 42) — at Start its <see cref="Members"/> roles are written verbatim
/// into the session's <c>session_pilot</c> rows.
/// </summary>
/// <param name="FleetId">Content-addressed id from <see cref="Sessions.FleetId.Compute"/> (sha256 of name + sorted members).</param>
/// <param name="Name">User-supplied display name. Unique across fleets (trimmed before save).</param>
/// <param name="CreatedAtIso">Creation timestamp, ISO-8601 UTC (millisecond precision).</param>
/// <param name="Members">The fleet's pilots and their assigned roles.</param>
public sealed record Fleet(
    string FleetId,
    string Name,
    string CreatedAtIso,
    IReadOnlyList<FleetMember> Members);

/// <summary>
/// One pilot within a <see cref="Fleet"/>: the pilot's EVE character id, display
/// name, and assigned <see cref="PilotRole"/>.
/// </summary>
/// <param name="PilotId">EVE character id. Joins to the existing <c>pilot</c> table for the name.</param>
/// <param name="Name">Display name captured at fleet-create time (the pilot was a discovered character then).</param>
/// <param name="Role">The role this pilot holds in the fleet.</param>
/// <remarks>
/// <paramref name="Name"/> is informational for the fleet expander. At Start the
/// freshly-discovered character name wins, so only <paramref name="PilotId"/> and
/// <paramref name="Role"/> are consumed by the session-start path.
/// </remarks>
public sealed record FleetMember(
    long PilotId,
    string Name,
    PilotRole Role);
