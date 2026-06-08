namespace EveDashboard.App.Wpf.Reporting.Compare;

/// <summary>
/// Plan 36: one row inside the decluttered compare-mode widget. Values are
/// pre-rendered strings — the view binds <c>Label</c>/<c>Avg</c>/<c>Total</c>
/// directly with no XAML value converters.
/// </summary>
/// <param name="Label">Row label (e.g. "ISK", "Time", "m³", "Crit ISK %").</param>
/// <param name="Avg">Pre-formatted Avg cell.</param>
/// <param name="Total">
/// Pre-formatted Total cell. <c>null</c> for rate rows
/// (<c>m³/sec</c>, <c>ISK/h</c>) — the rate DataGrid omits the Total column
/// entirely so the binding never executes.
/// </param>
public sealed record CompareRow(
    string  Label,
    string  Avg,
    string? Total);
