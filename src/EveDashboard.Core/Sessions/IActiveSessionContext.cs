namespace EveDashboard.Core.Sessions;

/// <summary>
/// Read-only consumer view on which mining session is currently active and
/// whether it is running or paused right now.
/// </summary>
/// <remarks>
/// <para>
/// The single piece of in-memory runtime state that the SQL substrate cannot
/// replace: a small in-memory companion to the session store. The truth
/// is still SQL (the active session can be re-derived from the latest
/// <c>session</c> row with an open <c>session_time</c>); this interface is a
/// navigational pointer cached in memory to avoid querying SQL on every
/// parser-emitted event.
/// </para>
/// <para>
/// Mutation is via the concrete <see cref="ActiveSessionContext.Set"/> method;
/// consumers (e.g. the session-event writer) take the read-only
/// interface here so they cannot accidentally mutate state.
/// </para>
/// </remarks>
public interface IActiveSessionContext
{
    /// <summary>
    /// EVE Dashboard <c>session.session_id</c> (a sha256 hex
    /// string) of the active session, or <c>null</c> when no session is active.
    /// </summary>
    string? ActiveSessionId { get; }

    /// <summary>Current run state (None / Running / Paused).</summary>
    SessionRunState State { get; }

    /// <summary>
    /// Raised after either <see cref="ActiveSessionId"/> or <see cref="State"/>
    /// changes. Subscribers should not assume the change reflects only one of
    /// the two; re-read both properties when handling.
    /// </summary>
    event EventHandler? ActiveSessionChanged;

    /// <summary>
    /// Lookup the fleet role for a pilot in the active session. Used
    /// by <c>SessionEventWriter</c> on every <c>log</c> INSERT to stamp the
    /// event-time role. Returns <see cref="PilotRole.Miner"/> if the pilot is
    /// not mapped (defensive — should not happen during a session whose
    /// <c>session_pilot</c> rows seeded the map at start).
    /// </summary>
    /// <param name="pilotId">EVE character id to look up.</param>
    /// <returns>The pilot's current role.</returns>
    PilotRole GetRole(long pilotId);

    /// <summary>
    /// Fires when a pilot's role changes mid-session via
    /// <c>ActiveSessionContext.SetRole</c>. Per-pilot widget VMs subscribe and
    /// update their bound <c>Role</c> property when the pilot id matches.
    /// </summary>
    /// <remarks>
    /// Bulk seeding via <c>ActiveSessionContext.SeedRoles</c> at session start
    /// does NOT raise this event — seeding is initial setup, not a user-facing
    /// role change. Raised outside the context's internal lock to avoid
    /// re-entrancy if a subscriber calls back in while handling.
    /// </remarks>
    event EventHandler<PilotRoleChangedEventArgs>? RolesChanged;
}
