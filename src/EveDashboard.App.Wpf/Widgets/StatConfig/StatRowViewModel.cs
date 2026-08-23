namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// One ROW currently rendered in a widget body's stat list. Bound by
/// the <c>ItemsControl</c> in <c>PerPilotWidgetView.xaml</c> /
/// <c>SessionStatsView.xaml</c>; the widget's body <c>DataTemplate</c> picks
/// a typography style based on <see cref="Emphasis"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stat-row identity is the C# property name on the source VM
/// (<see cref="Key"/>); the renderer keeps a 1:1 map from VM property to
/// <see cref="StatRowViewModel"/> instance for the lifetime of the rendered
/// list. Property changes on the source VM update <see cref="FormattedValue"/>
/// in place — no full <c>Stats</c> rebuild needed.
/// </para>
/// <para>
/// Hidden rows (<see cref="StatEntry.Hidden"/> = true) are NOT projected here —
/// they're filtered upstream so the widget body's <c>ItemsControl</c> never
/// has to render an invisible item.
/// </para>
/// </remarks>
public sealed partial class StatRowViewModel : ObservableObject
{
    /// <summary>C# property name on the source VM. Stable identity for this row.</summary>
    public string Key { get; }

    /// <summary>User-visible label rendered to the left of the value.</summary>
    public string Label { get; }

    /// <summary>Pre-formatted value string, ready for the row template's right cell.</summary>
    [ObservableProperty]
    private string _formattedValue = string.Empty;

    /// <summary>Visual emphasis level for this row.</summary>
    [ObservableProperty]
    private StatEmphasis _emphasis;

    /// <summary>Construct a stat row VM.</summary>
    /// <param name="key">C# property name on the source VM.</param>
    /// <param name="label">User-visible label.</param>
    /// <param name="formattedValue">Initial formatted value string.</param>
    /// <param name="emphasis">Initial visual emphasis level.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> or <paramref name="label"/> is null.</exception>
    public StatRowViewModel(string key, string label, string formattedValue, StatEmphasis emphasis)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);
        Key = key;
        Label = label;
        _formattedValue = formattedValue ?? string.Empty;
        _emphasis = emphasis;
    }
}
