namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Seam for the modal "Build session from history" dialog so the Reports
/// view-model can open it without a WPF dependency (tests substitute a fake).
/// </summary>
public interface IHistoricalSessionDialog
{
    /// <summary>
    /// Open the dialog modally and block until it closes.
    /// </summary>
    /// <param name="preselectedSessionIds">
    /// When non-empty the dialog opens in COMBINE mode: the window is pre-filled
    /// from these sessions' spans, they are pre-ticked to combine, and no gamelog
    /// parsing happens. Null or empty opens the date-range mode.
    /// </param>
    /// <returns>The outcome when at least one session was built; <c>null</c> when the user cancelled or nothing was written.</returns>
    HistoricalBatchOutcome? Show(IReadOnlyList<string>? preselectedSessionIds);
}
