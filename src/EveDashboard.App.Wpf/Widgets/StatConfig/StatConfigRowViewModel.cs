namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// One row inside the cog popup's editable working copy (plan 20). Carries
/// the user's edits for hide / emphasis until they click <c>Apply</c>; on
/// Apply the popup VM projects the working copy into a
/// <see cref="WidgetStatsConfig"/> and writes via the settings repository.
/// </summary>
/// <remarks>
/// <para>
/// Drag-drop reorders the popup's <c>ObservableCollection&lt;StatConfigRowViewModel&gt;</c>
/// in place; the popup VM reads the collection's order on Apply (no per-row
/// "position" property needed).
/// </para>
/// <para>
/// Hide-only rows construct with <see cref="Reorderable"/> = false; the popup
/// XAML uses that to suppress the drag handle and the emphasis picker for
/// those rows (their styling lives in dedicated chrome / footer TextBlocks
/// outside the popup's purview).
/// </para>
/// </remarks>
public sealed partial class StatConfigRowViewModel : ObservableObject
{
    /// <summary>C# property name on the source VM. Stable identity for this row across drags.</summary>
    public string Key { get; }

    /// <summary>User-visible label.</summary>
    public string Label { get; }

    /// <summary>True iff this row is reorderable (drag handle + emphasis picker shown). False for hide-only rows.</summary>
    public bool Reorderable { get; }

    /// <summary>User's hide-flag working copy; two-way bound by the row's checkbox.</summary>
    [ObservableProperty]
    private bool _hidden;

    /// <summary>User's emphasis working copy; two-way bound by the row's emphasis ComboBox (Reorderable rows only).</summary>
    [ObservableProperty]
    private StatEmphasis _emphasis;

    /// <summary>Construct an editable row VM.</summary>
    /// <param name="key">C# property name on the source VM.</param>
    /// <param name="label">User-visible label.</param>
    /// <param name="reorderable">True if this row is reorderable; false for hide-only.</param>
    /// <param name="hidden">Initial hidden state.</param>
    /// <param name="emphasis">Initial emphasis state.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> or <paramref name="label"/> is null.</exception>
    public StatConfigRowViewModel(string key, string label, bool reorderable, bool hidden, StatEmphasis emphasis)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);
        Key = key;
        Label = label;
        Reorderable = reorderable;
        _hidden = hidden;
        _emphasis = emphasis;
    }
}
