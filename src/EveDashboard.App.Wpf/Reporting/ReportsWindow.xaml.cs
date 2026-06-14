namespace EveDashboard.App.Wpf.Reporting;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using EveDashboard.App.Wpf.Geometry;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 25 Reports child window — modeless, opened from the chrome-bar
/// Reports button via <see cref="IReportsWindow.Show"/>. Manages its own
/// geometry persistence via the Reports-flavoured
/// <see cref="WindowGeometryService"/> (registered with the Reports settings
/// methods + 1100x700 defaults).
/// </summary>
/// <remarks>
/// Lifecycle: the adapter holds a single instance for the app lifetime and
/// re-shows / brings-to-front on subsequent <see cref="IReportsWindow.Show"/>
/// calls. <c>Closing</c> cancels the close and hides the window so geometry +
/// sidebar state (including the per-row checkbox checked state) survive
/// in-session open/close cycles.
/// </remarks>
public partial class ReportsWindow : Window
{
    private readonly ReportsWindowViewModel _vm;
    private readonly WindowGeometryService _geometryService;
    private readonly ILogger<ReportsWindow> _log;
    private DataGridColumnWidthBinder? _sidebarWidthBinder;

    /// <summary>Construct the Reports window.</summary>
    /// <param name="vm">Reports root VM.</param>
    /// <param name="geometryService">Reports-flavoured geometry persistence service.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public ReportsWindow(
        ReportsWindowViewModel vm,
        WindowGeometryService geometryService,
        ILogger<ReportsWindow> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(geometryService);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _geometryService = geometryService;
        _log = log;

        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _geometryService.ApplyOnStartupAsync(this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reports WindowGeometryService.ApplyOnStartupAsync failed; default placement applies.");
        }

        try
        {
            await _vm.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reports VM InitializeAsync failed; sidebar may be empty.");
        }

        // Plan 47: restore + persist the sidebar grid's column widths (per type, key "sessions").
        // The window is a Hide-on-close singleton, so this binds once for the app lifetime.
        if (_sidebarWidthBinder is null)
        {
            _sidebarWidthBinder = await DataGridColumnWidthBinder
                .AttachAsync(SessionsGrid, "sessions", _vm.Sidebar)
                .ConfigureAwait(true);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide instead of close so the singleton lifetime + restored geometry survive
        // in-session open/close cycles. App shutdown disposes the window via DI container.
        e.Cancel = true;
        Hide();
    }

    private void SidebarGridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Persist the new sidebar width after the user releases the splitter.
        // The Sidebar column width is bound OneWay from ExpandedWidth, so the
        // drag updates the actual column width directly; we read it back here.
        var newWidth = SidebarColumn.ActualWidth;
        if (newWidth > 0)
        {
            _vm.Sidebar.ExpandedWidth = newWidth;
        }
    }
}
