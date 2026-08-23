namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// Pure rules for building a session after the fact when saved sessions already
/// cover part of the window. A pilot may belong to exactly one session at any
/// instant, so every overlapping session is either COMBINED into the new one or
/// its pilots' time is EXCLUDED from the log parse.
/// </summary>
public static class OverlapAnalyzer
{
    /// <summary>
    /// Find the saved sessions that overlap the window and share at least one
    /// pilot with the roster, classified as full or partial fleet matches.
    /// </summary>
    /// <param name="roster">The fleet roster the user picked for the new session.</param>
    /// <param name="sessions">Candidate saved sessions (already excluding archived ones).</param>
    /// <param name="windowStart">Inclusive window start (UTC).</param>
    /// <param name="windowStop">Inclusive window end (UTC).</param>
    /// <returns>Overlaps ordered by session start. Sessions sharing no pilot with the roster are irrelevant and omitted: they cannot double-count anything.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="roster"/> or <paramref name="sessions"/> is null.</exception>
    /// <remarks>
    /// Overlap is inclusive at both ends: a session that stopped exactly at the
    /// window start still counts, because a mining tick stamped at that second
    /// would otherwise be claimable by both.
    /// </remarks>
    public static IReadOnlyList<SessionOverlap> Analyze(
        IReadOnlyList<FleetMember> roster,
        IReadOnlyList<SessionSpan> sessions,
        DateTimeOffset windowStart,
        DateTimeOffset windowStop)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(sessions);

        var rosterIds = roster.Select(m => m.PilotId).ToHashSet();
        var result = new List<SessionOverlap>();
        foreach (var session in sessions.OrderBy(s => s.Start))
        {
            if (session.Start > windowStop || session.Stop < windowStart) continue;

            var shared = session.Pilots.Where(p => rosterIds.Contains(p.PilotId)).Select(p => p.PilotId).Distinct().OrderBy(id => id).ToList();
            if (shared.Count == 0) continue;

            var extras = session.Pilots.Where(p => !rosterIds.Contains(p.PilotId)).ToList();
            result.Add(new SessionOverlap(
                session,
                extras.Count == 0 ? FleetMatchKind.Full : FleetMatchKind.Partial,
                shared,
                extras));
        }
        return result;
    }

    /// <summary>
    /// Build the union fleet roster needed to combine partial matches: the picked
    /// roster (its roles win for shared pilots) plus every extra pilot of the given
    /// overlaps, carrying the role the session recorded for them.
    /// </summary>
    /// <param name="roster">The fleet roster the user picked.</param>
    /// <param name="overlaps">The overlaps the user agreed to combine (full matches contribute nothing).</param>
    /// <returns>The roster followed by the extra pilots in first-seen order, no duplicates.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public static IReadOnlyList<FleetMember> ProposeUnionRoster(
        IReadOnlyList<FleetMember> roster,
        IEnumerable<SessionOverlap> overlaps)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(overlaps);

        var union = new List<FleetMember>(roster);
        var seen = roster.Select(m => m.PilotId).ToHashSet();
        foreach (var overlap in overlaps)
        {
            foreach (var extra in overlap.ExtraPilots)
            {
                if (!seen.Add(extra.PilotId)) continue;
                union.Add(new FleetMember(extra.PilotId, extra.Name, extra.Role));
            }
        }
        return union;
    }

    /// <summary>
    /// Turn the sessions the user declined to combine into per-pilot exclusion
    /// windows, clipped to the historical window.
    /// </summary>
    /// <param name="roster">The (final) roster of the new session; only its pilots can be excluded.</param>
    /// <param name="declined">Sessions the user chose NOT to combine.</param>
    /// <param name="windowStart">Inclusive window start (UTC).</param>
    /// <param name="windowStop">Inclusive window end (UTC).</param>
    /// <returns>Exclusions ordered by pilot then start; a declined session outside the window yields none.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="roster"/> or <paramref name="declined"/> is null.</exception>
    /// <remarks>
    /// The whole session span is excluded, pauses included: events the user
    /// paused out of that session were dropped on purpose and must not return
    /// through the log parse.
    /// </remarks>
    public static IReadOnlyList<PilotExclusion> ComputeExclusions(
        IReadOnlyList<FleetMember> roster,
        IEnumerable<SessionSpan> declined,
        DateTimeOffset windowStart,
        DateTimeOffset windowStop)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(declined);

        var rosterIds = roster.Select(m => m.PilotId).ToHashSet();
        var result = new List<PilotExclusion>();
        foreach (var session in declined)
        {
            var start = session.Start > windowStart ? session.Start : windowStart;
            var stop = session.Stop < windowStop ? session.Stop : windowStop;
            if (start > stop) continue;

            foreach (var pilotId in session.Pilots.Select(p => p.PilotId).Distinct())
            {
                if (!rosterIds.Contains(pilotId)) continue;
                result.Add(new PilotExclusion(pilotId, start, stop, session.SessionId));
            }
        }
        return result.OrderBy(e => e.PilotId).ThenBy(e => e.Start).ToList();
    }
}
