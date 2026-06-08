namespace EveDashboard.App.Wpf.Reporting;

/// <summary>
/// Test seam for the Reports child window. Production wires
/// <see cref="ReportsWindowAdapter"/>; the chrome Reports button on
/// <c>MainWindow</c> calls <see cref="Show"/> with no argument. Plan 25.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EveDashboard.App.Wpf.Dialogs.ISettingsDialog"/> in shape
/// but with <c>Show()</c> semantics rather than <c>ShowDialog()</c>: Reports
/// is modeless so the user can keep mining in <c>MainWindow</c> while
/// reviewing reports in this window.
/// </remarks>
public interface IReportsWindow
{
    /// <summary>
    /// Show the Reports window. Modeless — does not block <c>MainWindow</c>.
    /// Subsequent calls bring an already-open window to the front.
    /// </summary>
    void Show();
}
