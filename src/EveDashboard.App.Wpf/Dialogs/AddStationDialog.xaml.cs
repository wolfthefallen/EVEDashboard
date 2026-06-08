namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;
using EveDashboard.Core.Settings;

/// <summary>
/// Themed modal Add-station sub-dialog. Five fields matching
/// <see cref="MarketStation"/>; OK validates and projects, Cancel discards.
/// Production callers go through <see cref="IAddStationDialog"/> +
/// <see cref="AddStationDialogAdapter"/> for testability.
/// </summary>
public partial class AddStationDialog : Window
{
    private readonly AddStationDialogViewModel _vm;

    /// <summary>Construct the dialog with its ViewModel. DI-resolved.</summary>
    /// <param name="vm">The Add-station ViewModel; bound to <see cref="FrameworkElement.DataContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="vm"/> is null.</exception>
    public AddStationDialog(AddStationDialogViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    /// <summary>
    /// The validated station produced on OK, or <c>null</c> if the dialog was
    /// cancelled. Read by <see cref="AddStationDialogAdapter"/> after
    /// <see cref="Window.ShowDialog"/> returns.
    /// </summary>
    public MarketStation? Result { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = _vm.Validate();
        if (errors.Count > 0)
        {
            var body = "Cannot add station - the following are invalid:\n\n"
                       + string.Join("\n", errors);
            MessageBox.Show(this, body, "Invalid station",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Result = _vm.BuildStation();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
