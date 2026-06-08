namespace EveDashboard.Core.Sessions;

/// <summary>
/// Event payload for the <c>IActiveSessionContext.RolesChanged</c> event
/// (added by plan 35 step 3). Fires when a pilot's role changes mid-session
/// via <c>ActiveSessionContext.SetRole</c> (which mirrors
/// <c>SessionController.SetPilotRoleAsync</c>'s SQL UPDATE into the in-memory
/// role map).
/// </summary>
/// <remarks>
/// Bulk seeding via <c>ActiveSessionContext.SeedRoles</c> at session start
/// does NOT raise this event — seeding is initial setup, not a user-facing
/// role change. Per-pilot widget VMs subscribe to this event and update their
/// bound <c>Role</c> property when <see cref="PilotId"/> matches their own.
/// Plan 35.
/// </remarks>
public sealed class PilotRoleChangedEventArgs : EventArgs
{
    /// <summary>EVE character id of the pilot whose role changed.</summary>
    public long PilotId { get; }

    /// <summary>The pilot's role prior to the change.</summary>
    public PilotRole OldRole { get; }

    /// <summary>The pilot's role after the change.</summary>
    public PilotRole NewRole { get; }

    /// <summary>Construct event args.</summary>
    /// <param name="pilotId">EVE character id of the pilot whose role changed.</param>
    /// <param name="oldRole">Role before the change.</param>
    /// <param name="newRole">Role after the change.</param>
    public PilotRoleChangedEventArgs(long pilotId, PilotRole oldRole, PilotRole newRole)
    {
        PilotId = pilotId;
        OldRole = oldRole;
        NewRole = newRole;
    }
}
