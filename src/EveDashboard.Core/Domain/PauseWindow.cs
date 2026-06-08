namespace EveDashboard.Core.Domain;

/// <summary>
/// A closed time interval representing a single completed user-initiated pause
/// within a session. End time must be at-or-after start time.
/// </summary>
/// <remarks>
/// In-flight pauses (paused but not yet resumed) are not modelled by this type.
/// They live as <c>Session.CurrentPauseStart</c>. When the user resumes, the
/// session manager constructs a <see cref="PauseWindow"/> from the recorded
/// start and the resume time, and appends it to the session's pause-window list.
/// </remarks>
public readonly record struct PauseWindow
{
    /// <summary>Pause start time.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Pause end time. Always greater than or equal to <see cref="Start"/>.</summary>
    public DateTimeOffset End { get; }

    /// <summary>Wall-clock duration of the pause.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Construct a <see cref="PauseWindow"/>. Throws if <paramref name="end"/>
    /// is earlier than <paramref name="start"/>.
    /// </summary>
    /// <param name="start">When the pause began.</param>
    /// <param name="end">When the pause ended.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="end"/> precedes <paramref name="start"/>.</exception>
    public PauseWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end < start)
        {
            throw new ArgumentException(
                $"PauseWindow end ({end:O}) precedes start ({start:O}).",
                nameof(end));
        }
        Start = start;
        End = end;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="when"/> falls inside this
    /// pause window (inclusive on both ends).
    /// </summary>
    /// <param name="when">Timestamp to test.</param>
    public bool Contains(DateTimeOffset when) => when >= Start && when <= End;
}
