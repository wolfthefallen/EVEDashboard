namespace EveDashboard.Core.Sessions;

/// <summary>
/// The runtime state of the currently-selected mining session.
/// </summary>
/// <remarks>
/// Read by the session-event writer (plan 12) on every parsed event to decide
/// whether to enqueue or drop. Mutated by the session-controls UI (a
/// v1-backlog plan) in response to user actions on Start / Pause / Resume / End.
/// </remarks>
public enum SessionRunState
{
    /// <summary>No session is active. Events arriving in this state are dropped silently.</summary>
    None,

    /// <summary>A session is active and accepting events.</summary>
    Running,

    /// <summary>
    /// A session is active but in a paused window; events arriving in this
    /// state are dropped silently and excluded from rate calculations.
    /// </summary>
    Paused,
}
