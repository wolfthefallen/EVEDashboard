namespace EveDashboard.App.Wpf.Reporting.Compare;

using System.Globalization;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Stats.Queries;

/// <summary>
/// Plan 36: the single compare-mode widget VM that replaces the prior trio
/// (CompareSessionStats + CompareSessionOreBreakdown + per-pilot stack).
/// Holds a stacked list of session names and two pre-built row collections
/// (Cumulative + Rates) ready to bind to two static-column DataGrids.
/// </summary>
/// <remarks>
/// <para>
/// Per the 2026-05-27 alignment round:
/// </para>
/// <list type="bullet">
///   <item><description>5.1 + 5.2: Cumulative columns are <c>Avg | Total</c>; Rates column is <c>Avg</c> only.</description></item>
///   <item><description>5.2 = C (corrected rate math): rate Avg is computed as <c>sum(m³ or ISK) / sum(active_seconds)</c> over the union of selected sessions, NOT the mean-of-per-session-rates.</description></item>
///   <item><description>6.1: per-ore tables dropped.</description></item>
///   <item><description>6.2 = C: a single "Crit ISK %" row appears in Cumulative; Crit m³ / Crit count / Crit chance dropped from compare.</description></item>
///   <item><description>6.3 = A: stacked session-name subheader, one name per line, no dates.</description></item>
/// </list>
/// <para>
/// All cell values are pre-rendered into strings in the ctor (single pass, no
/// XAML value converters). Implements <see cref="IDisposable"/> with an empty
/// body so <c>BreakdownContentViewModel.DisposePriorWidgets</c> can keep its
/// existing shape; future row-hide / settings subscriptions can plug in here
/// without touching callers.
/// </para>
/// </remarks>
public sealed class CompareSessionViewModel : WidgetBaseViewModel, IDisposable
{
    private bool _disposed;

    /// <summary>Selected session names in click order, surfaced as the stacked subheader.</summary>
    public IReadOnlyList<string> SessionNames { get; }

    /// <summary>4 rows (ISK, Time, m³, Crit ISK %). DataGrid columns: <c>Stat | Avg | Total</c>.</summary>
    public IReadOnlyList<CompareRow> CumulativeRows { get; }

    /// <summary>2 rows (m³/sec, ISK/h). DataGrid columns: <c>Stat | Avg</c>. Both rows have <c>Total = null</c>.</summary>
    public IReadOnlyList<CompareRow> RateRows { get; }

    /// <summary>Construct the compare-session VM from per-session snapshots + display names (parallel lists in click order).</summary>
    /// <param name="perSessionSnapshots">Per-session <see cref="SessionStats"/> snapshots. Length must equal <paramref name="sessionNames"/>.Count and be >= 1.</param>
    /// <param name="sessionNames">Per-session display names.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the lengths differ or both are empty.</exception>
    public CompareSessionViewModel(
        IReadOnlyList<SessionStats> perSessionSnapshots,
        IReadOnlyList<string>       sessionNames)
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

        // Plan 36 5.2 = C: rate Avg uses union-wide totals over union-wide
        // active seconds (corrected math).
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
                Avg:   FormatDuration(TimeSpan.FromSeconds(totalSeconds / n)),
                Total: FormatDuration(totalTime)),
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

        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Format an active-duration TimeSpan as <c>HH:mm:ss</c> with hours unbounded (matches SavedSessionStatsViewModel.FormatDuration).</summary>
    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (long)duration.TotalHours;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}",
            totalHours, duration.Minutes, duration.Seconds);
    }

    /// <summary>Dispose: idempotent no-op for now; reserved for future settings subscriptions.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
