namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;

/// <summary>
/// Themed modal confirm dialog. Title, body text, and confirm-button label
/// are supplied at construction time; the Cancel button is always labelled
/// "Cancel". Plan 29 generalisation of the plan-16 stop-confirm dialog.
/// </summary>
/// <remarks>
/// Production callers go through <see cref="IConfirmDialog"/> +
/// <see cref="ConfirmDialogAdapter"/> for testability; this <see cref="Window"/>
/// is the actual UI the adapter constructs.
/// </remarks>
public partial class ConfirmDialog : Window
{
    /// <summary>Construct the dialog with the supplied strings.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="body">Body text shown above the buttons.</param>
    /// <param name="confirmLabel">Label for the confirm button.</param>
    public ConfirmDialog(string title, string body, string confirmLabel)
    {
        InitializeComponent();
        Title                  = title;
        BodyTextBlock.Text     = body;
        ConfirmButton.Content  = confirmLabel;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
