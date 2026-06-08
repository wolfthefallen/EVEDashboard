namespace EveDashboard.Stats.Queries;

/// <summary>
/// Snapshot of a session's mining statistics, as seen at
/// <see cref="LastCalculated"/>. All values are derived from the <c>log</c>
/// and <c>session_time</c> tables in the session SQLite database.
/// </summary>
/// <param name="SessionId">Session id this snapshot describes. Matches
///     <c>session.session_id</c> in the session SQLite database.</param>
/// <param name="TotalTimeMined">Active duration of the session, excluding
///     paused windows. Computed from <c>session_time</c> rows where
///     <c>is_pause = 0</c>; the wall-clock endpoint for any currently-open
///     row is the caller's <c>asOf</c> argument. UI formats as HH:MM:SS.</param>
/// <param name="LastCalculated">The wall-clock instant the snapshot was
///     taken at; equals the caller's <c>asOf</c> argument
///     (plan 13 decision 2.5). Debug signal for snapshot freshness.</param>
/// <param name="TotalM3Mined">Sum of <c>volume_m3</c> across every <c>log</c>
///     row for this session. Residue rows contribute 0 because their
///     <c>volume_m3</c> is null (see plan 13 decision 5.1).</param>
/// <param name="M3PerSecond"><see cref="TotalM3Mined"/> divided by
///     <see cref="TotalTimeMined"/> in seconds. <c>0</c> when the active
///     duration is <c>0</c> (plan 13 decision 8.2).</param>
/// <param name="TotalIskMined">Estimated session-wide ISK valuation, computed
///     by the plan-18 ISK pipeline; see <c>MEMORY.md -&gt; project_isk_calculation</c>.
///     Returns <c>0</c> if SDE lookups or market prices are unavailable for any
///     mined product in the session.</param>
/// <param name="IskPerHour">Estimated session-wide ISK per hour, computed by
///     the plan-18 ISK pipeline. Returns <c>0</c> when SDE lookups or market
///     prices are unavailable, or when the active duration is <c>0</c>.</param>
/// <param name="TotalIskFromCrits">Estimated session-wide ISK valuation
///     considering ONLY rows where <c>event_kind = 'Crit'</c>. Computed by the
///     plan-18 ISK pipeline over the crit-subset units in the same single
///     <c>IIskCalculator.ComputeAsync</c> call that produces
///     <see cref="TotalIskMined"/>. Returns <c>0</c> when no crit rows exist
///     or SDE/market prices are unavailable.</param>
/// <param name="CritIskPercent"><see cref="TotalIskFromCrits"/> divided by
///     <see cref="TotalIskMined"/>. Returns <c>0</c> when
///     <see cref="TotalIskMined"/> is <c>0</c> (divide-by-zero guard).</param>
/// <param name="TotalM3FromCrits">Sum of <c>volume_m3</c> across every
///     <c>log</c> row for this session where <c>event_kind = 'Crit'</c>.</param>
/// <param name="CritCount">Count of <c>log</c> rows for this session where
///     <c>event_kind = 'Crit'</c>.</param>
/// <param name="CritChance"><see cref="CritCount"/> divided by
///     (<see cref="CritCount"/> + tick count). <c>0</c> when no
///     crit-eligible events have been seen (plan 13 decision 3.2).</param>
/// <param name="ByCharacter">Per-pilot breakdown, keyed by
///     <c>pilot_id</c>. Includes every pilot in <c>session_pilot</c> for
///     the session, even those with no <c>log</c> rows yet.</param>
public sealed record SessionStats(
    string                                     SessionId,
    TimeSpan                                   TotalTimeMined,
    DateTimeOffset                             LastCalculated,
    double                                     TotalM3Mined,
    double                                     M3PerSecond,
    double                                     TotalIskMined,
    double                                     IskPerHour,
    double                                     TotalIskFromCrits,
    double                                     CritIskPercent,
    double                                     TotalM3FromCrits,
    long                                       CritCount,
    double                                     CritChance,
    IReadOnlyDictionary<long, CharacterStats>  ByCharacter);
