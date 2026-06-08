namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Test seam for the Settings modal. Production wires
/// <see cref="SettingsDialogAdapter"/>; the chrome Settings button on
/// <c>MainWindow</c> calls <see cref="Show"/> with no argument. Plan 22.2's
/// bootstrap-failure re-route in <c>App.xaml.cs</c> calls it with a non-null
/// <see cref="BootstrapFailureContext"/> to put the dialog into bootstrap-
/// failure mode (only the gamelog-path section visible, banner shown,
/// Apply re-runs the bootstrap chain).
/// </summary>
/// <remarks>
/// Mirrors <see cref="IConfirmDialog"/> (plan 16, generalised plan 29) in shape: an interface
/// the rest of the app depends on, a concrete <c>SettingsDialog</c> Window,
/// and an adapter that wires the two together. Apply/Close pattern means the
/// caller does not need a return value — the dialog persists changes itself
/// when the user clicks Apply.
/// </remarks>
public interface ISettingsDialog
{
    /// <summary>
    /// Show the modal Settings dialog, blocking until the user closes it.
    /// </summary>
    /// <param name="failure">
    /// Plan 22.2: <c>null</c> = normal-mode chrome-button entry; non-null =
    /// bootstrap-failure mode (the dialog hides every section except the
    /// gamelog-path editor, shows <see cref="BootstrapFailureContext.Message"/>
    /// in a banner, and Apply re-runs the bootstrap chain).
    /// </param>
    void Show(BootstrapFailureContext? failure = null);
}
