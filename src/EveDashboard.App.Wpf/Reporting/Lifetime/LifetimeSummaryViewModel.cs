namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.Globalization;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Reporting;
using EveDashboard.Stats.Queries;

/// <summary>
/// Plan 46: the empty-selection ("no session checked") widget VM for the Reports
/// window's right pane. Renders the all-time rollup — total active time, total
/// ISK, and a per-material "Total Refined Output" table — from a precomputed
/// <see cref="LifetimeSummary"/> (built once in the sidebar's per-session pass).
/// Mirrors <c>CompareSessionViewModel</c>: a <see cref="WidgetBaseViewModel"/>
/// with all values pre-rendered in the ctor.
/// </summary>
public sealed class LifetimeSummaryViewModel : WidgetBaseViewModel
{
    /// <summary>Total active (un-paused) time across all saved sessions, pre-rendered <c>HH:mm:ss</c> (hours unbounded).</summary>
    public string TimeTrackedDisplay { get; }

    /// <summary>Total ISK across all saved sessions, pre-rendered <c>N0</c> (invariant culture).</summary>
    public string TotalIskDisplay { get; }

    /// <summary>Per-material refined output summed across all sessions, already sorted by quantity descending (the table's row source).</summary>
    public IReadOnlyList<RefinedMaterialAmount> RefinedRows { get; }

    /// <summary>Construct the lifetime-summary VM from a precomputed rollup.</summary>
    /// <param name="summary">All-time aggregate produced by the sidebar's per-session pass.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="summary"/> is null.</exception>
    public LifetimeSummaryViewModel(LifetimeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        TimeTrackedDisplay = ReportFormatting.FormatActiveDuration(summary.TotalTime);
        TotalIskDisplay    = summary.TotalIsk.ToString("N0", CultureInfo.InvariantCulture);
        RefinedRows        = summary.Refined;

        // The chrome's "Last updated" line reflects when this rollup was computed
        // (Reports open / import / delete — see SessionsSidebarViewModel.LoadAsync).
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
