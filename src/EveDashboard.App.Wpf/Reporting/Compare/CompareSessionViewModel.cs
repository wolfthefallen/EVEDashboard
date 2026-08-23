namespace EveDashboard.App.Wpf.Reporting.Compare;

using System.Globalization;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;

/// <summary>
/// The single compare-mode widget VM — compare mode has no per-pilot stack
/// and no per-ore tables. Holds a stacked list of session names, grand-total
/// labels, and three pre-built row collections (Refined + Cumulative + Rates)
/// ready to bind to static-column DataGrids.
/// </summary>
/// <remarks>
/// <para>
/// Layout and math rules:
/// </para>
/// <list type="bullet">
///   <item><description>Cumulative columns are <c>Avg | Total</c>; the Rates column is <c>Avg</c> only.</description></item>
///   <item><description>Rate Avg is computed as <c>sum(m³ or ISK) / sum(active_seconds)</c> over the union of selected sessions, NOT the mean-of-per-session-rates.</description></item>
///   <item><description>No per-ore tables in compare mode.</description></item>
///   <item><description>A single "Crit ISK %" row appears in Cumulative; Crit m³ / Crit count / Crit chance are not shown in compare.</description></item>
///   <item><description>Stacked session-name subheader, one name per line, no dates.</description></item>
///   <item><description><see cref="TotalTimeDisplay"/> / <see cref="TotalIskDisplay"/> grand-total labels and a <see cref="RefinedRows"/> "Total Refined Output" table (Material | Quantity | ISK) render above the Cumulative table.</description></item>
/// </list>
/// <para>
/// All cell values are pre-rendered into strings in the ctor (single pass, no
/// XAML value converters). Inherits
/// <see cref="TextSizeConfigurableWidgetViewModel"/> for the "Text size" cog +
/// column-width persistence (widget-type key <c>"compare"</c>); the base also
/// supplies the <see cref="IDisposable"/> implementation that
/// <c>BreakdownContentViewModel.DisposePriorWidgets</c> relies on.
/// </para>
/// </remarks>
public sealed class CompareSessionViewModel : TextSizeConfigurableWidgetViewModel
{
    /// <summary>Sizeable elements + default emphasis (every label Header0, every table plain).</summary>
    private static readonly IReadOnlyList<(string Key, string Label, StatEmphasis Default)> Elements = new[]
    {
        ("Header", "Comparing sessions", StatEmphasis.Header0),
        ("TotalTime", "TOTAL TIME", StatEmphasis.Header0),
        ("TotalIsk", "TOTAL ISK", StatEmphasis.Header0),
        ("RefinedLabel", "Total Refined Output", StatEmphasis.Header0),
        ("RefinedTable", "Refined table", StatEmphasis.None),
        ("CumulativeLabel", "Cumulative", StatEmphasis.Header0),
        ("CumulativeTable", "Cumulative table", StatEmphasis.None),
        ("RatesLabel", "Rates", StatEmphasis.Header0),
        ("RatesTable", "Rates table", StatEmphasis.None),
    };

    /// <summary>Selected session names in click order, surfaced as the stacked subheader.</summary>
    public IReadOnlyList<string> SessionNames { get; }

    /// <summary>4 rows (ISK, Time, m³, Crit ISK %). DataGrid columns: <c>Stat | Avg | Total</c>.</summary>
    public IReadOnlyList<CompareRow> CumulativeRows { get; }

    /// <summary>2 rows (m³/sec, ISK/h). DataGrid columns: <c>Stat | Avg</c>. Both rows have <c>Total = null</c>.</summary>
    public IReadOnlyList<CompareRow> RateRows { get; }

    /// <summary>Sum of active durations across the selected sessions, pre-rendered <c>HH:mm:ss</c>.</summary>
    public string TotalTimeDisplay { get; }

    /// <summary>Sum of per-session Total ISK across the selected sessions, pre-rendered <c>N0</c>.</summary>
    public string TotalIskDisplay { get; }

    /// <summary>"Total Refined Output" rows — per material, quantity + ISK summed across the selection, sorted by quantity descending.</summary>
    public IReadOnlyList<CompareRefinedRow> RefinedRows { get; }

