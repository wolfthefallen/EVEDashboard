namespace EveDashboard.App.Wpf;

using System.ComponentModel;
using System.Windows;
using EveDashboard.App.Wpf.Geometry;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Modeless reference-image window for the ore -> mineral cross-reference
/// diagram. The top of the content area holds a <c>ComboBox</c> letting
/// the user pick "All / Full Chart" or any of the 7 nullsec SOV asteroid
/// deposits; selection is persisted via
/// <see cref="ISettingsRepository.SetSelectedCrystalAnomalyAsync(string, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Image-only viewer beyond the dropdown. Singleton + hide-on-close (mirrors
/// <see cref="Reporting.ReportsWindow"/>) so window geometry and the in-VM
/// selection survive in-session open/close cycles. Cross-restart persistence
/// for both is handled by their respective settings keys
/// (<c>crystal_diagram_window_geometry</c> + <c>crystal_diagram_selected_anomaly</c>).
/// </para>
/// <para>
/// The window deliberately does NOT use <c>SizeToContent="WidthAndHeight"</c>:
/// that made it open too large, clip the image past the screen edges, and
/// refuse to rescale when FancyZones snap-resized it (WPF only clears
/// SizeToContent on a manual drag-resize). Instead the size comes from
/// <see cref="WindowGeometryService"/> (900x650 centered default, persisted
/// thereafter) and the Image uses Stretch="Uniform" so it scales inside
/// whatever area the window allocates.
/// </para>
/// </remarks>
public partial class CrystalDiagramWindow : Window
{
    private readonly CrystalDiagramWindowViewModel _vm;
    private readonly WindowGeometryService _geometryService;
    private readonly ILogger<CrystalDiagramWindow> _log;

    /// <summary>Construct the Crystal Diagram window.</summary>
    /// <param name="vm">VM exposing the anomaly entries + selected entry + persistence.</param>
    /// <param name="geometryService">Crystal-flavoured geometry persistence service.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public CrystalDiagramWindow(
        CrystalDiagramWindowViewModel vm,
        WindowGeometryService geometryService,
        ILogger<CrystalDiagramWindow> log)
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
            _log.LogWarning(ex, "Crystal Diagram WindowGeometryService.ApplyOnStartupAsync failed; default placement applies.");
        }

        try
        {
            await _vm.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Crystal Diagram VM InitializeAsync failed; dropdown stays at the default 'all' entry.");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide instead of close so the singleton lifetime + restored geometry/selection
        // survive in-session open/close cycles. App shutdown disposes the window via DI container.
        e.Cancel = true;
        Hide();
    }
}
