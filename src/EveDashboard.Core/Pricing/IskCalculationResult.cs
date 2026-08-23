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
/// floor-rounding tolerance (the floor is applied per (input, material)
/// pair, not once on a single shared accumulator). Empty
/// when the input dict was empty.
/// </param>
/// <remarks>
/// <see cref="IskByInputType"/> drives the Reports per-ore
/// breakdown widget. <see cref="CritIsk"/> lets the repository
/// derive Crit ISK % in a single per-pilot ISK call (the two-dict
/// overload); the single-dict overload always returns <see cref="CritIsk"/>
/// = <c>0</c>. <see cref="RefinedQuantitiesByMaterial"/> and
/// <see cref="IskByMaterial"/> are init-only properties (default empty) so
/// construction sites that do not set them are unaffected — only the
/// production calculator populates them. <see cref="IskByMaterial"/> drives
/// the Reports compare-view "Total Refined Output" ISK column.
/// </remarks>
public sealed record IskCalculationResult(
    double                            TotalIsk,
    double                            CritIsk,
    IReadOnlyDictionary<long, double> IskByInputType)
{
    /// <summary>
    /// Yield-applied refined output for the FULL units (not the crit
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
    /// Yield-applied ISK value per refined-material <c>type_id</c>, for the
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
