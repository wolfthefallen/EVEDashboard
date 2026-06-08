namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// ViewModel behind the cog popup that lets the user reorder, hide, and
/// emphasise stats inside ONE widget body (plan 20). Constructed once per
/// widget VM and held as a field; the same type serves both per-pilot widgets
/// (load/save delegates capture the pilot id) and the SessionStats widget
/// (delegates have no extra arg).
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle inside one popup-open cycle:
/// <list type="number">
/// <item>Cog button click flips <c>WidgetBaseViewModel.IsStatConfigOpen</c>
///   to true; the chrome <c>Popup</c>'s <c>IsOpen</c> is two-way bound to
///   that flag and opens.</item>
/// <item><see cref="LoadAsync"/> reads the saved <see cref="WidgetStatsConfig"/>
///   via the load delegate, runs <see cref="StatRowsBuilder.Resolve"/> against
///   the widget VM's runtime type, and populates the editable working copies
///   <see cref="Reorderable"/> + <see cref="HideOnly"/>.</item>
/// <item>User drags rows / toggles checkboxes / picks emphasis levels — all
///   edits land on the working copy.</item>
/// <item>On Apply: project the working copy into a fresh
///   <see cref="WidgetStatsConfig"/>, persist via the save delegate, close
///   the popup. The settings repository's <c>SettingChanged</c> event then
///   fires; the widget VM rebuilds its <c>Stats</c> + visibility flags.</item>
/// <item>On Close or click-outside: working copy is discarded; saved config unchanged.</item>
/// </list>
/// </para>
/// <para>
/// On <see cref="ApplyAsync"/> failure (e.g. SQLite write error): logs Warning
/// and leaves the popup open at the user's edits. Mirrors the SettingsDialog
/// failure pattern.
/// </para>
/// </remarks>
public sealed partial class StatConfigPopupViewModel : ObservableObject, IDisposable
{
    private readonly Type _vmType;
    private readonly Func<CancellationToken, Task<WidgetStatsConfig?>> _load;
    private readonly Func<WidgetStatsConfig, CancellationToken, Task> _save;
    private readonly ILogger? _log;
    private bool _disposed;

    /// <summary>The user's working copy of reorderable rows (drag-drop reorders this collection in place).</summary>
    public ObservableCollection<StatConfigRowViewModel> Reorderable { get; } = new();

    /// <summary>The user's working copy of hide-only rows (only the Hidden flag is editable).</summary>
    public ObservableCollection<StatConfigRowViewModel> HideOnly { get; } = new();

    /// <summary>True after a successful Apply; the bound chrome <c>Popup.IsOpen</c> reads this through the widget VM's <c>IsStatConfigOpen</c> flag.</summary>
    [ObservableProperty]
    private bool _shouldClose;

    /// <summary>Construct a popup VM around a widget-VM type and the matching load/save delegates.</summary>
    /// <param name="vmType">Concrete widget-VM type. Reflected over for [StatRow] metadata.</param>
    /// <param name="load">Delegate that reads the saved config (returns null when absent).</param>
    /// <param name="save">Delegate that persists a new config.</param>
    /// <param name="log">Optional logger for the Apply-failure Warning line.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    public StatConfigPopupViewModel(
        Type vmType,
        Func<CancellationToken, Task<WidgetStatsConfig?>> load,
        Func<WidgetStatsConfig, CancellationToken, Task> save,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(vmType);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(save);
        _vmType = vmType;
        _load = load;
        _save = save;
        _log = log;
    }

    /// <summary>
    /// Read the saved config and populate <see cref="Reorderable"/> +
    /// <see cref="HideOnly"/> with editable working copies. Idempotent — call
    /// on every popup open so the user always sees the latest persisted state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var saved = await _load(ct).ConfigureAwait(true);
        var resolution = StatRowsBuilder.Resolve(_vmType, saved, _log);

        Reorderable.Clear();
        foreach (var d in resolution.Reorderable)
        {
            Reorderable.Add(new StatConfigRowViewModel(
                key: d.Key,
                label: d.Label,
                reorderable: true,
                hidden: d.Hidden,
                emphasis: d.Emphasis));
        }

        HideOnly.Clear();
        foreach (var d in resolution.HideOnly)
        {
            HideOnly.Add(new StatConfigRowViewModel(
                key: d.Key,
                label: d.Label,
                reorderable: false,
                hidden: d.Hidden,
                emphasis: d.Emphasis));
        }

        ShouldClose = false;
    }

    /// <summary>
    /// Project the working copies into a fresh <see cref="WidgetStatsConfig"/>
    /// and call the save delegate. On success, sets <see cref="ShouldClose"/>
    /// = true (the chrome popup observes via the widget VM's
    /// <c>IsStatConfigOpen</c> two-way binding and closes). On failure, logs
    /// Warning and leaves the popup open.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [RelayCommand]
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        // Reorderable order is the user's drag order. HideOnly order is fixed
        // by DefaultOrder — we still emit those entries so their Hidden flag
        // round-trips through the saved config.
        var entries = new List<StatEntry>(Reorderable.Count + HideOnly.Count);
        foreach (var row in Reorderable)
        {
            entries.Add(new StatEntry(row.Key, row.Hidden, row.Emphasis));
        }
        foreach (var row in HideOnly)
        {
            entries.Add(new StatEntry(row.Key, row.Hidden, row.Emphasis));
        }

        var newConfig = new WidgetStatsConfig(entries);
        try
        {
            await _save(newConfig, ct).ConfigureAwait(true);
            ShouldClose = true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex,
                "StatConfigPopup Apply failed for VM type {VmType}; leaving popup open at user's edits.",
                _vmType.Name);
            // Leave ShouldClose=false so the popup stays open.
        }
    }

    /// <summary>Discard working copy edits and close the popup.</summary>
    [RelayCommand]
    public void Close()
    {
        ShouldClose = true;
    }

    /// <summary>Dispose: idempotent. No external subscriptions to clear in this VM.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
