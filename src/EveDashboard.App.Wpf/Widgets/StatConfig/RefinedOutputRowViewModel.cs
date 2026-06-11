namespace EveDashboard.App.Wpf.Widgets.StatConfig;

/// <summary>
/// Plan 43: one visible row in a live widget's "Refined output" section — the
/// refined material's display name and its yield-applied running quantity.
/// Immutable; the section rebuilds its row collection on each merge.
/// </summary>
/// <param name="TypeId">EVE refined-material type id (identity for merge + visibility).</param>
/// <param name="Name">Display name (SDE-resolved, or <c>#&lt;typeId&gt;</c>).</param>
/// <param name="Quantity">Yield-applied units produced so far this session (in this widget's scope).</param>
public sealed record RefinedOutputRowViewModel(long TypeId, string Name, long Quantity);
