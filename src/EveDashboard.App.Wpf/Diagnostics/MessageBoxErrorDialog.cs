namespace EveDashboard.App.Wpf.Diagnostics;

using System.Windows;

/// <summary>
/// Production <see cref="IUnhandledErrorDialog"/>: a standard modal MessageBox.
/// Its text is selectable with Ctrl+C, so the user can copy the error + log path
/// into a bug report without a custom dialog.
/// </summary>
public sealed class MessageBoxErrorDialog : IUnhandledErrorDialog
{
    /// <inheritdoc />
    public void Show(string message) =>
        MessageBox.Show(message, "EVE Dashboard - unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
}
