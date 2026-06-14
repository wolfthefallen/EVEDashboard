namespace EveDashboard.Reporting;

using EveDashboard.Stats.Queries;

/// <summary>
/// All-time rollup across every saved session, shown in the Reports window's
/// right pane when no session is selected (plan 46). Computed once during the
/// sidebar's existing per-session pass (<see cref="Queries.IReportingQueries.GetAllSessionSummariesAsync"/>)
/// by summing the per-session snapshots that pass already produces — never a
/// second pass and never cross-session pricing.
/// </summary>
/// <param name="TotalTime">Sum of every session's ACTIVE (un-paused) duration —
///     each session's <c>SessionStats.TotalTimeMined</c> added together.</param>
/// <param name="TotalIsk">Sum of every session's <c>TotalIskMined</c>. Each
///     session is priced independently from its own captured rows, then summed;
///     this reconciles to the sum of the sidebar's per-session ISK column.</param>
/// <param name="Refined">Per-material refined output (quantity + ISK) summed
///     across all sessions, sorted by quantity descending — the same
///     <see cref="RefinedOutputAggregator.SumAcross"/> math the compare view uses.</param>
public sealed record LifetimeSummary(
    TimeSpan                                TotalTime,
    double                                  TotalIsk,
    IReadOnlyList<RefinedMaterialAmount>    Refined)
{
    /// <summary>
    /// The empty rollup (no saved sessions): zero time, zero ISK, no refined
    /// rows. Used for an empty database and as the sidebar's reset value on a
    /// failed load.
    /// </summary>
    public static LifetimeSummary Empty { get; } =
        new(TimeSpan.Zero, 0.0, Array.Empty<RefinedMaterialAmount>());
}
