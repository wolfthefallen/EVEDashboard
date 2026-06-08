namespace EveDashboard.App.Wpf.Widgets.StatConfig;

/// <summary>
/// Result of <see cref="StatRowsBuilder.Resolve"/> (plan 20). The two lists
/// are emitted separately because they flow into different rendering
/// surfaces:
/// <list type="bullet">
/// <item><see cref="Reorderable"/> -> the widget body's <c>Stats</c>
///   ObservableCollection (and the popup's reorderable section).</item>
/// <item><see cref="HideOnly"/> -> dedicated TextBlocks in chrome / footer
///   (and the popup's hide-only section).</item>
/// </list>
/// Hide-only rows are NOT projected into the widget body's <c>Stats</c>
/// collection — they would render twice if they were.
/// </summary>
/// <param name="Reorderable">
/// Reorderable rows in the user-chosen order, with their effective emphasis.
/// May be empty (e.g. for <c>SessionConfigViewModel</c> which has no
/// reorderable stats).
/// </param>
/// <param name="HideOnly">
/// Hide-only rows in <c>StatRowAttribute.DefaultOrder</c> ascending order.
/// Always at least <c>WidgetBaseViewModel.LastUpdated</c> (inherited
/// hide-only) on every <c>WidgetBaseViewModel</c>-derived widget.
/// </param>
public sealed record StatRowsResolution(
    IReadOnlyList<StatRowDescriptor> Reorderable,
    IReadOnlyList<StatRowDescriptor> HideOnly);
