namespace EveDashboard.Core.Domain;

/// <summary>
/// The top-level mining-session entity. Sessions are immutable; the session
/// manager creates new instances via <c>with</c>-expressions when state changes
/// (a pause begins, a pause ends, a character joins, the session ends, etc.).
/// </summary>
/// <param name="Id">Unique session identifier.</param>
/// <param name="Name">
/// Already-template-expanded session name. Template placeholders
/// (<c>{{$DATE}}</c>, <c>{{$TIME}}</c>, <c>{{$DATETIME}}</c>, <c>{{$CHARS}}</c>)
/// are resolved once at session creation; this field carries the resolved
/// fixed string.
/// </param>
/// <param name="Notes">Already-template-expanded free-form session notes.</param>
/// <param name="Characters">Characters mining within this session.</param>
/// <param name="StartTime">When the session started.</param>
/// <param name="EndTime">When the session ended, or <c>null</c> if still active.</param>
/// <param name="PauseWindows">
/// All completed pause intervals recorded so far. Open (in-flight) pauses are
/// tracked separately on <paramref name="CurrentPauseStart"/>.
/// </param>
/// <param name="CurrentPauseStart">
/// Start time of an in-flight pause, or <c>null</c> if the session is not
/// currently paused. When the user resumes, the session manager creates a new
/// <see cref="PauseWindow"/> from this value and the resume time, appends it
/// to <paramref name="PauseWindows"/>, and clears this field back to <c>null</c>.
/// </param>
public sealed record Session(
    Guid Id,
    string Name,
    string Notes,
    IReadOnlyList<Character> Characters,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    IReadOnlyList<PauseWindow> PauseWindows,
    DateTimeOffset? CurrentPauseStart)
{
    /// <summary>
    /// Compute the active duration of this session as of <paramref name="asOf"/>.
    /// Wall-clock duration minus the sum of all completed pause windows that
    /// overlap the active range, minus the open pause (if any) measured from
    /// <see cref="CurrentPauseStart"/> to <paramref name="asOf"/>.
    /// </summary>
    /// <param name="asOf">The "now" reference time. Pass UTC-aligned values.</param>
    /// <returns>Active duration. Never negative.</returns>
    /// <remarks>
    /// <see cref="EveDashboard.Core.Domain"/> is I/O-free — including no implicit
    /// clock dependency. Callers (the stats engine, the UI) supply
    /// <paramref name="asOf"/> from <c>DateTimeOffset.UtcNow</c> at their layer.
    /// </remarks>
    public TimeSpan ActiveDuration(DateTimeOffset asOf)
    {
        var endRef = EndTime is { } actualEnd && actualEnd < asOf ? actualEnd : asOf;
        if (endRef <= StartTime) return TimeSpan.Zero;

        var paused = TimeSpan.Zero;

        // Closed pauses.
        foreach (var p in PauseWindows)
        {
            if (p.Start >= endRef) continue;
            var effectiveEnd = p.End < endRef ? p.End : endRef;
            if (effectiveEnd > p.Start) paused += effectiveEnd - p.Start;
        }

        // Open (in-flight) pause, if any.
        if (CurrentPauseStart is { } openStart && openStart < endRef)
        {
            paused += endRef - openStart;
        }

        var active = (endRef - StartTime) - paused;
        return active < TimeSpan.Zero ? TimeSpan.Zero : active;
    }
}
