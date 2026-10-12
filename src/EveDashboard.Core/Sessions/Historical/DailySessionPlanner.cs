namespace EveDashboard.Core.Sessions.Historical;

using System.Globalization;
using EveDashboard.Core.Domain;

/// <summary>
/// Works out, from a flat stream of gamelog events and the sessions already
/// saved, which days should become sessions and what each one contains. Pure:
/// no clock, no I/O, no store access - every decision is a function of its
/// arguments, so it can be reasoned about and tested on its own.
/// </summary>
/// <remarks>
/// <para>
/// A mining RUN is the unit of work, not the calendar day: runs come from
/// <see cref="ActivityClusters"/>, the same rule that decides when a live
/// session auto-pauses, and each run belongs to the UTC day its first event
/// falls in. A run from 22:00 to 02:00 is therefore one session on the earlier
/// day rather than two halves. A day holding several runs gets one session
/// spanning them, with the gaps written as pauses by the timeline synthesiser.
/// </para>
/// <para>
/// Because the runs of a day end before the next day's first run begins (the
/// next run's first event is more than the idle threshold after the previous
/// run's last), no two days' windows can ever claim the same instant. That is
/// what makes a batch safe to write one day at a time.
/// </para>
/// <para>
/// Duplicate prevention: a saved session may be folded into at most ONE day -
/// the day whose window contains its start - and its span is excluded from every
/// other day it touches. A session that is not folded in anywhere is excluded
/// from all of them. Without that rule, a session straddling midnight would be
/// copied into two days, and folding it into the later day would drag that
/// day's window back over the earlier day's mining.
/// </para>
/// </remarks>
public static class DailySessionPlanner
{
    /// <summary>
    /// Plan the batch.
    /// </summary>
    /// <param name="events">Every mining event read for the range, any order.</param>
    /// <param name="savedSpans">Every non-archived saved session's span; the planner does its own window filtering.</param>
    /// <param name="rolesByPilot">Each pilot's last known role; pilots absent from it default to <see cref="PilotRole.Miner"/>.</param>
    /// <param name="namesByPilot">Each pilot's display name; a pilot absent from it is named by a merged session's record of them, else by id.</param>
    /// <param name="rangeStart">Inclusive start of the range the user asked for (UTC).</param>
    /// <param name="rangeStop">Inclusive end of the range the user asked for (UTC).</param>
    /// <param name="idle">The auto-pause idle threshold - the gap that ends a run.</param>
    /// <param name="mergeSameDaySessions">When true, a saved session starting inside a day's window is folded into that day; when false, every saved session is excluded instead.</param>
    /// <returns>The sessions to build and the days deliberately skipped, both in day order.</returns>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the range is inverted or <paramref name="idle"/> is not positive.</exception>
    public static DailyPlanResult Plan(
        IReadOnlyList<MiningEvent> events,
        IReadOnlyList<SessionSpan> savedSpans,
        IReadOnlyDictionary<long, PilotRole> rolesByPilot,
        IReadOnlyDictionary<long, string> namesByPilot,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeStop,
        TimeSpan idle,
        bool mergeSameDaySessions)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(savedSpans);
        ArgumentNullException.ThrowIfNull(rolesByPilot);
        ArgumentNullException.ThrowIfNull(namesByPilot);
        if (rangeStop < rangeStart) throw new ArgumentException("rangeStop must not precede rangeStart.", nameof(rangeStop));
        if (idle <= TimeSpan.Zero) throw new ArgumentException("idle must be positive.", nameof(idle));

        var inRange = events
            .Where(e => e.EveTimestamp >= rangeStart && e.EveTimestamp <= rangeStop)
            .OrderBy(e => e.EveTimestamp)
            .ToList();

        // Candidate pilots are everyone who shows up in the logs: the roster is
        // narrowed later, but exclusions must be computed against the whole set so
        // a pilot whose entire day belongs to a saved session is dropped rather
        // than quietly rostered.
        var candidates = inRange
            .Select(e => e.CharacterId)
            .Distinct()
            .Select(id => new FleetMember(
                id,
                namesByPilot.TryGetValue(id, out var name) ? name : id.ToString(CultureInfo.InvariantCulture),
                rolesByPilot.TryGetValue(id, out var role) ? role : PilotRole.Miner))
            .ToList();

