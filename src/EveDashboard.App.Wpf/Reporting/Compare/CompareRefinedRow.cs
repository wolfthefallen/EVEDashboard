namespace EveDashboard.App.Wpf.Reporting.Compare;

/// <summary>
/// Plan 45: one row in the compare-mode "Total Refined Output" table — a refined
/// material's display name plus its quantity and ISK value summed across the
/// selected sessions. Raw numeric values (not pre-rendered strings, unlike
/// <see cref="CompareRow"/>) so the DataGrid sorts them numerically and formats
/// via column <c>StringFormat</c>.
/// </summary>
/// <param name="Material">Refined-material display name (SDE-resolved, or <c>#&lt;typeId&gt;</c>).</param>
/// <param name="Quantity">Total yield-applied units across the selected sessions.</param>
/// <param name="Isk">Total ISK value across the selected sessions (<c>0</c> for an unpriced material).</param>
public sealed record CompareRefinedRow(
    string Material,
    long   Quantity,
    double Isk);
