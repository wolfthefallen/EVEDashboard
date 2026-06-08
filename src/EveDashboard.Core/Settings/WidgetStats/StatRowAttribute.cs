namespace EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Marks a public property on a widget ViewModel as a customisable stat row
/// (plan 20). Read by reflection at widget-construction time and on every
/// <see cref="ISettingsRepository.SettingChanged"/> matching the widget's
/// config key. Stats inherit the property name as their identity
/// (<c>nameof(VM.PropertyName)</c>) — there is no separate mapping table.
/// </summary>
/// <remarks>
/// <para>
/// Reorderable=false marks "hide-only" rows: their position is locked because
/// the row is rendered by a dedicated chrome / footer TextBlock (e.g.
/// <c>WidgetBaseViewModel.LastUpdated</c> is in the WidgetBase chrome line,
/// not the body's stat list). The popup's hide-only zone shows them with a
/// checkbox so the user can hide the dedicated TextBlock.
/// </para>
/// <para>
/// <see cref="DefaultOrder"/> determines initial slot among reorderable rows
/// (smaller = earlier). Tie-breaks are deterministic: ties break by property
/// name ascending. Hide-only rows have their own ordering inside the popup's
/// hide-only zone, also by <see cref="DefaultOrder"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class StatRowAttribute : Attribute
{
    /// <summary>Initialise the attribute with the human-readable label.</summary>
    /// <param name="label">User-visible label rendered to the left of the value.</param>
    public StatRowAttribute(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Label = label;
    }

    /// <summary>User-visible label rendered to the left of the value.</summary>
    public string Label { get; }

    /// <summary>
    /// Optional <see cref="string.Format(string,object?)"/> format applied to
    /// the value. Examples: <c>"N0"</c>, <c>"N2"</c>, <c>"P1"</c>. <c>null</c>
    /// means render the value via its default <c>ToString()</c>. Ignored for
    /// hide-only rows (their TextBlock has its own <c>StringFormat</c>).
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// True (default) means the user can drag this row to a new position.
    /// False means the row's position is locked (its rendering is handled by
    /// a dedicated TextBlock outside the body's <c>Stats</c> list — only
    /// hide / show is meaningful).
    /// </summary>
    public bool Reorderable { get; init; } = true;

    /// <summary>
    /// Default position among reorderable rows when no saved config exists
    /// AND for inserting future-added stats into an existing saved config.
    /// Smaller = earlier in the list.
    /// </summary>
    public int DefaultOrder { get; init; }

    /// <summary>
    /// Default visual emphasis for this row when no saved
    /// <see cref="StatEntry.Emphasis"/> overrides it (plan 20 amendment A1).
    /// Defaults to <see cref="StatEmphasis.None"/>; widget VMs explicitly set
    /// <see cref="StatEmphasis.Header0"/> / <see cref="StatEmphasis.Header1"/>
    /// / <see cref="StatEmphasis.Hero"/> on header / hero properties to match
    /// pre-plan-20 typography.
    /// </summary>
    public StatEmphasis DefaultEmphasis { get; init; } = StatEmphasis.None;
}