        var days = GroupRunsByDay(inRange, idle);
        var mergeDayOf = mergeSameDaySessions
            ? AssignMergeDays(savedSpans, days)
            : new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        var plans = new List<DailySessionPlan>();
        var skipped = new List<DailySessionSkip>();

        foreach (var day in days)
        {
            var dayEvents = inRange.Where(e => e.EveTimestamp >= day.Start && e.EveTimestamp <= day.Stop).ToList();

            // Two different questions, deliberately answered by different rules.
            // What to FOLD IN is a question about the day: "the sessions already
            // saved for this day", whether or not they happen to overlap the
            // untracked mining - a session that ended at 13:30 is still part of
            // the day whose other mining started at 14:00.
            var mergeIds = mergeDayOf
                .Where(kv => kv.Value == day.Day)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.Ordinal);
            var merge = savedSpans.Where(s => mergeIds.Contains(s.SessionId)).ToList();

            // What to EXCLUDE is a question about overlap and shared pilots, which
            // is OverlapAnalyzer's job - anything it reports that this day is not
            // folding in owns its pilots' time and must not be re-parsed here.
            var declined = OverlapAnalyzer.Analyze(candidates, savedSpans, day.Start, day.Stop)
                .Select(o => o.Session)
                .Where(s => !mergeIds.Contains(s.SessionId))
                .ToList();

            var exclusions = OverlapAnalyzer.ComputeExclusions(candidates, declined, day.Start, day.Stop);
            var survivors = dayEvents
                .Where(e => !exclusions.Any(x => x.PilotId == e.CharacterId && x.Covers(e.EveTimestamp)))
                .ToList();

            var oreMiners = survivors
                .Where(e => e.Kind is MiningEventKind.Tick or MiningEventKind.Crit)
                .Select(e => e.CharacterId)
                .ToHashSet();
            var pilotsWithNoOre = survivors
                .Select(e => e.CharacterId)
                .Distinct()
                .Where(id => !oreMiners.Contains(id))
                .OrderBy(id => id)
                .ToList();

            var roster = BuildRoster(candidates, oreMiners, merge, rolesByPilot);
            if (roster.Count == 0)
            {
                skipped.Add(new DailySessionSkip(day.Day, SkipReason(dayEvents, survivors, declined)));
                continue;
            }

            // The window is re-derived from the events that survived, so an
            // exclusion covering the front of a day does not leave the session
            // starting in dead air.
            var runs = ActivityClusters.FromEventTimes(
                survivors
                    .Where(e => e.Kind is MiningEventKind.Tick or MiningEventKind.Crit)
                    .Select(e => e.EveTimestamp)
                    .ToList(),
                idle);

            var windowStart = runs.Count > 0 ? runs[0].Start : day.Start;
            var windowStop = runs.Count > 0 ? runs[^1].Stop : day.Stop;
            var lastSurvivor = survivors.Count > 0 ? survivors[^1].EveTimestamp : windowStop;
            if (lastSurvivor > windowStop) windowStop = lastSurvivor;
            foreach (var span in merge)
            {
                if (span.Start < windowStart) windowStart = span.Start;
                if (span.Stop > windowStop) windowStop = span.Stop;
            }

