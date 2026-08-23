namespace EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// One persisted entry inside a <see cref="WidgetStatsConfig"/>.
/// Identifies a stat by its C# property name on the widget VM
/// (<c>nameof(VM.PropertyName)</c>; no mapping table) and carries
/// the user's hide flag plus visual emphasis.
/// </summary>
/// <param name="StatKey">
/// C# property name on the widget VM. Examples: <c>"M3PerSecond"</c>,
/// <c>"PilotName"</c>, <c>"LastUpdated"</c>. The renderer looks the property
/// up via reflection on the VM type to find its <see cref="StatRowAttribute"/>
/// metadata.
/// </param>
/// <param name="Hidden">
/// <c>true</c> means the row is hidden from the widget body. The entry stays
/// in the persisted list at its current position so re-showing the row
/// restores its original slot.
/// </param>
/// <param name="Emphasis">
/// User-selected visual emphasis level for this row.
/// When the saved entry has <c>None</c> AND the property's
/// <see cref="StatRowAttribute.DefaultEmphasis"/> is non-<c>None</c>, the
/// renderer uses the saved value verbatim (the user has explicitly chosen
/// "no emphasis" for this row).
/// </param>
public sealed record StatEntry(string StatKey, bool Hidden, StatEmphasis Emphasis);
