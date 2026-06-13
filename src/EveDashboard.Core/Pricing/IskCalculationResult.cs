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
/// = <c>0</c>. Plan 43 added <see cref="RefinedQuantitiesByMaterial"/> as an
/// init-only property (default empty) so existing construction sites are
/// unaffected — only the production calculator populates it. Plan 45 added
/// <see cref="IskByMaterial"/> the same way to drive the Reports compare-view
/// "Total Refined Output" ISK column.
/// </remarks>
public sealed record IskCalculationResult(
    double                            TotalIsk,
    double                            CritIsk,
    IReadOnlyDictionary<long, double> IskByInputType)
{
    /// <summary>
    /// Plan 43: yield-applied refined output for the FULL units (not the crit
    /// subset) — refined-material <c>type_id</c> -> total units, summed across
    /// every input ore and floored per (input, material) exactly as the ISK
    /// total is. Materials with no captured price are still present here (they
    /// contribute <c>0</c> to <see cref="TotalIsk"/> but are real output);
    /// materials whose yield-applied quantity floors to <c>0</c> are omitted.
    /// Empty by default; populated only by the production
    /// <c>IskCalculator</c>.
    /// </summary>
    public IReadOnlyDictionary<long, long> RefinedQuantitiesByMaterial { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<long, long>.Empty;

    /// <summary>
    /// Plan 45: yield-applied ISK value per refined-material <c>type_id</c>, for the
    /// FULL units (not the crit subset), under the caller's price mode. The sum of
    /// the values equals <see cref="TotalIsk"/> by construction (both are accumulated
    /// from the same priced (input, material) pairs). A material with no captured
    /// price is ABSENT here (it contributes <c>0</c> ISK) even though it appears in
    /// <see cref="RefinedQuantitiesByMaterial"/> with a non-zero quantity. Empty by
    /// default; populated only by the production <c>IskCalculator</c>.
    /// </summary>
    public IReadOnlyDictionary<long, double> IskByMaterial { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<long, double>.Empty;
}