            plans.Add(new DailySessionPlan(
                day.Day, windowStart, windowStop, roster, merge, exclusions, survivors, pilotsWithNoOre));
        }

        return new DailyPlanResult(plans, skipped);
    }

    /// <summary>
    /// Split the event stream into mining runs and group them by the UTC day each
    /// run started in, pooling every pilot: a gap in the pooled stream is the
    /// whole fleet standing down, which is what ends a run.
    /// </summary>
    private static List<DayWindow> GroupRunsByDay(IReadOnlyList<MiningEvent> inRange, TimeSpan idle)
    {
        var runs = ActivityClusters.FromEventTimes(inRange.Select(e => e.EveTimestamp).ToList(), idle);
        return runs
            .GroupBy(r => DateOnly.FromDateTime(r.Start.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DayWindow(g.Key, g.Min(r => r.Start), g.Max(r => r.Stop)))
            .ToList();
    }

    /// <summary>
    /// Decide, once for the whole batch, which single day may fold in each saved
    /// session: the day whose window was under way when the session started,
    /// failing that the day matching the calendar date of its start.
    /// </summary>
    /// <remarks>
    /// Deciding this up front is what guarantees a session cannot be copied into
    /// two days. The containment rule comes first so a session begun during a run
    /// that is already in progress belongs to that run's day, which is the same
    /// rule that keeps a run crossing midnight whole; the date fallback catches a
    /// session that started shortly before the day's first ore was mined. A
    /// session that matches neither is folded in nowhere and is excluded from
    /// every day it touches instead.
    /// </remarks>
    private static Dictionary<string, DateOnly> AssignMergeDays(
        IReadOnlyList<SessionSpan> savedSpans, List<DayWindow> days)
    {
        var assignment = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var span in savedSpans)
        {
            var containing = days.FirstOrDefault(d => span.Start >= d.Start && span.Start <= d.Stop);
            var owner = containing ?? days.FirstOrDefault(d => d.Day == DateOnly.FromDateTime(span.Start.UtcDateTime));
            if (owner is not null) assignment[span.SessionId] = owner.Day;
        }
        return assignment;
    }

    /// <summary>One day's run envelope: the day it is named for and the span its runs cover.</summary>
    private sealed record DayWindow(DateOnly Day, DateTimeOffset Start, DateTimeOffset Stop);

    /// <summary>
    /// The pilots who mined ore, plus every pilot of a session being folded in -
    /// a merged session's own record of a pilot's role wins, because that is the
    /// role its numbers were recorded under.
    /// </summary>
    private static List<FleetMember> BuildRoster(
        IReadOnlyList<FleetMember> candidates,
        HashSet<long> oreMiners,
        IReadOnlyList<SessionSpan> merge,
        IReadOnlyDictionary<long, PilotRole> rolesByPilot)
    {
        var roster = new List<FleetMember>();
        var seen = new HashSet<long>();

        foreach (var span in merge)
        {
            foreach (var pilot in span.Pilots)
            {
                if (seen.Add(pilot.PilotId))
                {
                    roster.Add(new FleetMember(pilot.PilotId, pilot.Name, pilot.Role));
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (!oreMiners.Contains(candidate.PilotId)) continue;
            if (!seen.Add(candidate.PilotId)) continue;
            roster.Add(candidate with
            {
                Role = rolesByPilot.TryGetValue(candidate.PilotId, out var role) ? role : candidate.Role,
            });
        }

        return roster.OrderBy(m => m.PilotId).ToList();
    }

    /// <summary>Why a day produced nothing, phrased for the user.</summary>
    /// <remarks>
    /// The two causes look alike from the outside but are not: a day can hold no
    /// ore at all, or hold ore that a saved session already owns. Telling them
    /// apart is the difference between "nothing to build" and "built elsewhere".
    /// </remarks>
    private static string SkipReason(
        IReadOnlyList<MiningEvent> dayEvents,
        IReadOnlyList<MiningEvent> survivors,
        IReadOnlyList<SessionSpan> declined)
    {
        static bool HasOre(IReadOnlyList<MiningEvent> events) =>
            events.Any(e => e.Kind is MiningEventKind.Tick or MiningEventKind.Crit);

        if (dayEvents.Count == 0) return "no mining in the logs";
        if (!HasOre(dayEvents)) return "the logs hold activity but no ore was mined";
        if (!HasOre(survivors) && declined.Count > 0)
        {
            var names = declined.Select(d => "'" + d.Name + "'").Distinct(StringComparer.Ordinal);
            return "every pilot's mining that day already belongs to " + string.Join(", ", names);
        }

        return "nothing left to build for this day";
    }
}
