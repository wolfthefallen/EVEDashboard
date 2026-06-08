namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Test seam for parameterised confirm-style modal dialogs (Stop session,
/// Delete session, etc.). Production wires <see cref="ConfirmDialogAdapter"/>;
/// tests substitute a fake that returns a fixed bool to drive both the
/// confirm and cancel branches without invoking WPF.
/// </summary>
/// <remarks>
/// Generalised in plan 29 from the original <c>IConfirmStopDialog</c> seam.
/// One dialog now serves every "are you sure?" flow in the app — caller
/// supplies the title, body, and confirm-button label.
/// </remarks>
public interface IConfirmDialog
{
    /// <summary>Show the modal confirm dialog with the supplied strings and return the user's choice.</summary>
    /// <param name="title">Window title (e.g. <c>"Stop session?"</c>, <c>"Delete session?"</c>).</param>
    /// <param name="body">Body text shown above the buttons. Wraps as needed.</param>
    /// <param name="confirmLabel">Label for the confirm button (e.g. <c>"Stop"</c>, <c>"Delete"</c>, <c>"OK"</c>). The Cancel button is always labelled "Cancel".</param>
    /// <returns><c>true</c> if the user clicked the confirm button; <c>false</c> if the user clicked Cancel or closed the dialog without picking either button.</returns>
    bool Show(string title, string body, string confirmLabel);
}
