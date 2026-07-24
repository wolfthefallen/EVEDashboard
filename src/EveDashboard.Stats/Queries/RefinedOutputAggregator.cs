namespace EveDashboard.Stats.Queries;

/// <summary>
/// Single source of the "sum refined output across a set of scopes" math
/// (plan 46). Both the compare-mode view (summing across the selected sessions)
/// and the empty-state lifetime rollup (summing across every saved session)
/// call this so the per-material aggregation + sort lives in exactly one place.
/// </summary>
public static class RefinedOutputAggregator
{
    /// <summary>
    /// Sum refined output per material across the supplied scopes. Quantities
    /// and ISK are added per <see cref="RefinedMaterialAmount.TypeId"/>; the
    /// display name is taken from the first occurrence of each material. The
    /// result is sorted by total quantity descending, ties broken by
    /// <see cref="RefinedMaterialAmount.TypeId"/> ascending — the same
    /// deterministic ordering the live per-session refined list uses
    /// (<c>SqliteSessionStatsRepository.BuildRefinedList</c>), so the two paths
    /// never disagree on tied materials.
    /// </summary>
    /// <param name="perScope">One refined-output list per scope (e.g. per
    ///     session). Each inner list may be empty; the outer sequence may be
    ///     empty.</param>
    /// <returns>One row per distinct material, quantity + ISK summed across the
    ///     scopes, sorted by quantity descending. Empty when no scope contributes
    ///     a material.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="perScope"/> is null.</exception>
    public static IReadOnlyList<RefinedMaterialAmount> SumAcross(
        IEnumerable<IReadOnlyList<RefinedMaterialAmount>> perScope)
    {
        ArgumentNullException.ThrowIfNull(perScope);

        var byMaterial = new Dictionary<long, (string Name, long Qty, double Isk)>();
        foreach (var scope in perScope)
        {
            if (scope is null) continue;
            foreach (var mat in scope)
            {
                var cur = byMaterial.GetValueOrDefault(mat.TypeId);
                // Name from the first occurrence (cur.Name is null only on the
                // first add for this TypeId).
                byMaterial[mat.TypeId] = (cur.Name ?? mat.Name, cur.Qty + mat.Quantity, cur.Isk + mat.Isk);
            }
        }

        return byMaterial
            .Select(kvp => new RefinedMaterialAmount(kvp.Key, kvp.Value.Name, kvp.Value.Qty) { Isk = kvp.Value.Isk })
            .OrderByDescending(m => m.Quantity)
            .ThenBy(m => m.TypeId)
            .ToArray();
    }
}