    /// <summary>Construct the compare-session VM from per-session snapshots + display names (parallel lists in click order).</summary>
    /// <param name="settings">Settings repository (text size + column widths).</param>
    /// <param name="perSessionSnapshots">Per-session <see cref="SessionStats"/> snapshots. Length must equal <paramref name="sessionNames"/>.Count and be >= 1.</param>
    /// <param name="sessionNames">Per-session display names.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the lengths differ or both are empty.</exception>
    public CompareSessionViewModel(
        ISettingsRepository         settings,
        IReadOnlyList<SessionStats> perSessionSnapshots,
        IReadOnlyList<string>       sessionNames)
        : base(settings, "compare", Elements, log: null)
    {
        ArgumentNullException.ThrowIfNull(perSessionSnapshots);
        ArgumentNullException.ThrowIfNull(sessionNames);
        if (perSessionSnapshots.Count != sessionNames.Count)
        {
            throw new ArgumentException(
                $"Snapshot count ({perSessionSnapshots.Count}) must equal session-name count ({sessionNames.Count}).");
        }
        if (perSessionSnapshots.Count == 0)
        {
            throw new ArgumentException("At least one session snapshot is required.");
        }

        SessionNames = sessionNames.ToArray();

        var n         = perSessionSnapshots.Count;
        var totalIsk  = perSessionSnapshots.Sum(s => s.TotalIskMined);
        var totalTime = perSessionSnapshots.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.TotalTimeMined);
        var totalM3   = perSessionSnapshots.Sum(s => s.TotalM3Mined);
        // Crit ISK is summable; Crit ISK % itself uses union-wide totals (NOT
        // mean of per-session percents) so the Avg and Total cells render the
        // same value.
        var totalCritIsk      = perSessionSnapshots.Sum(s => s.TotalIskFromCrits);
        var unionCritIskFrac  = totalIsk > 0 ? totalCritIsk / totalIsk : 0.0;
        var totalSeconds      = totalTime.TotalSeconds;

        // Rate Avg uses union-wide totals over union-wide active seconds, not
        // the mean of per-session rates.
        var rateM3PerSec = totalSeconds > 0 ? totalM3  / totalSeconds        : 0.0;
        var rateIskPerHr = totalSeconds > 0 ? totalIsk / (totalSeconds / 3600.0) : 0.0;

        CumulativeRows = new[]
        {
            new CompareRow(
                Label: "ISK",
                Avg:   (totalIsk / n).ToString("N0", CultureInfo.InvariantCulture),
                Total: totalIsk.ToString("N0", CultureInfo.InvariantCulture)),
            new CompareRow(
                Label: "Time",
                Avg:   ReportFormatting.FormatActiveDuration(TimeSpan.FromSeconds(totalSeconds / n)),
                Total: ReportFormatting.FormatActiveDuration(totalTime)),
            new CompareRow(
                Label: "m³",
                Avg:   (totalM3 / n).ToString("N1", CultureInfo.InvariantCulture),
                Total: totalM3.ToString("N1", CultureInfo.InvariantCulture)),
            // For Crit ISK %, Avg and Total both render the union-wide ratio
            // (a percentage is not summable; per-session means are misleading).
            new CompareRow(
                Label: "Crit ISK %",
                Avg:   unionCritIskFrac.ToString("P1", CultureInfo.InvariantCulture),
                Total: unionCritIskFrac.ToString("P1", CultureInfo.InvariantCulture)),
        };

        RateRows = new[]
        {
            new CompareRow(
                Label: "m³/sec",
                Avg:   rateM3PerSec.ToString("N2", CultureInfo.InvariantCulture),
                Total: null),
            new CompareRow(
                Label: "ISK/h",
                Avg:   rateIskPerHr.ToString("N0", CultureInfo.InvariantCulture),
                Total: null),
        };

        // Prominent grand totals shown above the Cumulative table.
        TotalTimeDisplay = ReportFormatting.FormatActiveDuration(totalTime);
        TotalIskDisplay  = totalIsk.ToString("N0", CultureInfo.InvariantCulture);

        // Aggregate each session's refined output by material across the
        // selection (quantity + ISK summed), sorted by total quantity descending.
        // The sum+sort math is shared with the lifetime rollup via the single
        // RefinedOutputAggregator — this VM only maps the result into its own
        // CompareRefinedRow shape.
        RefinedRows = RefinedOutputAggregator
            .SumAcross(perSessionSnapshots.Select(s => s.RefinedOutput))
            .Select(m => new CompareRefinedRow(m.Name, m.Quantity, m.Isk))
            .ToArray();

        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
