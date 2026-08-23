namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Reflection;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Resolved stat-row metadata combining <see cref="StatRowAttribute"/> from
/// reflection with the user's saved per-row state from
/// <see cref="WidgetStatsConfig"/>. Produced by
/// <see cref="StatRowsBuilder.Resolve"/>; consumed by
/// <see cref="StatRowsRenderer"/> (for the widget body) and the popup
/// view-model (for the editable working copy).
/// </summary>
/// <param name="Key">C# property name on the source VM.</param>
/// <param name="Label">User-visible label.</param>
/// <param name="Format"><see cref="string.Format(string,object?)"/> spec, or null = ToString().</param>
/// <param name="Reorderable">If false, this is a "hide-only" row whose body lives in a dedicated TextBlock.</param>
/// <param name="Hidden">User's current hide flag; for hide-only rows this controls the dedicated TextBlock visibility.</param>
/// <param name="Emphasis">Effective emphasis (saved override OR property default if no saved entry).</param>
/// <param name="PropInfo">Source-VM property reflection handle; used by <see cref="StatRowsRenderer"/> to read the live value.</param>
public sealed record StatRowDescriptor(
    string Key,
    string Label,
    string? Format,
    bool Reorderable,
    bool Hidden,
    StatEmphasis Emphasis,
    PropertyInfo PropInfo);
