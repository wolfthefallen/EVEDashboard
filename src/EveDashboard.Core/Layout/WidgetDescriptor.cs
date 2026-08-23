namespace EveDashboard.Core.Layout;

/// <summary>
/// Persisted identity of one widget in the dashboard layout (the former
/// SessionConfig widget now lives in a modal dialog, not the layout, so the
/// union is <see cref="SessionStats"/> and <see cref="Pilot"/>). Closed
/// discriminated union: only the two nested case types defined below are
/// valid concrete subtypes.
/// </summary>
/// <remarks>
/// The private parameterless constructor is what makes the hierarchy closed:
/// only nested types within <see cref="WidgetDescriptor"/> can call it, so
/// external assemblies cannot introduce a third case. Same pattern as
/// <c>EveDashboard.Sde.Updater.SdeUpdateResult</c>
/// (named as plain text — <c>EveDashboard.Core</c> does not depend on
/// <c>EveDashboard.Sde</c>).
/// </remarks>
public abstract record WidgetDescriptor
{
    private WidgetDescriptor() { }

    /// <summary>
    /// The SessionStats widget — running and stopped session stat tiles
    /// (DisplaySessionName + the seven stat fields + elapsed-time).
    /// Renderable iff the layout coordinator's <c>_statsActivated</c> flag
    /// is true (set on first Mode -> Running OR Mode -> Paused; latches
    /// for the app run).
    /// </summary>
    public sealed record SessionStats() : WidgetDescriptor;

    /// <summary>One per-pilot widget, identified by EVE character id (matches <c>session_pilot.pilot_id</c>).</summary>
    /// <param name="PilotId">EVE character id.</param>
    public sealed record Pilot(long PilotId) : WidgetDescriptor;
}
