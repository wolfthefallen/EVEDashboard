namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Test seam for the New-Session modal. Production wires
/// <see cref="SessionConfigDialogAdapter"/>; the chrome bar's
/// <c>New Session</c> button on <c>MainWindow</c> calls <see cref="Show"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IConfirmDialog"/> in shape. The dialog owns the saved-fleet
/// list + Create-Fleet flow + session-name editor + Start/Cancel buttons (a
/// fleet is the only way to start a session). Start calls
/// <c>SessionController.StartAsync(roster)</c> with the selected fleet and closes
/// on success; Cancel just closes.
/// </remarks>
public interface ISessionConfigDialog
{
    /// <summary>Show the dialog modally. Blocks until the user clicks Start (which also starts the session) or Cancel/Close.</summary>
    void Show();
}
