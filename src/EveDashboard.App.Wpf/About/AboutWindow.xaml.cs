namespace EveDashboard.App.Wpf.About;

using System.ComponentModel;
using System.Windows;
using System.Windows.Navigation;
using EveDashboard.App.Wpf.Geometry;
using EveDashboard.App.Wpf.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 52: modeless, singleton, hide-on-close About window (mirrors
/// <see cref="CrystalDiagramWindow"/> / <see cref="Reporting.Lifetime.LifetimeTotalsWindow"/>)
/// so the restored geometry survives in-session open/close cycles. Shows the app
/// version (with a clickable GitHub link), the SDE snapshot date, three thank-you
/// lines, and the CCP legal statement.
/// </summary>
/// <remarks>
/// <see cref="OnLoaded"/> applies persisted geometry (default 460x560 centered on
/// first launch via <c>about_window_geometry</c>) then asks the VM to fill in the
/// SDE date. The GitHub hyperlink opens in the user's default browser via
/// <see cref="IBrowserLauncher"/>; the navigation is handled in-app so WPF does
/// not try to navigate the window itself.
/// </remarks>
public partial class AboutWindow : Window
{
    private readonly AboutWindowViewModel _vm;
    private readonly WindowGeometryService _geometryService;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly ILogger<AboutWindow> _log;

    /// <summary>Construct the About window.</summary>
    /// <param name="vm">VM exposing the version, repository URL, and SDE date.</param>
    /// <param name="geometryService">About-flavoured geometry persistence service.</param>
    /// <param name="browserLauncher">Opens the GitHub link in the default browser.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public AboutWindow(
        AboutWindowViewModel vm,
        WindowGeometryService geometryService,
        IBrowserLauncher browserLauncher,
        ILogger<AboutWindow> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(geometryService);
        ArgumentNullException.ThrowIfNull(browserLauncher);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _geometryService = geometryService;
        _browserLauncher = browserLauncher;
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
            _log.LogWarning(ex, "About WindowGeometryService.ApplyOnStartupAsync failed; default placement applies.");
        }

        try
        {
            await _vm.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "About VM InitializeAsync failed; SDE date stays 'unknown'.");
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

    private void OnRepositoryNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Open in the default browser rather than letting WPF navigate the window.
        _browserLauncher.Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
