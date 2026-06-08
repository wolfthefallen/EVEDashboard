namespace EveDashboard.Reporting;

/// <summary>
/// Closed-form summary of a session's <c>session_time</c> rows: when it
/// started, how long the active intervals total to, and the list of completed
/// pause windows. Returned by <see cref="Queries.IReportingQueries.GetSessionTimelineAsync"/>
/// to drive the Excel-export Summary tab's active-vs-paused duration figures
/// (plan 29 <c>ExcelSessionExporter</c>).
/// </summary>
/// <param name="SessionStart">Wall-clock UTC instant of the earliest active interval (<c>is_pause = 0</c>) for the session.</param>
/// <param name="ActiveDuration">Sum of <c>(stop_time - start_time)</c> over every active interval. Pauses are excluded.</param>
/// <param name="PauseWindows">Every <c>is_pause = 1</c> row with both timestamps populated, ordered by <see cref="PauseWindowRow.Start"/> ascending. Open pauses (<c>stop_time IS NULL</c>) are dropped — saved sessions never have them since Stop closes any open interval.</param>
public sealed record SessionTimeline(
    DateTimeOffset SessionStart,
    TimeSpan ActiveDuration,
    IReadOnlyList<PauseWindowRow> PauseWindows);

/// <summary>
/// One closed pause interval. Both timestamps are non-null UTC values.
/// </summary>
/// <param name="Start">When the pause began.</param>
/// <param name="Stop">When the pause ended.</param>
public sealed record PauseWindowRow(
    DateTimeOffset Start,
    DateTimeOffset Stop);
