namespace EveDashboard.App.Wpf.Reporting;

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Core.Settings;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Compare;
using EveDashboard.Reporting.Queries;
using EveDashboard.SessionIO.Excel;
using EveDashboard.SessionIO.Json;
using EveDashboard.SessionIO.Tables;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sidebar VM for the Reports window — sortable list of saved sessions with
/// metadata (name, start, duration, pilots, total ISK). Compare-mode selection
/// is driven by a per-row <see cref="SessionRowViewModel.IsChecked"/>
/// checkbox; the DataGrid's row-highlight selection is purely a visual
/// affordance for right-click context and does NOT affect the compare view.
/// Selection-order is preserved via <see cref="SelectionOrderTracker{T}"/> so
/// the first-checked session maps to column <c>S1</c> in compare mode.
/// Checked-state changes raise <see cref="SelectionChanged"/>; the
/// <see cref="ReportsWindowViewModel"/> subscribes and routes the checked rows
/// (in click order) to <see cref="BreakdownContentViewModel.LoadAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="ExpandedWidth"/> persists to <c>reports_sidebar_width</c> via
/// <see cref="ISettingsRepository"/> on every change (the user resizes the
/// sidebar by dragging the GridSplitter). <see cref="LoadAsync"/> restores it
/// on Reports-window open. Checkbox state persists across Hide/Show within
/// the app session: the Reports window is Hide-on-close so the VM survives,
/// and <see cref="LoadAsync"/> (called on first open + after Import/Delete)
/// snapshots the previously-checked <c>SessionId</c>s and click order, then
/// re-applies <c>IsChecked</c> to surviving wrappers in original order.
/// </remarks>
public sealed partial class SessionsSidebarViewModel : ObservableObject, IColumnWidthStore
{
    private readonly IReportingQueries _queries;
    private readonly ISettingsRepository _settings;
    private readonly JsonSessionExporter _jsonExporter;
    private readonly ExcelSessionExporter _excelExporter;
    private readonly SessionTableDeleter _deleter;
    private readonly IReportsFileDialog _fileDialog;
    private readonly IConfirmDialog _confirmDialog;
    private readonly ILogger<SessionsSidebarViewModel> _log;

    /// <summary>
    /// Optional callback for surfacing one-line status updates ("Exported: ..."
    /// / "Export failed: ..."). The Reports root VM wires this to its own
    /// <c>StatusMessage</c> property so the sidebar VM stays free of XAML
    /// coupling. Plan 29 alignment 7.1 = B.
    /// </summary>
    public Action<string>? StatusReporter { get; set; }

    /// <summary>Bound DataGrid rows — wrapper VMs around <see cref="SessionSummaryRow"/> with an observable <c>IsChecked</c>. Rebuilt by <see cref="LoadAsync"/>.</summary>
    [ObservableProperty]
    private IReadOnlyList<SessionRowViewModel> _rows = Array.Empty<SessionRowViewModel>();

    /// <summary>
    /// Currently checked sidebar rows in check (click) order, projected to the
    /// underlying <see cref="SessionSummaryRow"/> data records. Empty list
    /// when no rows are checked. The first entry maps to column <c>S1</c> in
    /// compare mode, the second to <c>S2</c>, etc.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<SessionSummaryRow> _checkedRows = Array.Empty<SessionSummaryRow>();

    /// <summary>Sidebar's width in device-independent pixels. Persisted to <c>reports_sidebar_width</c>. Default 280.</summary>
    [ObservableProperty]
    private double _expandedWidth = 280;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, double>?> LoadColumnWidthsAsync(string gridKey, CancellationToken ct = default)
        => _settings.GetReportsColumnWidthsAsync(gridKey, ct);

    /// <inheritdoc />
    public void SaveColumnWidths(string gridKey, IReadOnlyDictionary<int, double> widths)
    {
        ArgumentNullException.ThrowIfNull(gridKey);
        ArgumentNullException.ThrowIfNull(widths);
        _ = SaveColumnWidthsAsync(gridKey, widths);
    }

