namespace EveDashboard.App.Wpf.Reporting;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Dialogs;
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
    private readonly ILogger<ReportsWindowViewModel> _log;

    /// <summary>The sortable sidebar of saved sessions.</summary>
    public SessionsSidebarViewModel Sidebar { get; }

    /// <summary>The middle-column breakdown content for the currently selected session.</summary>
    public BreakdownContentViewModel Content { get; }

    /// <summary>
    /// Plan 29 — one-line status surface at the bottom of the Reports window.
    /// Reads "Imported: ..." after a successful import, "Exported: ..." after
    /// an export, and "Import failed: ..." / "Export failed: ..." on errors.
    /// Empty string => the row collapses (see <c>StringEmptyToVisibilityConverter</c>).
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Construct the root Reports VM.</summary>
    /// <param name="sidebar">Sidebar VM.</param>
    /// <param name="content">Breakdown content VM.</param>
    /// <param name="importer">Plan 29 — JSON importer wired to the Import Session header button.</param>
    /// <param name="fileDialog">Plan 29 — file dialog seam for open-path selection.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public ReportsWindowViewModel(
        SessionsSidebarViewModel sidebar,
        BreakdownContentViewModel content,
        JsonSessionImporter importer,
        IReportsFileDialog fileDialog,
        ILogger<ReportsWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(log);
        Sidebar = sidebar;
        Content = content;
        _importer = importer;
        _fileDialog = fileDialog;
        _log = log;
        Sidebar.SelectionChanged += OnSidebarSelectionChanged;
        Sidebar.StatusReporter = msg => StatusMessage = msg;
    }

    /// <summary>Initialize on Reports-window open: load the sidebar (which restores collapse + width).</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task InitializeAsync(CancellationToken ct = default) => Sidebar.LoadAsync(ct);

    private async void OnSidebarSelectionChanged(object? sender, IReadOnlyList<SessionSummaryRow> selected)
    {
        try
        {
            await Content.LoadAsync(selected).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reports content failed to load for selection of {Count} session(s).", selected.Count);
        }
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
