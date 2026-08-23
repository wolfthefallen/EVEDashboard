namespace EveDashboard.App.Wpf.Widgets;

using System.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// Base class for the Reports table widgets (ore / refined / compare /
/// lifetime). Owns the per-element text-sizing plumbing — the "Text size" cog
/// popup, the resolved element-key -&gt; <see cref="StatEmphasis"/> map (exposed
/// via the <see cref="this[string]"/> indexer for XAML bindings), the
/// <see cref="ISettingsRepository.SettingChanged"/> subscription that re-resolves
/// live, and the <see cref="IDisposable"/> cleanup — plus thin column-width
/// persistence helpers the views' code-behind calls. One base so the
/// <see cref="ISettingsRepository"/> ctor injection is done once and shared by
/// both phase-1a (column widths) and phase-1b (text size).
/// </summary>
/// <remarks>
/// <para>
/// Text size is scoped per widget TYPE: every instance of a
/// given widget type shares one saved map under <c>reports_text_size.&lt;widgetTypeKey&gt;</c>.
/// Bind a sizeable element's <c>FontSize</c> to <c>{Binding [ElementKey], Converter={StaticResource EmphasisToFontSize}}</c>;
/// the converter inherits (no change) at <see cref="StatEmphasis.None"/>.
/// </para>
/// <para>
/// Column widths are likewise scoped per grid TYPE under
/// <c>reports_column_widths.&lt;gridKey&gt;</c>; the helpers here are thin
/// pass-throughs to <see cref="ISettingsRepository"/> so the code-behind never
/// reaches the repository directly.
/// </para>
/// </remarks>
public abstract partial class TextSizeConfigurableWidgetViewModel : WidgetBaseViewModel, IColumnWidthStore, IDisposable
{
    private static readonly IReadOnlyDictionary<string, StatEmphasis> EmptyMap = new Dictionary<string, StatEmphasis>();

    /// <summary>The settings repository. Protected so subclasses can read their own data if needed.</summary>
    protected ISettingsRepository Settings { get; }

    private readonly string _widgetTypeKey;
    private readonly string _textSizeKey;
    private readonly TextSizePopupViewModel _popup;
    private readonly IReadOnlyDictionary<string, StatEmphasis> _defaults;
    private readonly ILogger? _log;
    private IReadOnlyDictionary<string, StatEmphasis> _resolved = EmptyMap;
    private bool _disposed;

    /// <summary>
    /// Construct the base. Wires the text-size popup + the
    /// <see cref="ISettingsRepository.SettingChanged"/> subscription and kicks an
    /// initial resolve of the saved per-element emphasis.
    /// </summary>
    /// <param name="settings">Settings repository (read for text-size + column-width persistence and change events).</param>
    /// <param name="widgetTypeKey">Widget-type key (one of <c>"ore"</c>, <c>"refined"</c>, <c>"compare"</c>, <c>"lifetime"</c>).</param>
    /// <param name="elements">The configurable elements (stable key + user label + default emphasis), in display order.</param>
    /// <param name="log">Optional logger for resolve / save failures and the popup's Apply-failure path.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="settings"/>, <paramref name="widgetTypeKey"/>, or <paramref name="elements"/> is null.</exception>
    protected TextSizeConfigurableWidgetViewModel(
        ISettingsRepository settings,
        string widgetTypeKey,
        IReadOnlyList<(string Key, string Label, StatEmphasis Default)> elements,
        ILogger? log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(widgetTypeKey);
        ArgumentNullException.ThrowIfNull(elements);
        Settings = settings;
        _widgetTypeKey = widgetTypeKey;
        _textSizeKey = $"reports_text_size.{widgetTypeKey}";
        _log = log;

        var defaults = new Dictionary<string, StatEmphasis>(elements.Count);
        foreach (var el in elements)
        {
            defaults[el.Key] = el.Default;
        }
        _defaults = defaults;

        _popup = new TextSizePopupViewModel(
            elements,
            ct => settings.GetReportsTextSizeAsync(widgetTypeKey, ct),
            (map, ct) => settings.SetReportsTextSizeAsync(widgetTypeKey, map, ct),
            log);
        _popup.PropertyChanged += OnPopupPropertyChanged;
        Settings.SettingChanged += OnSettingChanged;

        _ = ResolveAsync();
    }

