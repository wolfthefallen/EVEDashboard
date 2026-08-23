namespace EveDashboard.Stats.Queries;

using EveDashboard.Core.Sessions;

/// <summary>
/// Snapshot of one pilot's mining statistics within a session, keyed by EVE
/// character id. Per-pilot rates use the parent <see cref="SessionStats"/>'s
/// <see cref="SessionStats.TotalTimeMined"/> as denominator (there is no
/// per-pilot active-duration field).
/// </summary>
/// <param name="PilotId">EVE character id of this pilot. Matches the
///     <c>pilot_id</c> in the session SQLite <c>session_pilot</c> / <c>log</c>
///     tables; same id used in the gamelog filename.</param>
/// <param name="LastEventAt"><c>MAX(eve_timestamp)</c> across this pilot's
///     <c>log</c> rows for the session, or <c>null</c> when this pilot has no
///     events yet (the per-pilot widget renders "No events yet" in that case).</param>
/// <param name="TotalM3Mined">Sum of <c>volume_m3</c> across this pilot's
///     <c>log</c> rows for the session. Residue rows contribute 0 because
///     their <c>volume_m3</c> is null.</param>
/// <param name="M3PerSecond">This pilot's <see cref="TotalM3Mined"/> divided
///     by the session-level <see cref="SessionStats.TotalTimeMined"/> in
///     seconds. <c>0</c> when the active duration is <c>0</c>
///     (divide-by-zero guard).</param>
/// <param name="TotalIskMined">Estimated ISK valuation per session, computed by
///     the <c>IIskCalculator</c> ISK pipeline.
///     Returns <c>0</c> if SDE lookups or market prices are unavailable for any
///     of the pilot's mined products.</param>
/// <param name="IskPerHour">Estimated ISK per hour, computed by the same
///     ISK pipeline. Returns <c>0</c> when SDE lookups or market prices are
///     unavailable, or when the active duration is <c>0</c>.</param>
/// <param name="TotalIskFromCrits">Estimated ISK valuation for this pilot
///     considering ONLY rows where <c>event_kind = 'Crit'</c>. Computed by the
///     ISK pipeline over the crit-subset units in the same single
///     <c>IIskCalculator.ComputeAsync</c> call that produces
///     <see cref="TotalIskMined"/>. Not currently surfaced on any per-pilot
///     widget; exposed here so per-pilot UI can be wired without
///     re-touching this layer.</param>
/// <param name="CritIskPercent"><see cref="TotalIskFromCrits"/> divided by
///     <see cref="TotalIskMined"/>. Returns <c>0</c> when
///     <see cref="TotalIskMined"/> is <c>0</c> (divide-by-zero guard).</param>
/// <param name="TotalM3FromCrits">Sum of <c>volume_m3</c> across this pilot's
///     <c>log</c> rows where <c>event_kind = 'Crit'</c>.</param>
/// <param name="CritCount">Count of this pilot's <c>log</c> rows where
///     <c>event_kind = 'Crit'</c>.</param>
/// <param name="CritChance"><see cref="CritCount"/> divided by
///     (<see cref="CritCount"/> + tick count). <c>0</c> when no
///     crit-eligible events have been seen.</param>
/// <param name="Role">The pilot's current fleet role in this session
///     (read from <c>session_pilot.role</c>, NOT event-time role). Drives
///     per-pilot widget border color and DisplayName tag.</param>
public sealed record CharacterStats(
    long             PilotId,
    DateTimeOffset?  LastEventAt,
    double           TotalM3Mined,
    double           M3PerSecond,
    double           TotalIskMined,
    double           IskPerHour,
    double           TotalIskFromCrits,
    double           CritIskPercent,
    double           TotalM3FromCrits,
    long             CritCount,
    double           CritChance,
    PilotRole        Role)
{
    /// <summary>
    /// This pilot's refined-material output, yield-applied and sorted
    /// by quantity descending. Init-only with an empty default so existing
    /// construction sites are unaffected.
    /// </summary>
    public IReadOnlyList<RefinedMaterialAmount> RefinedOutput { get; init; } = Array.Empty<RefinedMaterialAmount>();
}
