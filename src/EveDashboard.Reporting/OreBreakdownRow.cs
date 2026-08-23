namespace EveDashboard.Reporting;

/// <summary>
/// One ore-type row in the Reports per-ore breakdown widget — total m^3 mined
/// and estimated ISK valuation for a single source <c>type_id</c> within a
/// session (or a session+pilot scope).
/// </summary>
/// <param name="TypeId">EVE source type id (the ore / gas / etc. that was mined; matches <c>log.type_id</c>).</param>
/// <param name="TypeName">Display name from the SDE; falls back to <c>"#{TypeId}"</c> when the SDE lookup is unknown for this type.</param>
/// <param name="VolumeM3">Sum of <c>volume_m3</c> across the matching <c>log</c> rows.</param>
/// <param name="Isk">Estimated ISK valuation for this type's contribution, computed from the <c>IIskCalculator</c> pipeline's per-input-type breakdown (<c>IskCalculationResult.IskByInputType</c>).</param>
public sealed record OreBreakdownRow(
    long   TypeId,
    string TypeName,
    double VolumeM3,
    double Isk);