    /// <inheritdoc />
    public override bool HasTextSizeConfig => true;

    /// <inheritdoc />
    public override TextSizePopupViewModel? TextSizePopup => _popup;

    /// <summary>
    /// Resolved emphasis for one element key: the saved override if present, else the
    /// element's DEFAULT emphasis (the closest active-session look to its current
    /// styling), else <see cref="StatEmphasis.None"/> for an unknown key. Labels bind
    /// this to their chrome via <c>Tag="{Binding [Title]}" Style="{StaticResource ReportsEmphasisLabel}"</c>;
    /// the size-only tables bind <c>FontSize="{Binding [Table], Converter={StaticResource EmphasisToFontSize}}"</c>.
    /// </summary>
    /// <param name="key">Element key (e.g. <c>"Title"</c>, <c>"Table"</c>).</param>
    /// <returns>The element's effective emphasis (override, else default).</returns>
    public StatEmphasis this[string key] =>
        _resolved.TryGetValue(key, out var e)
            ? e
            : (_defaults.TryGetValue(key, out var d) ? d : StatEmphasis.None);

    /// <summary>Read the saved column widths for one grid type (null when unset). Thin pass-through used by the view's code-behind.</summary>
    /// <param name="gridKey">Grid-type key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Column-index -&gt; pixel width, or null.</returns>
    public Task<IReadOnlyDictionary<int, double>?> LoadColumnWidthsAsync(string gridKey, CancellationToken ct = default)
        => Settings.GetReportsColumnWidthsAsync(gridKey, ct);

    /// <summary>
    /// Persist the resized column widths for one grid type. Fire-and-forget (does
    /// not block the UI thread); failures are logged, not surfaced. Used by the
    /// view's code-behind on column resize / unload.
    /// </summary>
    /// <param name="gridKey">Grid-type key.</param>
    /// <param name="widths">Column-index -&gt; pixel width map (only the resized columns).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="gridKey"/> or <paramref name="widths"/> is null.</exception>
    public void SaveColumnWidths(string gridKey, IReadOnlyDictionary<int, double> widths)
    {
        ArgumentNullException.ThrowIfNull(gridKey);
        ArgumentNullException.ThrowIfNull(widths);
        _ = SaveColumnWidthsAsync(gridKey, widths);
    }

    private async Task SaveColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths)
    {
        try
        {
            await Settings.SetReportsColumnWidthsAsync(gridKey, widths).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to persist Reports column widths for grid '{Grid}'.", gridKey);
        }
    }

    private async Task ResolveAsync()
    {
        try
        {
            _resolved = await Settings.GetReportsTextSizeAsync(_widgetTypeKey).ConfigureAwait(true) ?? EmptyMap;
            // Refresh every indexer binding ({Binding [Key]}) at once. "Item[]" is
            // WPF's indexer-change sentinel (System.Windows.Data.Binding.IndexerName).
            OnPropertyChanged("Item[]");
        }
        catch (Exception ex)
        {
            // Fire-and-forget (ctor + OnSettingChanged) — without this guard a fault
            // would vanish on an unobserved Task and the widget would silently stop
            // reflecting text-size changes.
            _log?.LogWarning(ex, "Failed to resolve Reports text size for '{Type}'; keeping current sizes.", _widgetTypeKey);
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (string.Equals(e.Key, _textSizeKey, StringComparison.Ordinal))
        {
            _ = ResolveAsync();
        }
    }

    private void OnPopupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TextSizePopupViewModel.ShouldClose)) return;
        if (_popup.ShouldClose) CloseTextSizePopupFromVm();
    }

    /// <summary>Dispose: unsubscribe from settings + popup, dispose the popup, invoke <see cref="DisposeCore"/>. Idempotent.</summary>
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Settings.SettingChanged -= OnSettingChanged; } catch { /* defensive */ }
        try { _popup.PropertyChanged -= OnPopupPropertyChanged; } catch { /* defensive */ }
        try { _popup.Dispose(); } catch { /* defensive */ }
        DisposeCore();
    }

    /// <summary>Override to add subclass-specific cleanup. Default no-op. Called by <see cref="Dispose"/>.</summary>
    protected virtual void DisposeCore()
    {
    }
}
