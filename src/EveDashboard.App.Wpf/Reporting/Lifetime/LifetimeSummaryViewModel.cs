namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.Globalization;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Reporting;
using EveDashboard.Stats.Queries;

/// <summary>
/// Widget VM for the all-time rollup shown in the Lifetime Totals window.
/// Renders total active time, total ISK, and a per-material "Total Refined
/// Output" table from a precomputed <see cref="LifetimeSummary"/> (returned by
/// <c>IReportingQueries.GetAllSessionSummariesAsync</c> alongside the per-session
/// rows). Mirrors <c>CompareSessionViewModel</c>: all values pre-rendered in the
/// ctor. Inherits <see cref="TextSizeConfigurableWidgetViewModel"/> for the
/// "Text size" cog + column-width persistence (widget-type key <c>"lifetime"</c>).
/// </summary>
public sealed class LifetimeSummaryViewModel : TextSizeConfigurableWidgetViewModel
{
    /// <summary>Sizeable elements + default emphasis (Title + Refined label Header0, the Time/ISK rows + table plain).</summary>
    private static readonly IReadOnlyList<(string Key, string Label, StatEmphasis Default)> Elements = new[]
    {
        ("Title", "Lifetime Totals", StatEmphasis.Header0),
        ("TimeTracked", "Time Tracked", StatEmphasis.None),
        ("TotalIsk", "Total ISK", StatEmphasis.None),
        ("RefinedLabel", "Total Refined Output", StatEmphasis.Header0),
        ("Table", "Table", StatEmphasis.None),
    };

    /// <summary>Total active (un-paused) time across all saved sessions, pre-rendered <c>HH:mm:ss</c> (hours unbounded).</summary>
    public string TimeTrackedDisplay { get; }

    /// <summary>Total ISK across all saved sessions, pre-rendered <c>N0</c> (invariant culture).</summary>
    public string TotalIskDisplay { get; }

    /// <summary>Per-material refined output summed across all sessions, already sorted by quantity descending (the table's row source).</summary>
    public IReadOnlyList<RefinedMaterialAmount> RefinedRows { get; }

    /// <summary>Construct the lifetime-summary VM from a precomputed rollup.</summary>
    /// <param name="summary">All-time aggregate from <c>IReportingQueries.GetAllSessionSummariesAsync</c>.</param>
    /// <param name="settings">Settings repository (text size + column widths).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="summary"/> or <paramref name="settings"/> is null.</exception>
    public LifetimeSummaryViewModel(LifetimeSummary summary, ISettingsRepository settings)
        : base(settings, "lifetime", Elements, log: null)
    {
        ArgumentNullException.ThrowIfNull(summary);

        TimeTrackedDisplay = ReportFormatting.FormatActiveDuration(summary.TotalTime);
        TotalIskDisplay    = summary.TotalIsk.ToString("N0", CultureInfo.InvariantCulture);
        RefinedRows        = summary.Refined;

        // The chrome's "Last updated" line reflects when this rollup was computed;
        // the Lifetime Totals window rebuilds it on every Show, so this is the last open.
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
