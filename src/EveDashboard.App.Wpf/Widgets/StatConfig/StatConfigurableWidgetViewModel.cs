namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.ComponentModel;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 25 base class for widget VMs that own a stat-config popup. Owns the
/// duplicated stat-config plumbing flagged in <c>CLEANUP.md</c> 2.2 — popup
/// wiring, settings subscription, <c>RebuildStatsAsync</c>, <c>FindHidden</c>,
/// and dispose orchestration. Concrete widgets provide the
/// <c>[StatRow]</c>-decorated properties, the data flow, and any extra
/// hide-only flags via <see cref="OnAdditionalHideOnlyResolved"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: the ctor wires the <see cref="StatRowsRenderer"/>, the
/// <see cref="StatConfigPopupViewModel"/>, and the
/// <see cref="ISettingsRepository.SettingChanged"/> subscription. The ctor does
/// NOT call <see cref="RebuildStatsAsync"/> — the renderer reads property
/// values via reflection, which would NRE on subclass fields not yet
/// initialized at base-ctor time. Subclasses MUST call <see cref="StartInitialRebuild"/>
/// from their own ctor after their fields are set.
/// </para>
/// <para>
/// Per-pilot widgets pass a setting key keyed by pilot id
/// (<c>per_pilot_stats_config.&lt;pilotId&gt;</c>); the singleton SessionStats
/// widget passes <c>session_stats_config</c>. The base filters
/// <see cref="ISettingsRepository.SettingChanged"/> by this key so other
/// pilots' settings + unrelated keys are ignored.
/// </para>
/// </remarks>
public abstract partial class StatConfigurableWidgetViewModel : WidgetBaseViewModel, IDisposable
{
    /// <summary>
    /// Plan 35 (decision 2.6 cross = B): protected so subclasses can subscribe
    /// to <see cref="ISettingsRepository.SettingChanged"/> for cross-cutting
    /// concerns (e.g. theme reactivity in <c>PerPilotWidgetViewModel</c>).
    /// </summary>
    protected ISettingsRepository Settings { get; }
    private readonly StatRowsRenderer _renderer;
    private readonly StatConfigPopupViewModel _popup;
    private readonly Type _vmType;
    private readonly string _settingKey;
    private readonly Func<CancellationToken, Task<WidgetStatsConfig?>> _load;
    private readonly ILogger? _log;
    private bool _disposed;

    /// <summary>Plan 20: bound stat rows in user-defined order. Filtered to visible rows only.</summary>
    public System.Collections.ObjectModel.ObservableCollection<StatRowViewModel> Stats => _renderer.Stats;

    /// <inheritdoc />
    public override bool HasStatConfig => true;

    /// <inheritdoc />
    public override StatConfigPopupViewModel? StatConfigPopup => _popup;

    /// <summary>Construct the base. Wires renderer + popup + settings subscription. Does NOT call <see cref="RebuildStatsAsync"/> — subclass MUST call <see cref="StartInitialRebuild"/> after its own init.</summary>
    /// <param name="vmType">Concrete widget-VM <see cref="Type"/>; reflection target for <see cref="StatRowsBuilder.Resolve"/> + the popup VM.</param>
    /// <param name="settings">Settings repository — read for stat-config persistence + change events.</param>
    /// <param name="settingKey">Setting-table key the subclass owns (e.g. <c>session_stats_config</c> or <c>per_pilot_stats_config.&lt;pilotId&gt;</c>). Used to filter <see cref="ISettingsRepository.SettingChanged"/>.</param>
    /// <param name="load">Delegate to read the saved <see cref="WidgetStatsConfig"/> (returns null when absent).</param>
    /// <param name="save">Delegate to persist a new <see cref="WidgetStatsConfig"/>.</param>
    /// <param name="log">Optional logger; passed to the popup VM's Apply-failure path.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    protected StatConfigurableWidgetViewModel(
        Type vmType,
        ISettingsRepository settings,
        string settingKey,
        Func<CancellationToken, Task<WidgetStatsConfig?>> load,
        Func<WidgetStatsConfig, CancellationToken, Task> save,
        ILogger? log)
    {
        ArgumentNullException.ThrowIfNull(vmType);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingKey);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(save);
        _vmType = vmType;
        Settings = settings;
        _settingKey = settingKey;
        _load = load;
        _log = log;

        _renderer = new StatRowsRenderer(this);
        _popup = new StatConfigPopupViewModel(vmType, load, save, log);
        _popup.PropertyChanged += OnPopupPropertyChanged;

        Settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>
    /// Subclass entry point to fire the initial <see cref="RebuildStatsAsync"/>
    /// AFTER the subclass's own fields are initialized. Intentional split from
    /// the base ctor — see remarks at the type level.
    /// </summary>
    protected void StartInitialRebuild()
    {
        _ = RebuildStatsAsync();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, _settingKey, StringComparison.Ordinal)) return;
        _ = RebuildStatsAsync();
    }

    private void OnPopupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatConfigPopupViewModel.ShouldClose)) return;
        if (_popup.ShouldClose) ClosePopupFromVm();
    }

    /// <summary>Re-read saved config + rebuild <see cref="Stats"/> + visibility flags. Test seam visible to widget-test projects via <c>InternalsVisibleTo</c>.</summary>
    /// <param name="ct">Cancellation token.</param>
    internal async Task RebuildStatsAsync(CancellationToken ct = default)
    {
        var saved = await _load(ct).ConfigureAwait(true);
        var resolution = StatRowsBuilder.Resolve(_vmType, saved, _log);
        _renderer.ApplyDescriptors(resolution.Reorderable);

        IsLastUpdatedVisible = !FindHidden(resolution.HideOnly, nameof(LastUpdated));
        OnAdditionalHideOnlyResolved(resolution.HideOnly);
    }

    /// <summary>Override to apply extra hide-only flags (e.g. per-pilot's <c>LastEventAt</c>). Default no-op.</summary>
    /// <param name="hideOnly">Hide-only descriptors from <see cref="StatRowsBuilder.Resolve"/>.</param>
    protected virtual void OnAdditionalHideOnlyResolved(IReadOnlyList<StatRowDescriptor> hideOnly)
    {
    }

    /// <summary>Find the <c>Hidden</c> flag for a hide-only descriptor by key. Returns false when the key is not in the list.</summary>
    /// <param name="hideOnly">Hide-only descriptors.</param>
    /// <param name="key">Stat row key to search for.</param>
    /// <returns>The descriptor's <c>Hidden</c> flag, or <c>false</c> when the key is absent.</returns>
    protected static bool FindHidden(IReadOnlyList<StatRowDescriptor> hideOnly, string key)
    {
        foreach (var d in hideOnly)
        {
            if (string.Equals(d.Key, key, StringComparison.Ordinal)) return d.Hidden;
        }
        return false;
    }

    /// <summary>Dispose: unsubscribe from settings + popup; dispose renderer + popup; invoke <see cref="DisposeCore"/> for subclass cleanup. Idempotent.</summary>
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Settings.SettingChanged -= OnSettingChanged; } catch { /* defensive */ }
        try { _popup.PropertyChanged -= OnPopupPropertyChanged; } catch { /* defensive */ }
        try { _popup.Dispose(); } catch { /* defensive */ }
        try { _renderer.Dispose(); } catch { /* defensive */ }
        DisposeCore();
    }

    /// <summary>Override to add subclass-specific cleanup. Default no-op. Called by <see cref="Dispose"/>.</summary>
    protected virtual void DisposeCore()
    {
    }
}
