namespace EveDashboard.App.Wpf.Widgets;

/// <summary>
/// A VM that can load + persist its grid's per-grid-type
/// column widths. Implemented by <see cref="TextSizeConfigurableWidgetViewModel"/>
/// (the Reports table widgets) and by the Reports sidebar VM, so a view's
/// code-behind can drive column-width persistence uniformly via
/// <c>DataGridColumnWidthBinder</c> without reaching the settings repository
/// directly (the only width-persistence pattern in the app — code-behind -&gt; VM
/// -&gt; settings).
/// </summary>
public interface IColumnWidthStore
{
    /// <summary>Read the saved column widths for one grid type (null when unset).</summary>
    /// <param name="gridKey">Grid-type key (e.g. <c>"ore"</c>, <c>"sessions"</c>, <c>"compare.rates"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Column-index -&gt; pixel width, or null.</returns>
    Task<IReadOnlyDictionary<int, double>?> LoadColumnWidthsAsync(string gridKey, CancellationToken ct = default);

    /// <summary>Persist the resized column widths for one grid type. Fire-and-forget; failures are logged, not surfaced.</summary>
    /// <param name="gridKey">Grid-type key.</param>
    /// <param name="widths">Column-index -&gt; pixel width map (only the resized columns).</param>
    void SaveColumnWidths(string gridKey, IReadOnlyDictionary<int, double> widths);
}
