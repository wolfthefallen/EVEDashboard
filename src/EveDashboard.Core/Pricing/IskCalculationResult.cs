namespace EveDashboard.Core.Pricing;

/// <summary>
/// Result of one <see cref="IIskCalculator"/> invocation, optionally including
/// a crit-subset total when the caller used the two-dict overload.
/// </summary>
/// <param name="TotalIsk">Total ISK value computed from the input mined units.</param>
/// <param name="CritIsk">
/// Total ISK value considering ONLY the crit-subset of the input mined units
/// (the <c>critTypeIdToUnitsMined</c> argument on the two-dict overload).
/// <c>0</c> for the single-dict overload and for any pilot whose crit-subset
/// dict was empty or null. Computed in the same pass as <see cref="TotalIsk"/>
/// — exact math, no proportional rounding drift.
/// </param>
/// <param name="IskByInputType">
/// Per-source-input-type ISK breakdown — the contribution of each top-level
/// input type id (ore / gas) in the original <c>typeIdToUnitsMined</c> dict.
/// Sum of values approximately equals <see cref="TotalIsk"/> within
/// floor-rounding tolerance (per-input floor is applied per (input, material)
/// pair, vs. the older single shared-accumulator floor; see plan 25). Empty
/// when the input dict was empty.
/// </param>
/// <remarks>
/// Plan 25 added <see cref="IskByInputType"/> to drive the Reports per-ore
/// breakdown widget. Plan 36 added <see cref="CritIsk"/> so the repository
/// can derive Crit ISK % in a single per-pilot ISK call (the two-dict
/// overload). The single-dict overload always returns <see cref="CritIsk"/>
/// = <c>0</c>.
/// </remarks>
public sealed record IskCalculationResult(
    double                            TotalIsk,
    double                            CritIsk,
    IReadOnlyDictionary<long, double> IskByInputType);
