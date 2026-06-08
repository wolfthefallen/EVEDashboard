namespace EveDashboard.Reporting;

/// <summary>
/// One row in the Reports-window sidebar — a saved session's metadata for
/// browsing / sorting / picking. Plan 25.
/// </summary>
/// <param name="SessionId">Session id (sha256 hex PK in the session SQLite <c>session</c> table).</param>
/// <param name="Name">Template-expanded session name as stored in <c>session.name</c>.</param>
/// <param name="StartTime">Wall-clock instant the session started — earliest <c>start_time</c> on a non-pause <c>session_time</c> row.</param>
/// <param name="ActiveDuration">Total active wall-clock duration (sum of non-pause <c>session_time</c> intervals); excludes pause windows.</param>
/// <param name="PilotCount">Count of distinct pilots associated with the session via <c>session_pilot</c>.</param>
/// <param name="TotalIskMined">Estimated session-wide ISK valuation, computed from the plan-18 ISK pipeline. Returns <c>0</c> if SDE/market data is unavailable.</param>
public sealed record SessionSummaryRow(
    string         SessionId,
    string         Name,
    DateTimeOffset StartTime,
    TimeSpan       ActiveDuration,
    int            PilotCount,
    double         TotalIskMined);
