namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// Turns a bag of event timestamps into the stretches of continuous activity a
/// live session would have recorded: a stretch runs from its first event until
/// the idle threshold after its last, and a gap longer than that threshold -
/// the point where the live auto-pause fires - starts a new one.
/// </summary>
/// <remarks>
/// This is the shared definition of "one trip out". The timeline synthesiser
/// uses it to decide when a session was active, and the historical day planner
/// uses it to decide where one mining run ends and the next begins, so both
/// agree by construction rather than by two copies of the same arithmetic.
/// </remarks>
public static class ActivityClusters
{
    /// <summary>
    /// Group <paramref name="eventTimes"/> into activity stretches.
    /// </summary>
    /// <param name="eventTimes">Event timestamps in any order; duplicates are harmless.</param>
    /// <param name="idle">The auto-pause idle threshold. A gap STRICTLY longer than this splits a stretch; a gap exactly equal to it does not.</param>
    /// <param name="clipStop">Optional ceiling for the final stretch's end, for a caller that must not spill past a known boundary. Earlier stretches never need it: the next event is more than <paramref name="idle"/> later, so their end always precedes it.</param>
    /// <returns>The stretches in chronological order, each ending <paramref name="idle"/> after its last event. An empty input yields an empty list.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="eventTimes"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="idle"/> is not positive.</exception>
    public static IReadOnlyList<TimeInterval> FromEventTimes(
        IReadOnlyList<DateTimeOffset> eventTimes,
        TimeSpan idle,
        DateTimeOffset? clipStop = null)
    {
        ArgumentNullException.ThrowIfNull(eventTimes);
        if (idle <= TimeSpan.Zero) throw new ArgumentException("idle must be positive.", nameof(idle));
        if (eventTimes.Count == 0) return Array.Empty<TimeInterval>();

        var events = eventTimes.OrderBy(t => t).ToList();
        var result = new List<TimeInterval>();
        var segmentStart = events[0];
        for (var i = 0; i < events.Count; i++)
        {
            if (i == events.Count - 1)
            {
                var end = events[i] + idle;
                if (clipStop is { } ceiling && end > ceiling) end = ceiling;
                result.Add(new TimeInterval(segmentStart, end));
            }
            else if (events[i + 1] - events[i] > idle)
            {
                result.Add(new TimeInterval(segmentStart, events[i] + idle));
                segmentStart = events[i + 1];
            }
        }

        return result;
    }
}
