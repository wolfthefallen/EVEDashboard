namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Test seam for the Reports-window file dialogs (open/save). Production wires
/// <see cref="ReportsFileDialogAdapter"/>; unit tests substitute a fake that
/// returns a fixed path or <c>null</c> to drive the OK and Cancel branches
/// without invoking WPF.
/// </summary>
/// <remarks>
/// Same shape as the other dialog seams in this folder
/// (<see cref="IAddStationDialog"/>, <see cref="IConfirmDialog"/>) — the
/// concrete adapter wraps the WPF-specific <c>OpenFileDialog</c> /
/// <c>SaveFileDialog</c> from <c>Microsoft.Win32</c>; the interface stays
/// WPF-free so VMs are unit-testable.
/// </remarks>
public interface IReportsFileDialog
{
    /// <summary>Show an Open File dialog filtered to <c>.json</c> files.</summary>
    /// <returns>Absolute path of the chosen file, or <c>null</c> if the user cancelled.</returns>
    string? PickOpenJsonPath();

    /// <summary>Show a Save File dialog filtered to <c>.json</c> with the supplied default filename.</summary>
    /// <param name="defaultName">Default filename without extension.</param>
    /// <returns>Absolute path of the chosen file, or <c>null</c> if the user cancelled.</returns>
    string? PickSaveJsonPath(string defaultName);

    /// <summary>Show a Save File dialog filtered to <c>.xlsx</c> with the supplied default filename.</summary>
    /// <param name="defaultName">Default filename without extension.</param>
    /// <returns>Absolute path of the chosen file, or <c>null</c> if the user cancelled.</returns>
    string? PickSaveExcelPath(string defaultName);
}
