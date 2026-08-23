namespace EveDashboard.App.Wpf.Reporting;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Reporting.Lifetime;
using EveDashboard.Reporting;
using EveDashboard.SessionIO.Import;
using EveDashboard.SessionIO.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Root VM for the Reports window. Composes
/// <see cref="SessionsSidebarViewModel"/> and <see cref="BreakdownContentViewModel"/>;
/// subscribes to the sidebar's <see cref="SessionsSidebarViewModel.SelectionChanged"/>
/// event and routes the ordered selection list to the content VM (single
/// session = single-mode display; 2+ = compare-mode display).
/// </summary>
public sealed partial class ReportsWindowViewModel : ObservableObject
{
    private readonly JsonSessionImporter _importer;
    private readonly IReportsFileDialog _fileDialog;
    private readonly ILifetimeTotalsWindow _lifetimeWindow;
    private readonly IHistoricalSessionDialog _historicalDialog;
    private readonly ILogger<ReportsWindowViewModel> _log;

    /// <summary>The sortable sidebar of saved sessions.</summary>
    public SessionsSidebarViewModel Sidebar { get; }

    /// <summary>The middle-column breakdown content for the currently selected session.</summary>
    public BreakdownContentViewModel Content { get; }

    /// <summary>
    /// One-line status surface at the bottom of the Reports window.
    /// Reads "Imported: ..." after a successful import, "Exported: ..." after
    /// an export, and "Import failed: ..." / "Export failed: ..." on errors.
    /// Empty string => the row collapses (see <c>StringEmptyToVisibilityConverter</c>).
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Construct the root Reports VM.</summary>
    /// <param name="sidebar">Sidebar VM.</param>
    /// <param name="content">Breakdown content VM.</param>
    /// <param name="importer">JSON importer wired to the Import Session header button.</param>
    /// <param name="fileDialog">File dialog seam for open-path selection.</param>
    /// <param name="lifetimeWindow">Lifetime Totals window seam, opened by the chrome "Lifetime Totals" button.</param>
    /// <param name="historicalDialog">Modal "Build session from history" dialog seam (date-range and combine modes).</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public ReportsWindowViewModel(
        SessionsSidebarViewModel sidebar,
        BreakdownContentViewModel content,
        JsonSessionImporter importer,
        IReportsFileDialog fileDialog,
        ILifetimeTotalsWindow lifetimeWindow,
        IHistoricalSessionDialog historicalDialog,
        ILogger<ReportsWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(lifetimeWindow);
        ArgumentNullException.ThrowIfNull(historicalDialog);
        ArgumentNullException.ThrowIfNull(log);
        Sidebar = sidebar;
        Content = content;
        _importer = importer;
        _fileDialog = fileDialog;
        _lifetimeWindow = lifetimeWindow;
        _historicalDialog = historicalDialog;
        _log = log;
        Sidebar.SelectionChanged += OnSidebarSelectionChanged;
        Sidebar.SelectionChanged += (_, _) => CombineSelectedCommand.NotifyCanExecuteChanged();
        Sidebar.StatusReporter = msg => StatusMessage = msg;
    }

    /// <summary>Initialize on Reports-window open: load the sidebar (which restores collapse + width).</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task InitializeAsync(CancellationToken ct = default) => Sidebar.LoadAsync(ct);

    private async void OnSidebarSelectionChanged(object? sender, IReadOnlyList<SessionSummaryRow> selected)
    {
        try
        {
            // An empty selection leaves the pane blank (Single mode, all nulls);
            // the all-time rollup lives in its own window (ShowLifetimeTotals).
            await Content.LoadAsync(selected).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reports content failed to load for selection of {Count} session(s).", selected.Count);
        }
    }

    /// <summary>
    /// Open the standalone Lifetime Totals window. Modeless; the seam
    /// re-fetches the all-time rollup on each Show so it reflects the current
    /// set of saved sessions.
    /// </summary>
    [RelayCommand]
    private void ShowLifetimeTotals() => _lifetimeWindow.Show();

    /// <summary>Open the "Build session from history" dialog in date-range mode.</summary>
    [RelayCommand]
    private Task BuildFromHistoryAsync(CancellationToken ct) => RunHistoricalDialogAsync(null, ct);

    private bool CanCombineSelected() => Sidebar.CheckedRows.Count >= 2;

    /// <summary>Open the dialog in combine mode with the sidebar's ticked sessions (needs at least two).</summary>
    [RelayCommand(CanExecute = nameof(CanCombineSelected))]
    private Task CombineSelectedAsync(CancellationToken ct) =>
        RunHistoricalDialogAsync(Sidebar.CheckedRows.Select(r => r.SessionId).ToList(), ct);

    private async Task RunHistoricalDialogAsync(IReadOnlyList<string>? preselected, CancellationToken ct)
    {
        var outcome = _historicalDialog.Show(preselected);
        if (outcome is null) return;
        await Sidebar.LoadAsync(ct).ConfigureAwait(true);
        StatusMessage = outcome.Summary;
    }

    /// <summary>
    /// Import Session command. Picks a .json file via the dialog seam, runs
    /// the importer, and on success refreshes the sidebar without altering
    /// the current selection. Status surface reflects the outcome.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [RelayCommand]
    private async Task ImportSessionAsync(CancellationToken ct)
    {
        var path = _fileDialog.PickOpenJsonPath();
        if (path is null) return;

        try
        {
            var result = await _importer.ImportAsync(path, ct).ConfigureAwait(true);
            await Sidebar.LoadAsync(ct).ConfigureAwait(true);
            StatusMessage = result.AlreadyPresent
                ? $"Already in your database: {result.SessionName}"
                : $"Imported: {result.SessionName}";
        }
        catch (SchemaVersionMismatchException ex)
        {
            _log.LogWarning(ex, "Import refused due to schema_version mismatch.");
            StatusMessage = $"Import failed: {ex.Message}";
        }
        catch (PortableFileFormatException ex)
        {
            _log.LogWarning(ex, "Import refused — file is not a recognisable session export.");
            StatusMessage = $"Import failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Import failed.");
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
}
