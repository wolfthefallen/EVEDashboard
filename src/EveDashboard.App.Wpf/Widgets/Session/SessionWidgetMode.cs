namespace EveDashboard.App.Wpf.Widgets.Session;

/// <summary>
/// Visible mode of the Session widget set (SessionConfig + SessionStats).
/// Drives chrome-bar button visibility and which Session-area widget is in
/// the layout (per plan 24): <see cref="Config"/> renders SessionConfig;
/// <see cref="Running"/>, <see cref="Paused"/>, and <see cref="Stopped"/>
/// render SessionStats.
/// </summary>
public enum SessionWidgetMode
{
    /// <summary>
    /// Default startup state when no open <c>session_time</c> row exists.
    /// Renders the SessionConfig widget (session-name input + pilot list).
    /// Chrome-bar shows <c>Start</c>.
    /// </summary>
    Config,

    /// <summary>
    /// A session is actively running. Renders the SessionStats widget
    /// (DisplaySessionName + the seven stat fields + live elapsed-time).
    /// Chrome-bar shows <c>Pause</c> + <c>Stop</c>.
    /// </summary>
    Running,

    /// <summary>
    /// A session is paused. Either an in-session pause (user clicked Pause
    /// from <see cref="Running"/>) OR an app-launch detection of an open
    /// <c>session_time</c> row from a prior run (per round-6 11.7 the widget
    /// never auto-resumes — user must click). Both cases surface the same
    /// chrome buttons (<c>Resume</c> + <c>Stop</c>) and render the same
    /// SessionStats widget content; the dispatch between in-session-resume
    /// and app-launch-resume happens inside
    /// <see cref="SessionController.ResumeAsync"/> via the
    /// <c>_sessionStartUtc</c> null-check (null = app-launch path; set =
    /// in-session path).
    /// </summary>
    Paused,

    /// <summary>
    /// A session has just been stopped. The widget reuses the
    /// <see cref="Running"/> stat-tile block — DisplaySessionName + the
    /// frozen final TotalTimeMined + the seven stat values — so the user
    /// can review what they just mined. Chrome-bar shows a single
    /// <c>New Session</c> button (visible only in this mode); clicking it
    /// transitions back to <see cref="Config"/> with the prior pilot
    /// selection preserved (per plan 23 decision 1.2 = A). Plan 23 ships
    /// this mode; without it the widget flips back to Config on Stop and
    /// the user loses sight of the just-ended session's totals.
    /// </summary>
    Stopped,
}
