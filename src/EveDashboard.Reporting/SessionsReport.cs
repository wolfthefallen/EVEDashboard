namespace EveDashboard.Reporting;

/// <summary>
/// Result of the Reports sidebar's single per-session pass: the
/// per-session rows AND the all-time <see cref="LifetimeSummary"/> aggregate,
/// both built in one loop over the saved sessions so the lifetime rollup costs
/// no extra per-session work.
/// </summary>
/// <param name="Sessions">One row per saved session, ordered newest-first by
///     start time (sha256 session ids are not chronological, so id order is
///     meaningless).</param>
/// <param name="Lifetime">All-time rollup across the same sessions; equals
///     <see cref="LifetimeSummary.Empty"/> when the database has no sessions.</param>
public sealed record SessionsReport(
    IReadOnlyList<SessionSummaryRow> Sessions,
    LifetimeSummary                  Lifetime);
