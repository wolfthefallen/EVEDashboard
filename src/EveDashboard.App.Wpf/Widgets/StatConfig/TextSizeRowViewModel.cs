namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Plan 47: one editable row inside the Reports "Text size" popup. Carries the
/// user's chosen <see cref="StatEmphasis"/> for one named element of a table
/// widget (a label line, or the table itself) until they click <c>Apply</c>;
/// the popup VM then projects the rows into an element-key -&gt; emphasis map and
/// persists it. Mirrors <see cref="StatConfigRowViewModel"/> but with no
/// hide / reorder concept — text size is the only editable field.
/// </summary>
public sealed partial class TextSizeRowViewModel : ObservableObject
{
    /// <summary>Stable element key persisted in the text-size map (e.g. <c>"Title"</c>, <c>"Table"</c>, <c>"TotalTime"</c>).</summary>
    public string Key { get; }

    /// <summary>User-visible label for this element in the popup.</summary>
    public string Label { get; }

    /// <summary>
    /// This element's DEFAULT emphasis (the closest active-session look to its current
    /// styling). When the saved value equals this default the popup drops it, so an
    /// element with no override resolves back to its default rather than to None.
    /// </summary>
    public StatEmphasis Default { get; }

    /// <summary>User's emphasis working copy; two-way bound by the row's emphasis ComboBox.</summary>
    [ObservableProperty]
    private StatEmphasis _emphasis;

    /// <summary>Construct an editable text-size row VM.</summary>
    /// <param name="key">Stable element key.</param>
    /// <param name="label">User-visible label.</param>
    /// <param name="emphasis">Initial (effective) emphasis state.</param>
    /// <param name="defaultEmphasis">This element's default emphasis.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> or <paramref name="label"/> is null.</exception>
    public TextSizeRowViewModel(string key, string label, StatEmphasis emphasis, StatEmphasis defaultEmphasis)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);
        Key = key;
        Label = label;
        _emphasis = emphasis;
        Default = defaultEmphasis;
    }
}
