namespace EveDashboard.Stats.Historical;

/// <summary>A closed time interval (UTC).</summary>
/// <param name="Start">Inclusive start.</param>
/// <param name="Stop">Inclusive end; never earlier than <paramref name="Start"/>.</param>
public sealed record TimeInterval(DateTimeOffset Start, DateTimeOffset Stop);

/// <summary>One <c>session_time</c> row to write.</summary>
/// <param name="Start">Row <c>start_time</c>.</param>
/// <param name="Stop">Row <c>stop_time</c>.</param>
/// <param name="IsPause"><c>true</c> for a pause row (<c>is_pause = 1</c>), <c>false</c> for an active row.</param>
public sealed record SessionTimeRowSpec(DateTimeOffset Start, DateTimeOffset Stop, bool IsPause);

/// <summary>
/// Pure time accounting for a session built after the fact. Produces the
/// contiguous active / pause timeline a live session would have recorded, from
/// the merged sources' own rows plus the gamelog events in the untracked gaps.
/// </summary>
/// <remarks>
/// <para>
/// Merged sources are authoritative for their own spans: their active intervals
/// are taken as-is (unioned, so two sources that ran concurrently cannot double
/// count wall-clock time). Every part of the window NOT covered by a source is
/// treated the way the live session controller treats time: paused until the
/// first qualifying (Tick / Crit) event, active from there, a pause synthesised
/// after any gap longer than the idle threshold - starting <c>idle</c> after the
/// last event, exactly when the live auto-pause would have fired - and paused
/// again <c>idle</c> after the last event of the gap.
/// </para>
/// <para>
/// Only events inside uncovered gaps drive the idle rule; an event inside a
/// source's span is already accounted for by that source's rows.
/// </para>
/// </remarks>
public static class SessionTimelineSynthesizer
{
    /// <summary>
    /// Build the timeline rows for <paramref name="windowStart"/>..<paramref name="windowStop"/>.
    /// </summary>
    /// <param name="windowStart">Effective session start (UTC).</param>
    /// <param name="windowStop">Effective session stop (UTC); must not precede the start.</param>
    /// <param name="coveredSpans">Full wall-clock spans of the merged sources (pauses included).</param>
    /// <param name="coveredActive">The merged sources' ACTIVE (<c>is_pause = 0</c>) intervals.</param>
    /// <param name="qualifyingEventTimes">Timestamps of the Tick / Crit events kept from the gamelog parse (any order).</param>
    /// <param name="idle">The auto-pause idle threshold.</param>
    /// <returns>Contiguous, non-overlapping rows covering the whole window, sorted by start, zero-length rows omitted. An empty window yields no rows.</returns>
    /// <exception cref="ArgumentNullException">Thrown if any list is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the window is inverted or <paramref name="idle"/> is not positive.</exception>
    public static IReadOnlyList<SessionTimeRowSpec> Build(
        DateTimeOffset windowStart,
        DateTimeOffset windowStop,
        IReadOnlyList<TimeInterval> coveredSpans,
        IReadOnlyList<TimeInterval> coveredActive,
        IReadOnlyList<DateTimeOffset> qualifyingEventTimes,
        TimeSpan idle)
    {
        ArgumentNullException.ThrowIfNull(coveredSpans);
        ArgumentNullException.ThrowIfNull(coveredActive);
        ArgumentNullException.ThrowIfNull(qualifyingEventTimes);
        if (windowStop < windowStart) throw new ArgumentException("windowStop must not precede windowStart.", nameof(windowStop));
        if (idle <= TimeSpan.Zero) throw new ArgumentException("idle must be positive.", nameof(idle));

        var covered = Merge(coveredSpans.Select(i => Clip(i, windowStart, windowStop)).Where(i => i is not null)!);
        var active = new List<TimeInterval>(coveredActive.Select(i => Clip(i, windowStart, windowStop)).Where(i => i is not null)!);

        var events = qualifyingEventTimes.Where(t => t >= windowStart && t <= windowStop).OrderBy(t => t).ToList();
        foreach (var gap in Complement(windowStart, windowStop, covered))
        {
            active.AddRange(IdleRule(gap, events.Where(t => t >= gap.Start && t <= gap.Stop).ToList(), idle));
        }

        var activeMerged = Merge(active);
        var rows = new List<SessionTimeRowSpec>();
        foreach (var a in activeMerged) rows.Add(new SessionTimeRowSpec(a.Start, a.Stop, IsPause: false));
        foreach (var p in Complement(windowStart, windowStop, activeMerged)) rows.Add(new SessionTimeRowSpec(p.Start, p.Stop, IsPause: true));
        return rows.Where(r => r.Stop > r.Start).OrderBy(r => r.Start).ToList();
    }

    /// <summary>Active segments inside one uncovered gap, per the live idle rule.</summary>
    private static IEnumerable<TimeInterval> IdleRule(TimeInterval gap, IReadOnlyList<DateTimeOffset> events, TimeSpan idle)
    {
        if (events.Count == 0) yield break;

        var segmentStart = events[0];
        for (var i = 0; i < events.Count; i++)
        {
            var last = i == events.Count - 1;
            if (last)
            {
                var end = events[i] + idle;
                yield return new TimeInterval(segmentStart, end < gap.Stop ? end : gap.Stop);
            }
            else if (events[i + 1] - events[i] > idle)
            {
                yield return new TimeInterval(segmentStart, events[i] + idle);
                segmentStart = events[i + 1];
            }
        }
    }

    private static TimeInterval? Clip(TimeInterval i, DateTimeOffset lo, DateTimeOffset hi)
    {
        var start = i.Start > lo ? i.Start : lo;
        var stop = i.Stop < hi ? i.Stop : hi;
        return start <= stop ? new TimeInterval(start, stop) : null;
    }

    /// <summary>Sort and coalesce touching / overlapping intervals.</summary>
    private static List<TimeInterval> Merge(IEnumerable<TimeInterval> intervals)
    {
        var result = new List<TimeInterval>();
        foreach (var i in intervals.OrderBy(x => x.Start))
        {
            if (result.Count > 0 && i.Start <= result[^1].Stop)
            {
                if (i.Stop > result[^1].Stop) result[^1] = result[^1] with { Stop = i.Stop };
            }
            else
            {
                result.Add(i);
            }
        }
        return result;
    }

    /// <summary>The parts of <paramref name="lo"/>..<paramref name="hi"/> not covered by the (merged, sorted) intervals.</summary>
    private static IEnumerable<TimeInterval> Complement(DateTimeOffset lo, DateTimeOffset hi, IReadOnlyList<TimeInterval> merged)
    {
        var cursor = lo;
        foreach (var i in merged)
        {
            if (i.Start > cursor) yield return new TimeInterval(cursor, i.Start);
            if (i.Stop > cursor) cursor = i.Stop;
        }
        if (hi > cursor) yield return new TimeInterval(cursor, hi);
    }
}
