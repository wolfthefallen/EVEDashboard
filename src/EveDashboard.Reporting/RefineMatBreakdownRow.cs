namespace EveDashboard.Reporting;

/// <summary>
/// One refined-material row in the Reports "Refined output" breakdown widget
/// — the yield-applied quantity of a single refined material
/// (Tritanium, etc.) produced within a session (or a session+pilot scope).
/// Reporting shows ALL materials (it ignores the live widget's auto-show /
/// hide) and is quantity-only
/// (per-material ISK is out of scope — the per-ore breakdown already shows ISK).
/// </summary>
/// <param name="TypeId">EVE refined-material type id.</param>
/// <param name="TypeName">Display name from the SDE; falls back to <c>"#{TypeId}"</c> when unknown.</param>
/// <param name="Quantity">Total yield-applied units produced in the scoped slice.</param>
public sealed record RefineMatBreakdownRow(
    long   TypeId,
    string TypeName,
    long   Quantity);
