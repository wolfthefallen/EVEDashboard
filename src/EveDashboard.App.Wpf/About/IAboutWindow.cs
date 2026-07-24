namespace EveDashboard.App.Wpf.About;

/// <summary>
/// Test seam for the About reference window (plan 52). Production wires
/// <see cref="AboutWindowAdapter"/>; the chrome "About" button on
/// <see cref="MainWindow"/> calls <see cref="Show"/> with no argument.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EveDashboard.App.Wpf.ICrystalDiagramWindow"/>: the About
/// window is a modeless reference window. The user keeps mining in
/// <c>MainWindow</c> while it floats alongside.
/// </remarks>
public interface IAboutWindow
{
    /// <summary>
    /// Show the About window. Modeless — does not block
    /// <see cref="MainWindow"/>. Subsequent calls bring an already-open
    /// window to the front.
    /// </summary>
    void Show();
}
