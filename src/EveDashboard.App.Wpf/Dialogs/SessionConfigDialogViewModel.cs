namespace EveDashboard.App.Wpf.Dialogs;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Sessions;

/// <summary>
/// ViewModel behind the <see cref="SessionConfigDialog"/>. Wraps the singleton
/// <see cref="SessionController"/> so the bound name + pilot list edits flow
/// directly into the controller's state — same forwarding pattern as the
/// removed inline SessionConfig widget. <see cref="StartCommand"/> is the
/// controller's own <c>StartCommand</c> exposed verbatim (CanExecute wiring
/// + async execution come for free); the dialog observes the controller's
/// <see cref="SessionController.Mode"/> transition into
/// <see cref="SessionWidgetMode.Running"/> as its "session started, please
/// close" signal.
/// </summary>
/// <remarks>
/// On dialog open <see cref="ResetForNewOpen"/> blanks <see cref="SessionName"/>
/// to mirror the prior inline widget's "New Session" behaviour. Pilot toggle
/// state is preserved across sessions per plan-23 decision 2.3 = A.
/// </remarks>
public sealed partial class SessionConfigDialogViewModel : ObservableObject, IDisposable
{
    private readonly SessionController _controller;
    private bool _disposed;

    /// <summary>Pilot toggle list (forwarded by reference from the controller; items raise their own INPC).</summary>
    public ObservableCollection<PilotToggleViewModel> Pilots => _controller.Pilots;

    /// <summary>Round-trip user-typed session name (template variables expanded at Start by the controller).</summary>
    public string SessionName
    {
        get => _controller.SessionName;
        set => _controller.SessionName = value;
    }

    /// <summary>The controller's own Start command, exposed verbatim. CanExecute wiring (name non-empty AND >= 1 pilot toggled on) and async execution are inherited.</summary>
    public ICommand StartCommand => _controller.StartCommand;

    /// <summary>Discards the open and closes the dialog. The controller's name/pilot edits are intentionally NOT reverted (matches the removed inline widget's behaviour).</summary>
    [RelayCommand]
    private void Cancel()
    {
        ShouldClose = true;
    }

    /// <summary>True when the view should close itself. The view subscribes to <see cref="INotifyPropertyChanged.PropertyChanged"/> for <c>ShouldClose</c> and calls <see cref="System.Windows.Window.Close"/>.</summary>
    [ObservableProperty]
    private bool _shouldClose;

    /// <summary>Construct the dialog VM around the singleton session controller.</summary>
    /// <param name="controller">The session controller — must be non-null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="controller"/> is null.</exception>
    public SessionConfigDialogViewModel(SessionController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _controller.PropertyChanged += OnControllerPropertyChanged;
    }

    /// <summary>Reset the dialog state for a fresh open: clear <see cref="ShouldClose"/> and blank the session name (pilot toggles are deliberately preserved).</summary>
    public void ResetForNewOpen()
    {
        ShouldClose = false;
        SessionName = string.Empty;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionController.SessionName):
                OnPropertyChanged(nameof(SessionName));
                break;
            case nameof(SessionController.Mode):
                // The "Start succeeded" signal. Plan 37 changed StartAsync to
                // land in Paused (Start-as-paused) instead of Running, so the
                // close-signal mode flipped from Running to Paused. The dialog
                // is only opened from Config or Stopped (chrome-button
                // visibility), so a transition to Paused while the dialog is
                // visible is unambiguously the "Start just succeeded" signal.
                if (_controller.Mode == SessionWidgetMode.Paused)
                {
                    ShouldClose = true;
                }
                break;
        }
    }

    /// <summary>Dispose: unsubscribe from the controller. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _controller.PropertyChanged -= OnControllerPropertyChanged; } catch { /* defensive */ }
    }
}
