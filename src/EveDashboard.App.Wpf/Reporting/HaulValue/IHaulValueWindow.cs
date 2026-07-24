namespace EveDashboard.App.Wpf.Reporting.HaulValue;

/// <summary>
/// Test seam for the Haul Value window (plan 55). Production wires
/// <see cref="HaulValueWindowAdapter"/>; a chrome button on
/// <see cref="MainWindow"/> calls <see cref="Show"/> with no argument.
/// </summary>
/// <remarks>
/// Modeless, singleton, hide-on-close (mirrors <see cref="EveDashboard.App.Wpf.About.IAboutWindow"/>
/// and the Lifetime Totals window). The user keeps mining in <c>MainWindow</c>
/// while it floats alongside.
/// </remarks>
public interface IHaulValueWindow
{
    /// <summary>
    /// Show the Haul Value window (or bring it to the front if already open) and
    /// refresh its saved-session picker so a just-finished session appears.
    /// </summary>
    void Show();
}