    private async Task SaveColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths)
    {
        try
        {
            await _settings.SetReportsColumnWidthsAsync(gridKey, widths).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist Reports sidebar column widths for grid '{Grid}'.", gridKey);
        }
    }

    /// <summary>Raised whenever <see cref="CheckedRows"/> changes — including transitions to the empty selection. Carries the new ordered selection.</summary>
    public event EventHandler<IReadOnlyList<SessionSummaryRow>>? SelectionChanged;

    /// <summary>Tracks check-order across PropertyChanged toggles from each row VM.</summary>
    private readonly SelectionOrderTracker<SessionRowViewModel> _tracker = new();

    /// <summary>Construct the sidebar VM.</summary>
    /// <param name="queries">Reporting queries facade.</param>
    /// <param name="settings">Settings repository for sidebar geometry persistence.</param>
    /// <param name="jsonExporter">Plan 29 — JSON exporter, wired to the Export to JSON context-menu command.</param>
    /// <param name="excelExporter">Plan 29 — Excel exporter, wired to the Export to Excel context-menu command.</param>
    /// <param name="deleter">Plan 29 follow-up — session deleter, wired to the Delete... context-menu command.</param>
    /// <param name="fileDialog">Plan 29 — file dialog seam for save-path selection.</param>
    /// <param name="confirmDialog">Plan 29 follow-up — confirm-dialog seam used by the Delete... command.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SessionsSidebarViewModel(
        IReportingQueries    queries,
        ISettingsRepository  settings,
        JsonSessionExporter  jsonExporter,
        ExcelSessionExporter excelExporter,
        SessionTableDeleter  deleter,
        IReportsFileDialog   fileDialog,
        IConfirmDialog       confirmDialog,
        ILogger<SessionsSidebarViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jsonExporter);
        ArgumentNullException.ThrowIfNull(excelExporter);
        ArgumentNullException.ThrowIfNull(deleter);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(confirmDialog);
        ArgumentNullException.ThrowIfNull(log);
        _queries = queries;
        _settings = settings;
        _jsonExporter = jsonExporter;
        _excelExporter = excelExporter;
        _deleter = deleter;
        _fileDialog = fileDialog;
        _confirmDialog = confirmDialog;
        _log = log;
    }

    /// <summary>
    /// Load the list of saved sessions and restore sidebar width from
    /// settings. Call on Reports-window open. Subsequent calls (after Import
    /// or Delete) preserve checked state for sessions that still exist in the
    /// new row set — the previous tracker's click-order is snapshotted by
    /// <see cref="SessionSummaryRow.SessionId"/> and re-applied to the freshly
    /// fetched wrappers.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var savedWidth = await _settings.GetReportsSidebarWidthAsync(ct).ConfigureAwait(true);
            // Suppress persistence during the restore — the partial method
            // wraps the setter so we'd otherwise double-write the just-read value.
            _suppressPersistence = true;
            try
            {
                if (savedWidth.HasValue && savedWidth.Value > 0) ExpandedWidth = savedWidth.Value;
            }
            finally { _suppressPersistence = false; }

            // Snapshot the currently-checked session ids in click order BEFORE
            // discarding old wrappers, so we can restore the checked state on
            // surviving rows after the rebuild.
            var prevCheckedIdsInOrder = _tracker.SelectedInOrder.Select(r => r.SessionId).ToList();

            // Unsubscribe from old wrapper VMs to avoid an invocation-list leak.
            foreach (var old in Rows)
            {
                old.PropertyChanged -= OnRowCheckedChanged;
            }
            _tracker.Clear();

            var report = await _queries.GetAllSessionSummariesAsync(ct).ConfigureAwait(true);
            var dataRows = report.Sessions;
            var wrappers = new List<SessionRowViewModel>(dataRows.Count);
            foreach (var data in dataRows)
            {
                var vm = new SessionRowViewModel(data);
                vm.PropertyChanged += OnRowCheckedChanged;
                wrappers.Add(vm);
            }
            Rows = wrappers;

            // Index by session id for O(1) lookup during restore.
            var byId = wrappers.ToDictionary(w => w.SessionId);

            // Suppress the per-toggle SelectionChanged event during restore so
            // the downstream LoadAsync only fires once at the end (avoid an N-fire
            // storm when restoring many checks).
            _suppressSelectionEvents = true;
            try
            {
                foreach (var id in prevCheckedIdsInOrder)
                {
                    if (byId.TryGetValue(id, out var wrapper))
                    {
                        wrapper.IsChecked = true;  // raises PropertyChanged synchronously
                    }
                }
            }
            finally
            {
                _suppressSelectionEvents = false;
            }

            // Publish the final restored state in one event, even if nothing was
            // restored (empty list — needed so the breakdown pane clears after a
            // delete that removed the only checked session).
            CheckedRows = _tracker.SelectedInOrder.Select(r => r.Data).ToArray();
            SelectionChanged?.Invoke(this, CheckedRows);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SessionsSidebarViewModel.LoadAsync failed; sidebar renders empty.");
            Rows = Array.Empty<SessionRowViewModel>();
            CheckedRows = Array.Empty<SessionSummaryRow>();
            SelectionChanged?.Invoke(this, CheckedRows);
        }
    }

    private bool _suppressPersistence;
    private bool _suppressSelectionEvents;

    /// <summary>
    /// Per-row PropertyChanged handler: when a <see cref="SessionRowViewModel.IsChecked"/>
    /// flips, update the tracker and republish <see cref="CheckedRows"/>.
    /// </summary>
    private void OnRowCheckedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SessionRowViewModel row) return;
        if (e.PropertyName != nameof(SessionRowViewModel.IsChecked)) return;

        if (row.IsChecked)
        {
            _tracker.Apply(added: new[] { row }, removed: Array.Empty<SessionRowViewModel>());
        }
        else
        {
            _tracker.Apply(added: Array.Empty<SessionRowViewModel>(), removed: new[] { row });
        }

        if (_suppressSelectionEvents) return;

        CheckedRows = _tracker.SelectedInOrder.Select(r => r.Data).ToArray();
        SelectionChanged?.Invoke(this, CheckedRows);
    }

    partial void OnExpandedWidthChanged(double value)
    {
        if (_suppressPersistence) return;
        _ = PersistWidthAsync(value);
    }

    private async Task PersistWidthAsync(double value)
    {
        try { await _settings.SetReportsSidebarWidthAsync(value).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogWarning(ex, "Persisting reports_sidebar_width failed."); }
    }

    /// <summary>Plan 29 — export the right-clicked session to a user-chosen JSON file.</summary>
    /// <param name="row">Session row from the sidebar DataGrid context menu.</param>
    [RelayCommand]
    private async Task ExportJsonAsync(SessionSummaryRow? row)
    {
        if (row is null) return;
        var path = _fileDialog.PickSaveJsonPath(SuggestedFileName(row.Name));
        if (path is null) return;
        try
        {
            await _jsonExporter.ExportAsync(row.SessionId, path).ConfigureAwait(true);
            StatusReporter?.Invoke($"Exported: {row.Name}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Export to JSON failed for session {SessionId}.", row.SessionId);
            StatusReporter?.Invoke($"Export failed: {ex.Message}");
        }
    }

    /// <summary>Plan 29 — export the right-clicked session to a user-chosen Excel workbook.</summary>
    /// <param name="row">Session row from the sidebar DataGrid context menu.</param>
    [RelayCommand]
    private async Task ExportExcelAsync(SessionSummaryRow? row)
    {
        if (row is null) return;
        var path = _fileDialog.PickSaveExcelPath(SuggestedFileName(row.Name));
        if (path is null) return;
        try
        {
            await _excelExporter.ExportAsync(row.SessionId, path).ConfigureAwait(true);
            StatusReporter?.Invoke($"Exported: {row.Name}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Export to Excel failed for session {SessionId}.", row.SessionId);
            StatusReporter?.Invoke($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Slugify a session name into something a filesystem will accept as a
    /// default filename. Strips the characters Windows forbids in filenames.
    /// </summary>
    private static string SuggestedFileName(string sessionName)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(sessionName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "session" : clean;
    }

    /// <summary>
    /// Plan 29 follow-up — delete the right-clicked session and its FK-cascading
    /// rows after the user confirms. Pilots, market_data, and market_stations
    /// are deliberately untouched (no direct session FK; same rule the user
    /// stated for market_data). After the delete commits, <see cref="LoadAsync"/>
    /// rebuilds the row set and re-restores checked state on the survivors —
    /// the deleted session simply doesn't reappear, so its check drops
    /// naturally without disturbing the other checked rows.
    /// </summary>
    /// <param name="row">Session row from the sidebar DataGrid context menu.</param>
    [RelayCommand]
    private async Task DeleteSessionAsync(SessionSummaryRow? row)
    {
        if (row is null) return;

        var confirmed = _confirmDialog.Show(
            title:        "Delete session?",
            body:         $"Are you sure you want to delete '{row.Name}'? It cannot be undone.",
            confirmLabel: "Delete");
        if (!confirmed) return;

        try
        {
            await _deleter.DeleteAsync(row.SessionId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusReporter?.Invoke($"Deleted: {row.Name}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Delete failed for session {SessionId}.", row.SessionId);
            StatusReporter?.Invoke($"Delete failed: {ex.Message}");
        }
    }
}
