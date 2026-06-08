namespace EveDashboard.Core.Settings.WidgetStats;

using System.Text.Json.Serialization;

/// <summary>
/// Persisted shape of a single widget instance's stat customisation (plan 20).
/// Stored as JSON in the <c>setting</c> table under one of two key shapes:
/// <list type="bullet">
/// <item><c>session_stats_config</c> — singleton, drives the SessionStats widget.</item>
/// <item><c>per_pilot_stats_config.&lt;pilot_id&gt;</c> — one row per pilot id, drives that pilot's PerPilot widget.</item>
/// </list>
/// The order of <see cref="Entries"/> IS the user's chosen row order in the
/// widget body. Hidden rows (<see cref="StatEntry.Hidden"/>) keep their slot
/// so re-showing restores the original position (decision 5.2 = A).
/// </summary>
/// <param name="Entries">
/// Ordered list of stat entries. Stats present on the widget VM but missing
/// from this list are inserted at their <see cref="StatRowAttribute.DefaultOrder"/>
/// position (decision 7.1 = B — future-stat inheritance). Stats listed here
/// but not present on the VM are ignored (defensive — handles the
/// "future plan removed a stat" edge case).
/// </param>
[JsonConverter(typeof(WidgetStatsConfigJsonConverter))]
public sealed record WidgetStatsConfig(IReadOnlyList<StatEntry> Entries);
