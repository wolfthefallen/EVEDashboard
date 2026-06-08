namespace EveDashboard.Core.Domain;

/// <summary>
/// An EVE item type recognised by this app — either a mineable item
/// (ore or gas) read from a mining log line, or a refined output (mineral,
/// moon material, ice product) referenced by an ore's <c>type_materials</c>.
/// </summary>
/// <param name="TypeId">EVE type ID (the SDE primary key).</param>
/// <param name="Name">Display name as shown in EVE (e.g. <c>"Veldspar"</c>).</param>
/// <param name="VolumePerUnitM3">
/// m³ per single item-stack-of-1. Used by the stats engine to convert a raw
/// mined-unit count to m³.
/// </param>
/// <param name="PortionSize">
/// Refining batch size (units consumed per refining cycle). The single
/// uniform ISK formula is
/// <c>(quantity / portion_size) * type_materials.quantity * client_refining_percent</c>.
/// Items that have no <c>type_materials</c> rows (e.g. gas in v1, or refined
/// outputs themselves) carry the SDE's value here but contribute no ISK
/// because the join to <c>type_materials</c> returns no rows.
/// </param>
public sealed record MiningProduct(
    long TypeId,
    string Name,
    double VolumePerUnitM3,
    int PortionSize);
