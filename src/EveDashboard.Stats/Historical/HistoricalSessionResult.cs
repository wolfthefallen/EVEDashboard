namespace EveDashboard.Stats.Historical;

/// <summary>
/// What the historical session builder wrote, for the UI summary and the log.
/// </summary>
/// <param name="SessionId">The new session's content-addressed id.</param>
/// <param name="EffectiveStart">The session's start (the requested window widened to cover every merged source).</param>
/// <param name="EffectiveStop">The session's stop (same widening rule).</param>
/// <param name="CopiedLogRows"><c>log</c> rows copied from the merged sources.</param>
/// <param name="ParsedLogRows"><c>log</c> rows written from the gamelog parse.</param>
/// <param name="DroppedByOwnership">Parsed events dropped because a declined session or a merged source already owned that pilot's time.</param>
/// <param name="DroppedUnknownType">Parsed Tick / Crit events dropped because the SDE did not recognise the ore (the live writer applies the same rule).</param>
/// <param name="DroppedNotInRoster">Parsed events for pilots outside the final roster (defensive; normally zero).</param>
/// <param name="ActiveIntervals">Number of active <c>session_time</c> rows written.</param>
/// <param name="PauseIntervals">Number of pause <c>session_time</c> rows written.</param>
/// <param name="ArchivedSessionIds">The sources now marked <c>archived = 1</c> with <c>merged_into</c> = <paramref name="SessionId"/>.</param>
public sealed record HistoricalSessionResult(
    string SessionId,
    DateTimeOffset EffectiveStart,
    DateTimeOffset EffectiveStop,
    int CopiedLogRows,
    int ParsedLogRows,
    int DroppedByOwnership,
    int DroppedUnknownType,
    int DroppedNotInRoster,
    int ActiveIntervals,
    int PauseIntervals,
    IReadOnlyList<string> ArchivedSessionIds);
