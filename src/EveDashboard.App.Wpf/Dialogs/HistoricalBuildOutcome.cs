namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// What the "Build session from history" dialog produced when the user
/// completed it - handed back to the Reports window so it can reload its
/// sidebar and show a status line.
/// </summary>
/// <param name="SessionId">The new session's id.</param>
/// <param name="SessionName">The new session's (template-expanded) name.</param>
/// <param name="Summary">One-line human summary: rows copied / parsed, sessions archived, and where the prices came from.</param>
public sealed record HistoricalBuildOutcome(string SessionId, string SessionName, string Summary);
