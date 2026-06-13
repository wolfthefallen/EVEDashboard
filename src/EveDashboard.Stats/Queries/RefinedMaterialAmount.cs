namespace EveDashboard.Stats.Queries;

/// <summary>
/// One refined-material running total for the "Refined output" section
/// (plan 43). The <see cref="Quantity"/> is yield-applied (the same floored
/// amount that underlies the ISK figure), and a material appears here only once
/// it has produced a non-zero quantity.
/// </summary>
/// <param name="TypeId">EVE type id of the refined material (e.g. Tritanium).</param>
/// <param name="Name">Display name resolved from the SDE, or <c>#&lt;typeId&gt;</c> when unresolved.</param>
/// <param name="Quantity">Total yield-applied units produced this session (per the scope of the carrying snapshot).</param>
public sealed record RefinedMaterialAmount(long TypeId, string Name, long Quantity)
{
    /// <summary>
    /// Plan 45: yield-applied ISK value of this material under the carrying
    /// snapshot's price mode (<c>0</c> when no price was captured for it). Summing
    /// <see cref="Isk"/> across a scope's materials yields that scope's Total ISK.
    /// Init-only with a <c>0</c> default so existing 3-arg construction sites are
    /// unaffected.
    /// </summary>
    public double Isk { get; init; }
}
