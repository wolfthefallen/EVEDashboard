namespace EveDashboard.App.Wpf.Reporting.Lifetime;

/// <summary>
/// Test seam for the Lifetime Totals window. Production wires
/// <see cref="LifetimeTotalsWindowAdapter"/>; the "Lifetime Totals" button in
/// the Reports chrome strip calls <see cref="Show"/> with no argument.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IReportsWindow"/> / <see cref="EveDashboard.App.Wpf.ICrystalDiagramWindow"/>:
/// the Lifetime Totals window is a modeless reference window. The user keeps the
/// Reports window (and MainWindow) usable while the totals float alongside.
/// </remarks>
public interface ILifetimeTotalsWindow
{
    /// <summary>
    /// Show the Lifetime Totals window. Modeless — does not block the Reports
    /// window or MainWindow. Subsequent calls bring an already-open window to
    /// the front. Each call refreshes the all-time totals so they reflect the
    /// current set of saved sessions.
    /// </summary>
    void Show();
}
