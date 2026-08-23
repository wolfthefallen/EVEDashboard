namespace EveDashboard.App.Wpf.Dialogs;

using System.ComponentModel;
using System.Data.Common;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Themed modal New-Session dialog. Body hosts the saved-fleet list +
/// Create-Fleet flow + session-name editor; bottom row hosts Start (default) and
/// Cancel. A fleet is the only way to start a session.
/// </summary>
/// <remarks>
/// Production callers go through <see cref="ISessionConfigDialog"/> +
/// <see cref="SessionConfigDialogAdapter"/>. The adapter constructs the dialog,
/// resets the VM for a fresh open, sets the Owner, and calls
/// <see cref="Window.ShowDialog"/>. On <see cref="FrameworkElement.Loaded"/> the
/// dialog asks the VM to hydrate the fleet list (see <see cref="OnLoaded"/>). The
/// dialog watches its bound VM's
/// <see cref="SessionConfigDialogViewModel.ShouldClose"/> property and closes
/// when it flips to true (Start success or Cancel click).
/// </remarks>
public partial class SessionConfigDialog : Window
{
    private readonly SessionConfigDialogViewModel _vm;
    private readonly ILogger<SessionConfigDialog> _log;

    /// <summary>Construct the dialog window with its DI-resolved VM.</summary>
    /// <param name="vm">The dialog VM; bound as <see cref="FrameworkElement.DataContext"/>.</param>
    /// <param name="log">Logger — used to record (and contain) a failed fleet-list hydrate.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="vm"/> or <paramref name="log"/> is null.</exception>
    public SessionConfigDialog(SessionConfigDialogViewModel vm, ILogger<SessionConfigDialog> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _log = log;
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Hydrate the fleet list once the dialog is shown. The
    /// <c>InitializeAsync</c> await is wrapped in a narrow try/catch so a DB/IO
    /// failure never escapes this <c>async void</c> handler as an unhandled
    /// dispatcher exception: we log at Warning and let the dialog open with an
    /// empty fleet list (Start stays disabled — its default state).
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.InitializeAsync();
        }
        catch (Exception ex) when (ex is DbException or IOException)
        {
            _log.LogWarning(ex,
                "Failed to load the fleet list for the New Session dialog; opening with no fleets (Start disabled).");
        }
        catch (Exception ex)
        {
            // There is no app-wide unhandled-exception handler, so ANY unfiltered
            // throw on this async void would crash WPF. Surface unexpected errors
            // at Error and still open the dialog degraded (empty fleet list, Start
            // stays disabled) rather than taking the process down.
            _log.LogError(ex,
                "Unexpected error loading the New Session dialog; opening with no fleets (Start disabled).");
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionConfigDialogViewModel.ShouldClose)) return;
        if (_vm.ShouldClose) Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }
}
