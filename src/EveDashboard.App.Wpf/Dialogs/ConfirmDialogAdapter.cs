namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;

/// <summary>
/// Production <see cref="IConfirmDialog"/> implementation. Constructs a fresh
/// <see cref="ConfirmDialog"/> per call with the caller-supplied strings,
/// parents it on the current <see cref="Application.MainWindow"/> (or the
/// active modal if one is open above it), shows it modally, and returns the
/// bool DialogResult.
/// </summary>
public sealed class ConfirmDialogAdapter : IConfirmDialog
{
    /// <inheritdoc />
    public bool Show(string title, string body, string confirmLabel, string cancelLabel = "Cancel")
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(confirmLabel);
        ArgumentNullException.ThrowIfNull(cancelLabel);

        var dialog = new ConfirmDialog(title, body, confirmLabel, cancelLabel)
        {
            Owner = ResolveOwner(),
        };
        return dialog.ShowDialog() == true;
    }

    private static Window? ResolveOwner()
    {
        var app = Application.Current;
        if (app is null) return null;

        // Prefer the topmost active window so the modal centers over its caller
        // (e.g. the Reports window) rather than over MainWindow.
        foreach (Window w in app.Windows)
        {
            if (w.IsActive) return w;
        }
        return app.MainWindow;
    }
}
