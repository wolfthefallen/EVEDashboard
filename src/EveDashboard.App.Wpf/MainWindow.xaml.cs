namespace EveDashboard.App.Wpf;

using System.Windows;
using System.Windows.Controls;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Geometry;
using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Services;
using EveDashboard.Core.Layout;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 17 + 22.1 + 19 shell: outer 2-row Grid. Row 0 is the chrome row
/// (Settings, session controls, Hidden-widgets popup trigger); Row 1 is a
/// single ScrollViewer + ItemsControl + WrapPanel containing all widgets
/// (Session + per-pilots) in user-definable order. Bound to a single
/// <see cref="MainWindowViewModel"/> exposing the
/// <see cref="EveDashboard.App.Wpf.Layout.LayoutCoordinator"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly WindowGeometryService _geometryService;
    private readonly ILogger<MainWindow> _log;
    private readonly ISettingsDialog _settingsDialog;
    private readonly IReportsWindow _reportsWindow;
    private readonly ICrystalDiagramWindow _crystalDiagramWindow;
    private readonly ISessionConfigDialog _sessionConfigDialog;
    private readonly AutoPauseSoundPlayer _soundPlayer;

    /// <summary>Construct the MainWindow with its dependencies. DI-resolved.</summary>
    /// <param name="vm">The MainWindow VM exposing the layout coordinator.</param>
    /// <param name="geometryService">Window-geometry persistence service.</param>
    /// <param name="log">Logger.</param>
    /// <param name="settingsDialog">Plan 22.1 Settings dialog seam, opened by the chrome Settings button.</param>
    /// <param name="reportsWindow">Plan 25 Reports window seam, opened by the chrome Reports button.</param>
    /// <param name="crystalDiagramWindow">Crystal Diagram reference-window seam, opened by the chrome Crystal Diagram button.</param>
    /// <param name="sessionConfigDialog">Post-plan-25 New Session dialog seam, opened by the chrome New Session button.</param>
    /// <param name="soundPlayer">Plan 37 auto-pause / auto-resume sound player. Subscribed to the session controller's sound-request events.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public MainWindow(
        MainWindowViewModel vm,
        WindowGeometryService geometryService,
        ILogger<MainWindow> log,
        ISettingsDialog settingsDialog,
        IReportsWindow reportsWindow,
        ICrystalDiagramWindow crystalDiagramWindow,
        ISessionConfigDialog sessionConfigDialog,
        AutoPauseSoundPlayer soundPlayer)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(geometryService);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(settingsDialog);
        ArgumentNullException.ThrowIfNull(reportsWindow);
        ArgumentNullException.ThrowIfNull(crystalDiagramWindow);
        ArgumentNullException.ThrowIfNull(sessionConfigDialog);
        ArgumentNullException.ThrowIfNull(soundPlayer);
        _vm = vm;
        _geometryService = geometryService;
        _log = log;
        _settingsDialog = settingsDialog;
        _reportsWindow = reportsWindow;
        _crystalDiagramWindow = crystalDiagramWindow;
        _sessionConfigDialog = sessionConfigDialog;
        _soundPlayer = soundPlayer;

        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;

        // Plan 37: wire the sound player to the controller's request events.
        // Both objects are singletons living for the app's lifetime, so no
        // unsubscription is needed.
        _vm.Session.AutoPauseSoundRequested  += (_, _) => _soundPlayer.PlayPauseSound();
        _vm.Session.AutoResumeSoundRequested += (_, _) => _soundPlayer.PlayResumeSound();
    }

    /// <summary>
    /// Async initialization called by <c>App.OnStartup</c> before
    /// <see cref="Window.Show"/>. Detects an open <c>session_time</c> row and
    /// puts the Session widget into Resume mode if needed.
    /// </summary>
    public Task InitializeAsync()
    {
        return _vm.Session.InitializeAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _geometryService.ApplyOnStartupAsync(this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WindowGeometryService.ApplyOnStartupAsync failed; default WPF placement applies.");
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsDialog.Show();
    }

    private void ReportsButton_Click(object sender, RoutedEventArgs e)
    {
        _reportsWindow.Show();
    }

    private void CrystalDiagramButton_Click(object sender, RoutedEventArgs e)
    {
        _crystalDiagramWindow.Show();
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _sessionConfigDialog.Show();
    }

    private void HiddenItemShowButton_Click(object sender, RoutedEventArgs e)
    {
        // Tag binding carries the WidgetDescriptor for the row's hidden item.
        if (sender is Button btn && btn.Tag is WidgetDescriptor descriptor)
        {
            _vm.Layout.Show(descriptor);
        }
    }
}
