namespace EveDashboard.Core.Layout;

using System.Text.Json.Serialization;

/// <summary>
/// Top-level persisted shape of the user's dashboard layout. Stored
/// as JSON in the <c>setting</c> table under key <c>widget_layout</c>.
/// </summary>
/// <param name="Items">
/// Ordered list of layout items. The order of this list IS the user's chosen
/// widget order, left-to-right, top-to-bottom in the <c>WrapPanel</c> flow.
/// May contain entries for pilots not currently in any active session
/// ("ghost" entries); those are preserved
/// indefinitely so that a pilot dragged to slot N reappears at slot N if
/// re-selected.
/// </param>
public sealed record WidgetLayout(
    [property: JsonPropertyName("items")] IReadOnlyList<WidgetLayoutItem> Items);

/// <summary>One entry in a <see cref="WidgetLayout"/>: a descriptor plus its hidden flag.</summary>
/// <param name="Descriptor">Identity of the widget (Session or Pilot).</param>
/// <param name="Hidden">If true, the widget is hidden from the flow but still tracked (View collapsed; the VM continues to receive stat refreshes while hidden).</param>
public sealed record WidgetLayoutItem(WidgetDescriptor Descriptor, bool Hidden);
