namespace EveDashboard.App.Wpf;

/// <summary>
/// Test seam for the Crystal Diagram reference window. Production wires
/// <see cref="CrystalDiagramWindowAdapter"/>; the chrome "Crystal Diagram"
/// button on <see cref="MainWindow"/> calls <see cref="Show"/> with no argument.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Reporting.IReportsWindow"/>: the Crystal Diagram is a
/// modeless reference window. The user keeps mining in <c>MainWindow</c>
/// while the diagram floats alongside.
/// </remarks>
public interface ICrystalDiagramWindow
{
    /// <summary>
    /// Show the Crystal Diagram window. Modeless — does not block
    /// <see cref="MainWindow"/>. Subsequent calls bring an already-open
    /// window to the front.
    /// </summary>
    void Show();
}
