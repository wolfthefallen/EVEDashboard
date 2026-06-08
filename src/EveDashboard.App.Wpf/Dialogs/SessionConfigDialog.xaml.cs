namespace EveDashboard.App.Wpf.Dialogs;

using System.ComponentModel;
using System.Windows;

/// <summary>
/// Themed modal New-Session dialog. Body owns the pilot list + session-name
/// editor; bottom row hosts Start (default) and Cancel buttons. Replaces the
/// inline SessionConfig widget shipped in plan 24.
/// </summary>
/// <remarks>
/// Production callers go through <see cref="ISessionConfigDialog"/> +
/// <see cref="SessionConfigDialogAdapter"/>. The adapter constructs the dialog,
/// resets the VM for a fresh open, sets the Owner, and calls
/// <see cref="Window.ShowDialog"/>. The dialog itself watches its bound VM's
/// <see cref="SessionConfigDialogViewModel.ShouldClose"/> property and closes
/// when it flips to true (Start success or Cancel click).
/// </remarks>
public partial class SessionConfigDialog : Window
{
    private readonly SessionConfigDialogViewModel _vm;

    /// <summary>Construct the dialog window with its DI-resolved VM.</summary>
    /// <param name="vm">The dialog VM; bound as <see cref="FrameworkElement.DataContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="vm"/> is null.</exception>
    public SessionConfigDialog(SessionConfigDialogViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Closed += OnClosed;
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
