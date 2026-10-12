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

/// <summary>
/// Everything one visit to the dialog produced. A single build reports one
/// session; building a session per day reports one per day, plus the days that
/// were skipped or failed, so none of them goes missing silently.
/// </summary>
/// <param name="Sessions">The sessions created, in day order.</param>
/// <param name="Skipped">Days deliberately passed over, each with its reason.</param>
/// <param name="Failures">Days that errored while the rest of the batch completed.</param>
/// <param name="Summary">One-line human summary of the whole visit.</param>
public sealed record HistoricalBatchOutcome(
    IReadOnlyList<HistoricalBuildOutcome> Sessions,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Failures,
    string Summary);
