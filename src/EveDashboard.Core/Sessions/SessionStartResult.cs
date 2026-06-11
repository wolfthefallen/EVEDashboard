namespace EveDashboard.Core.Sessions;

/// <summary>
/// Outcome of a session-start attempt (plan 42). Returned by the reworked
/// <c>SessionController.StartAsync(roster)</c> so the New Session dialog can
/// decide whether to close or surface an inline error. Lives in Core (pure) so
/// both the controller and the dialog view-model reference one type.
/// </summary>
public enum SessionStartResult
{
    /// <summary>A session is now active (or was already active — the defensive guard treats that as success).</summary>
    Started,

    /// <summary>No fleet pilot was discoverable at Start, so no session was created (decision 2.5 = A). The dialog stays open.</summary>
    NoDiscoverablePilots,
}
