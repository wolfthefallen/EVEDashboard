namespace EveDashboard.Core.Domain;

/// <summary>
/// One row of an EVE refining recipe — the material that an ore (or other
/// reprocessable type) yields per refining batch.
/// </summary>
/// <param name="MaterialTypeId">EVE type id of the refined output material.</param>
/// <param name="Quantity">
/// Quantity of <paramref name="MaterialTypeId"/> produced per refining batch
/// at 100% efficiency. The canonical ISK algorithm combines it with the
/// source type's <c>portion_size</c> and the configured
/// <c>refining_yield_percent</c>.
/// </param>
public sealed record TypeMaterial(long MaterialTypeId, long Quantity);
