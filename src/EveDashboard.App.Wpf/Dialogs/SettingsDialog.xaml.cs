namespace EveDashboard.App.Wpf.Dialogs;

using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Themed modal Settings dialog. Glue between the View and the
/// <see cref="SettingsDialogViewModel"/>: owns the WPF
/// <see cref="OpenFolderDialog"/> invocation (UI-thread requirement) and the
/// close-with-unsaved-changes confirm prompt.
/// </summary>
/// <remarks>
/// <para>
/// Production callers go through <see cref="ISettingsDialog"/> +
/// <see cref="SettingsDialogAdapter"/> for testability; this <see cref="Window"/>
/// is the actual UI the adapter constructs from DI per <c>Show()</c> call.
/// </para>
/// <para>
/// When the VM is in bootstrap-failure mode, the close-with-unsaved-
/// changes confirm prompt is suppressed and Cancel/X/Alt-F4 calls
/// <see cref="Application.Shutdown(int)"/> with exit code <c>1</c>. Apply
/// success raises <see cref="SettingsDialogViewModel.BootstrapApplySucceeded"/>
/// which this code-behind handles by re-running <c>App.RunBootstrapChainAsync</c>
/// (cast through <see cref="Application.Current"/>); on chain success the
/// dialog closes cleanly behind the now-open MainWindow.
/// </para>
/// </remarks>
public partial class SettingsDialog : Window
{
    private readonly SettingsDialogViewModel _vm;
    private readonly ILogger<SettingsDialog> _log;

    /// <summary>Set to true after a successful bootstrap-chain re-run, so OnClosing closes cleanly instead of calling Shutdown(1).</summary>
    private bool _bootstrapApplyFinishedSuccessfully;

    /// <summary>Construct the dialog with its ViewModel. DI-resolved.</summary>
    /// <param name="vm">Settings ViewModel; bound to <see cref="FrameworkElement.DataContext"/>.</param>
    /// <param name="log">Logger for Apply / Load failures surfaced to the user.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SettingsDialog(SettingsDialogViewModel vm, ILogger<SettingsDialog> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _log = log;
        DataContext = vm;
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        _vm.BrowseRequested += OnBrowseRequested;
        _vm.BootstrapApplySucceeded += OnBootstrapApplySucceeded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Settings dialog LoadAsync failed.");
            MessageBox.Show(
                this,
                $"Could not load settings: {ex.Message}",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnBrowseRequested(object? sender, EventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select EVE Gamelog folder",
            InitialDirectory = string.IsNullOrWhiteSpace(_vm.GamelogPath) ? string.Empty : _vm.GamelogPath,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _vm.GamelogPath = dlg.FolderName;
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _vm.ApplyAsync().ConfigureAwait(true);
            if (!result.Saved)
            {
                var body = "Cannot save settings - the following are invalid:\n\n"
                           + string.Join("\n", result.Errors);
                MessageBox.Show(this, body, "Cannot save settings",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Settings dialog ApplyAsync threw.");
            MessageBox.Show(
                this,
                $"Could not save settings: {ex.Message}",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Bootstrap-failure mode = no MainWindow exists yet to return to.
        // Cancel/X/Alt-F4 means "give up, exit". The normal-mode dirty-prompt
        // is suppressed in this mode. If Apply has already succeeded for this
        // session, the chain re-run opened MainWindow and the dialog closes
        // cleanly.
        if (_vm.IsBootstrapFailureMode)
        {
            if (!_bootstrapApplyFinishedSuccessfully)
            {
                Application.Current?.Shutdown(1);
            }
            return;
        }

        if (!_vm.IsDirty) return;

        var choice = MessageBox.Show(
            this,
            "You have unsaved changes. Discard them?",
            "Unsaved changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice == MessageBoxResult.No)
        {
            e.Cancel = true;
        }
    }

    /// <summary>
    /// Handler for <see cref="SettingsDialogViewModel.BootstrapApplySucceeded"/>.
    /// Re-runs the bootstrap chain via <see cref="App.RunBootstrapChainAsync"/>;
    /// on chain success, marks the flag and closes the dialog cleanly behind
    /// the now-open MainWindow. On chain failure (e.g. the new path is also
    /// bad), surfaces the exception inline; the dialog stays open so the user
    /// can edit the path again — the ViewModel keeps its bootstrap-failure
    /// state.
    /// </summary>
    private async void OnBootstrapApplySucceeded(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current is App app)
            {
                await app.RunBootstrapChainAsync().ConfigureAwait(true);
            }
            _bootstrapApplyFinishedSuccessfully = true;
            Close();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Bootstrap-chain re-run after Apply succeeded failed.");
            MessageBox.Show(
                this,
                $"The new gamelog folder did not bootstrap cleanly: {ex.Message}\n\nFix the path and try again.",
                "Bootstrap retry failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
