namespace EveDashboard.Core.Sessions.Historical;

using EveDashboard.Core.Domain;

/// <summary>
/// One day's worth of mining, worked out from the gamelogs and ready to be
/// written as a session.
/// </summary>
/// <param name="Day">The UTC day this session is named for - the day its first mining run started.</param>
/// <param name="WindowStart">Start of the session: the first event that survived exclusion.</param>
/// <param name="WindowStop">End of the session: the idle threshold after the day's last event, or the end of a merged source if that runs later.</param>
/// <param name="Roster">Who was out: pilots who mined ore that day, plus the pilots of any merged source.</param>
/// <param name="Merge">Saved sessions to fold into this one; their rows are copied and they are archived.</param>
/// <param name="Exclusions">Per-pilot windows owned by saved sessions that are NOT being folded in, so their time is not counted twice.</param>
/// <param name="Events">The day's events that survived exclusion, for the builder to write.</param>
/// <param name="PilotsWithNoOre">Pilots with events that day but no Tick or Crit - they mined nothing, so they are not rostered, but the user is told.</param>
/// <remarks>
/// A day may hold several mining runs; they share one session, with the gaps
/// between them written as pauses, so the day's active time (and therefore its
/// m3/sec and ISK/h) counts only time actually spent mining.
/// </remarks>
public sealed record DailySessionPlan(
    DateOnly Day,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowStop,
    IReadOnlyList<FleetMember> Roster,
    IReadOnlyList<SessionSpan> Merge,
    IReadOnlyList<PilotExclusion> Exclusions,
    IReadOnlyList<MiningEvent> Events,
    IReadOnlyList<long> PilotsWithNoOre);

/// <summary>A day that will not become a session, and why.</summary>
/// <param name="Day">The UTC day.</param>
/// <param name="Reason">Plain-language explanation to show the user.</param>
public sealed record DailySessionSkip(DateOnly Day, string Reason);

/// <summary>What a batch build is going to do, decided before anything is written.</summary>
/// <param name="Plans">The sessions to build, in day order.</param>
/// <param name="Skipped">The days deliberately passed over, in day order.</param>
public sealed record DailyPlanResult(
    IReadOnlyList<DailySessionPlan> Plans,
    IReadOnlyList<DailySessionSkip> Skipped);
