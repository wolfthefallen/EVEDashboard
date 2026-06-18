namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.ComponentModel;
using System.Windows;
using EveDashboard.App.Wpf.Geometry;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 51: standalone Lifetime Totals window. Modeless, singleton, hide-on-close
/// (mirrors <see cref="ReportsWindow"/> / <see cref="CrystalDiagramWindow"/>) so
/// the restored geometry survives in-session open/close cycles. Hosts the reused
/// <see cref="LifetimeSummaryView"/> widget; the all-time rollup it shows is
/// (re)loaded by the adapter on every <c>Show</c> via <see cref="RefreshAsync"/>,
/// not on load (the data is global and can change while the window is hidden).
/// </summary>
/// <remarks>
/// <see cref="OnLoaded"/> only applies persisted geometry (default 700x640
/// centered on first launch via <c>lifetime_totals_window_geometry</c>). The
/// rollup data load lives in the adapter's <c>Show</c> path so a hidden-then-
/// reshown window always reflects current sessions.
/// </remarks>
public partial class LifetimeTotalsWindow : Window
{
    private readonly LifetimeTotalsWindowViewModel _vm;
    private readonly WindowGeometryService _geometryService;
    private readonly ILogger<LifetimeTotalsWindow> _log;

    /// <summary>Construct the Lifetime Totals window.</summary>
    /// <param name="vm">VM owning the reusable lifetime-rollup widget VM + refresh.</param>
    /// <param name="geometryService">Lifetime-flavoured geometry persistence service.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public LifetimeTotalsWindow(
        LifetimeTotalsWindowViewModel vm,
        WindowGeometryService geometryService,
        ILogger<LifetimeTotalsWindow> log)
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

    /// <summary>Re-fetch the all-time rollup. Driven by the adapter on every <c>Show</c>.</summary>
    public Task RefreshAsync() => _vm.RefreshAsync();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _geometryService.ApplyOnStartupAsync(this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Lifetime Totals WindowGeometryService.ApplyOnStartupAsync failed; default placement applies.");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide instead of close so the singleton lifetime + restored geometry
        // survive in-session open/close cycles. App shutdown disposes the window
        // via the DI container.
        e.Cancel = true;
        Hide();
    }
}
