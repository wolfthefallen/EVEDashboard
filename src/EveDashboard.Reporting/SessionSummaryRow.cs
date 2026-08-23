namespace EveDashboard.Reporting;

/// <summary>
/// One row in the Reports-window sidebar — a saved session's metadata for
/// browsing / sorting / picking.
/// </summary>
/// <param name="SessionId">Session id (sha256 hex PK in the session SQLite <c>session</c> table).</param>
/// <param name="Name">Template-expanded session name as stored in <c>session.name</c>.</param>
/// <param name="StartTime">Wall-clock instant the session started — earliest <c>start_time</c> on a non-pause <c>session_time</c> row.</param>
/// <param name="ActiveDuration">Total active wall-clock duration (sum of non-pause <c>session_time</c> intervals); excludes pause windows.</param>
/// <param name="PilotCount">Count of distinct pilots associated with the session via <c>session_pilot</c>.</param>
/// <param name="TotalIskMined">Estimated session-wide ISK valuation, computed by the <c>IIskCalculator</c> pipeline. Returns <c>0</c> if SDE/market data is unavailable.</param>
/// <param name="Archived">True when <c>session.archived = 1</c>: the session is hidden from every listing by default and never counted in lifetime totals. Only present in a result set when the caller asked for archived sessions.</param>
/// <param name="MergedInto">The <c>session_id</c> of the combined session this (archived) session was folded into, or <c>null</c>. Informational — used for troubleshooting, never dereferenced.</param>
public sealed record SessionSummaryRow(
    string         SessionId,
    string         Name,
    DateTimeOffset StartTime,
    TimeSpan       ActiveDuration,
    int            PilotCount,
    double         TotalIskMined,
    bool           Archived = false,
    string?        MergedInto = null);
