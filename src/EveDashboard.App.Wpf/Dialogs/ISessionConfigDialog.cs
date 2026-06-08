namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Test seam for the New-Session modal. Production wires
/// <see cref="SessionConfigDialogAdapter"/>; the chrome bar's
/// <c>New Session</c> button on <c>MainWindow</c> calls <see cref="Show"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IConfirmDialog"/> in shape. The dialog owns the
/// pilot list + session-name editor + Start/Cancel buttons. Start commits to
/// <c>SessionController.StartCommand</c> and closes; Cancel just closes.
/// </remarks>
public interface ISessionConfigDialog
{
    /// <summary>Show the dialog modally. Blocks until the user clicks Start (which also starts the session) or Cancel/Close.</summary>
    void Show();
}
