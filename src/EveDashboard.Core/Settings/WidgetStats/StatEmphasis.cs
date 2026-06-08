namespace EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Visual emphasis level applied to a single stat row inside a widget body
/// (plan 20). Drives the row template's typography (font size + weight + brush)
/// without affecting reorder / hide semantics. Persisted per-row via
/// <see cref="StatEntry.Emphasis"/>; default per-property comes from
/// <see cref="StatRowAttribute.DefaultEmphasis"/>.
/// </summary>
/// <remarks>
/// The five values map to concrete styles defined by the row DataTemplates in
/// <c>EveDashboard.App.Wpf.Widgets.StatConfig</c> (the App.Wpf project owns the
/// XAML side; this enum lives in Core because <see cref="StatEntry"/> is
/// persisted JSON and must be Core-clean).
/// </remarks>
public enum StatEmphasis
{
    /// <summary>Default plain row: foreground brush, no bold, label-and-right-aligned-value layout.</summary>
    None = 0,

    /// <summary>14pt bold. Compact header — current PerPilot <c>PilotName</c> styling.</summary>
    Header0 = 1,

    /// <summary>16pt bold. Standard header — current SessionStats <c>DisplaySessionName</c> styling.</summary>
    Header1 = 2,

    /// <summary>20pt bold. Larger header for callouts; no current widget uses this by default — available for user-driven emphasis.</summary>
    Header2 = 3,

    /// <summary>22pt with the accent brush. Hero callout — current SessionStats <c>TotalTimeMined</c> styling.</summary>
    Hero = 4,
}
