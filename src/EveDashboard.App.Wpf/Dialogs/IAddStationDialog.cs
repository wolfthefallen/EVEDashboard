namespace EveDashboard.App.Wpf.Dialogs;

using EveDashboard.Core.Settings;

/// <summary>
/// Test seam for the Add-station sub-dialog. Production wires
/// <see cref="AddStationDialogAdapter"/>; <see cref="SettingsDialogViewModel"/>
/// unit tests substitute a fake that returns a fixed <see cref="MarketStation"/>
/// or <c>null</c> to drive both the OK and Cancel branches without invoking WPF.
/// </summary>
/// <remarks>
/// Per plan 22.1 decision 22.D: parent <see cref="SettingsDialogViewModel"/>
/// must be unit-testable in xUnit; routing the sub-dialog through this
/// interface keeps the WPF Window out of the test path. Same shape as
/// <see cref="IConfirmDialog"/> (plan 16, generalised plan 29) but returns a payload on
/// confirm instead of a <see cref="bool"/>.
/// </remarks>
public interface IAddStationDialog
{
    /// <summary>
    /// Show the modal Add-station dialog and return the user's input.
    /// </summary>
    /// <returns>
    /// The new <see cref="MarketStation"/> if the user clicked OK with a
    /// validated set of inputs; <c>null</c> if the user clicked Cancel or
    /// closed the dialog without confirming.
    /// </returns>
    MarketStation? Show();
}
