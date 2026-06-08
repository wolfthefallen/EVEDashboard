namespace EveDashboard.App.Wpf.Layout;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 19 single source of truth for the dashboard layout: the ordered,
/// observable collection of <see cref="WidgetItem"/>s rendered by
/// <c>MainWindow.xaml</c>'s unified <c>WrapPanel</c> flow, plus the
/// hidden-flag flip for the chrome's <c>x</c> button + <c>Hidden widgets ▾</c>
/// popup, plus persistence of order + hidden state to
/// <see cref="ISettingsRepository.SetWidgetLayoutAsync"/>. Plan 24 split the
/// singleton Session widget into a SessionConfig widget + a SessionStats
/// widget; the post-plan-25 follow-up moved SessionConfig to a modal dialog
/// (see <c>SessionConfigDialog</c>) so the dashboard layout now owns only
/// <see cref="WidgetDescriptor.SessionStats"/> and <see cref="WidgetDescriptor.Pilot"/>
/// kinds. SessionStats renderability is driven by the latching
/// <c>_statsActivated</c> flag (set on first
/// <see cref="SessionController.Mode"/> transition into Running or Paused).
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: constructed by DI as a singleton.
/// <see cref="LoadAsync(CancellationToken)"/> is called once from
/// <c>App.xaml.cs</c>'s bootstrap chain after the <see cref="ISettingsRepository"/>
/// is available and before <c>MainWindow.Show</c>. The coordinator subscribes
/// to <see cref="PerPilotWidgetCoordinator.Widgets"/>' <c>CollectionChanged</c>
/// at construction so a session start (which clears + repopulates that
/// collection) triggers a rebuild of <see cref="Items"/> against the persisted
/// order. Plan 24 added a third subscription — <c>SessionController.PropertyChanged</c>
/// for <c>Mode</c> — so transitions between Config / Running / Paused /
/// Stopped re-evaluate which Session-area descriptor renders. The
/// <c>_statsActivated</c> flag latches true on the first observed
/// <see cref="SessionWidgetMode.Running"/> or
/// <see cref="SessionWidgetMode.Paused"/> transition (covers both in-session
/// Start AND app-launch-with-open-session-row); once true, latches for the
/// app run (no across-restart Stats persistence — that's a v2 reporting
/// feature).
/// </para>
/// <para>
/// Persistence shape: the JSON written to the <c>setting</c> table under key
/// <c>widget_layout</c> contains the FULL persisted order — including
/// "ghost" entries for pilots not currently in any active session (per
/// decision 19.C they are preserved indefinitely so a pilot dragged to
/// slot N reappears at slot N when re-selected). <see cref="Items"/>
/// materialises only the renderable subset (Session + currently-active
/// pilots).
/// </para>
/// <para>
/// Save debouncing mirrors <see cref="EveDashboard.App.Wpf.Geometry.WindowGeometryService"/>'s
/// 500 ms <c>Task.Delay</c>+CTS pattern (the plan referenced
/// <see cref="System.Windows.Threading.DispatcherTimer"/> but the
/// WindowGeometryService precedent is Task.Delay; matching it keeps tests
/// dispatcher-free).
/// </para>
/// <para>
/// Per <c>MEMORY.md → feedback_chrome_bar_buttons_only</c>: the coordinator
/// owns no chrome status text, only data. The chrome row in
/// <c>MainWindow.xaml</c> binds to <see cref="HasHiddenWidgets"/> to
/// conditionally show the popup-trigger button — that visibility flip is
/// the only chrome-bar surface this coordinator drives.
/// </para>
/// </remarks>
public sealed partial class LayoutCoordinator : ObservableObject, IDisposable
{
    /// <summary>Default debounce window before a layout change is persisted; mirrors the WindowGeometryService precedent.</summary>
    public static readonly TimeSpan DefaultSaveDebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly IActiveSessionContext _activeSession;
    private readonly PerPilotWidgetCoordinator _perPilots;
    private readonly SessionController _sessionController;
    private readonly SessionStatsViewModel _sessionStatsVm;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<LayoutCoordinator> _log;

    /// <summary>The full persisted order, including ghost entries for inactive pilots. Source of truth for save.</summary>
    private readonly List<WidgetLayoutItem> _persistedItems = new();

    /// <summary>Per-WidgetItem PropertyChanged handlers; key=item, value=handler. Used to unsubscribe on item removal.</summary>
    private readonly Dictionary<WidgetItem, PropertyChangedEventHandler> _itemPropChangedHandlers = new();

    /// <summary>Per-WidgetBaseViewModel HideRequested handlers; key=descriptor, value=lambda. Used to unsubscribe on item removal / dispose.</summary>
    private readonly Dictionary<WidgetDescriptor, Action> _hideHandlers = new();

    private CancellationTokenSource? _saveDebounceCts;
    private Task? _inFlightSave;
    /// <summary>Serialises every <see cref="SaveAsync"/> call so a slow in-flight debounced save can never overwrite a freshly-flushed snapshot. See Bug 1 (2026-05-04) in <c>plans/BUGS.md</c>.</summary>
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private TimeSpan _saveDebounceInterval = DefaultSaveDebounceInterval;
    private bool _suppressSave;
    private bool _disposed;

    /// <summary>
    /// Plan 24 lifecycle latch — true once the SessionController's Mode has
    /// been observed at <see cref="SessionWidgetMode.Running"/> OR
    /// <see cref="SessionWidgetMode.Paused"/> at any point in this app run.
    /// Stays true for the rest of the app run (no across-restart Stats
    /// persistence — that's a v2 reporting feature). Gates whether the
    /// <see cref="WidgetDescriptor.SessionStats"/> descriptor renders into
    /// <see cref="Items"/>.
    /// </summary>
    private bool _statsActivated;

    /// <summary>Ordered collection of renderable widgets bound by <c>MainWindow.xaml</c>'s <c>ItemsControl</c>.</summary>
    public ObservableCollection<WidgetItem> Items { get; } = new();

    /// <summary>True iff at least one item in <see cref="Items"/> currently has <see cref="WidgetItem.IsHidden"/> == true. Drives the chrome popup-trigger button visibility.</summary>
    public bool HasHiddenWidgets => Items.Any(i => i.IsHidden);

    /// <summary>The hidden subset of <see cref="Items"/>, evaluated lazily. Bound by the chrome popup's <c>ListBox</c>.</summary>
    public IEnumerable<WidgetItem> HiddenItems => Items.Where(i => i.IsHidden);

    /// <summary>Singleton SessionController proxy — exposed so <see cref="MainWindowViewModel"/>'s chrome-bar bindings (Start/Pause/Resume/Stop/NewSession) keep working without owning a separate reference.</summary>
    public SessionController SessionViewModel => _sessionController;

    /// <summary>Construct a layout coordinator. Subscribes to <see cref="PerPilotWidgetCoordinator.Widgets"/>' <c>CollectionChanged</c>, to <see cref="Items"/>' own <c>CollectionChanged</c>, and to <paramref name="sessionController"/>'s <c>PropertyChanged</c> at construction.</summary>
    /// <param name="activeSession">Active-session context. Reserved for future use — currently the per-pilot coordinator's collection-change events are sufficient.</param>
    /// <param name="perPilots">Per-pilot widget coordinator. Its <see cref="PerPilotWidgetCoordinator.Widgets"/> collection drives renderable-subset merging.</param>
    /// <param name="sessionController">The singleton session controller — drives <see cref="WidgetDescriptor.SessionStats"/> visibility through its Mode property (the SessionConfig widget moved to a modal dialog post-plan-25).</param>
    /// <param name="sessionStatsVm">Thin VM for the SessionStats widget (forwards stat fields + DisplaySessionName + TotalTimeMined from the controller).</param>
    /// <param name="settings">Settings repository for layout load/save.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public LayoutCoordinator(
        IActiveSessionContext activeSession,
        PerPilotWidgetCoordinator perPilots,
        SessionController sessionController,
        SessionStatsViewModel sessionStatsVm,
        ISettingsRepository settings,
        ILogger<LayoutCoordinator> log)
    {
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(perPilots);
        ArgumentNullException.ThrowIfNull(sessionController);
        ArgumentNullException.ThrowIfNull(sessionStatsVm);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _activeSession = activeSession;
        _perPilots = perPilots;
        _sessionController = sessionController;
        _sessionStatsVm = sessionStatsVm;
        _settings = settings;
        _log = log;

        Items.CollectionChanged += OnItemsCollectionChanged;
        _perPilots.Widgets.CollectionChanged += OnPerPilotsCollectionChanged;
        _sessionController.PropertyChanged += OnControllerPropertyChanged;
    }

    /// <summary>
    /// Read the persisted layout and materialise <see cref="Items"/>. Default
    /// layout when no saved layout exists is empty (SessionStats is added on
    /// first activation per plan 24's lifecycle rules, and per-pilot widgets
    /// are appended on first session start in
    /// <see cref="PerPilotWidgetCoordinator.Widgets"/>' natural alphabetical
    /// order). The post-plan-25 follow-up moved the SessionConfig widget to a
    /// modal dialog, so no SessionConfig seeding happens here anymore.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var saved = await _settings.GetWidgetLayoutAsync(ct).ConfigureAwait(true);
        _persistedItems.Clear();
        if (saved is not null)
        {
            _persistedItems.AddRange(saved.Items);
        }

        RebuildItems();
    }

    /// <summary>Move the item at <paramref name="from"/> to <paramref name="to"/>. Called by the GongSolutions drop handler. Drop-on-self is a no-op (per decision 19.E).</summary>
    /// <param name="from">Source index in <see cref="Items"/>.</param>
    /// <param name="to">Target index in <see cref="Items"/>.</param>
    public void MoveItem(int from, int to)
    {
        if (from < 0 || from >= Items.Count) return;
        if (to < 0 || to >= Items.Count) return;
        if (from == to) return;
        Items.Move(from, to);
    }

    /// <summary>Flip the matching <see cref="WidgetItem.IsHidden"/> to true. No-op if no matching item is found.</summary>
    /// <param name="descriptor">Identity of the widget to hide.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="descriptor"/> is null.</exception>
    public void Hide(WidgetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var item = Items.FirstOrDefault(i => i.Descriptor.Equals(descriptor));
        if (item is null) return;
        item.IsHidden = true;
    }

    /// <summary>Flip the matching <see cref="WidgetItem.IsHidden"/> back to false. No-op if no matching item is found.</summary>
    /// <param name="descriptor">Identity of the widget to show.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="descriptor"/> is null.</exception>
    public void Show(WidgetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var item = Items.FirstOrDefault(i => i.Descriptor.Equals(descriptor));
        if (item is null) return;
        item.IsHidden = false;
    }

    /// <summary>Unsubscribe events and cancel any in-flight debounced save. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Items.CollectionChanged -= OnItemsCollectionChanged; } catch { /* defensive */ }
        try { _perPilots.Widgets.CollectionChanged -= OnPerPilotsCollectionChanged; } catch { /* defensive */ }
        try { _sessionController.PropertyChanged -= OnControllerPropertyChanged; } catch { /* defensive */ }
        foreach (var item in Items)
        {
            UnsubscribeItem(item);
        }
        try { _saveDebounceCts?.Cancel(); _saveDebounceCts?.Dispose(); } catch { /* defensive */ }
    }

    // --- internal seams (visible to tests via InternalsVisibleTo) -------------------

    /// <summary>Test seam: override the debounce interval. Tests use a near-zero interval so saves complete promptly.</summary>
    /// <param name="interval">Replacement interval; must be non-negative.</param>
    internal void SetSaveDebounceIntervalForTests(TimeSpan interval) => _saveDebounceInterval = interval;

    /// <summary>Test seam: synchronously flush any pending debounced save. Returns immediately if none is in-flight.</summary>
    /// <remarks>
    /// Cancels the latest debounce CTS, then awaits the latest in-flight
    /// <see cref="DebouncedSaveAsync"/> Task before issuing the final
    /// <see cref="SaveAsync"/>. The await ensures a slow debounced save (one
    /// that woke up from <c>Task.Delay</c> just before cancellation
    /// arrived) finishes its <see cref="ISettingsRepository.SetWidgetLayoutAsync"/>
    /// write before this method's own write starts. The <c>_saveLock</c>
    /// SemaphoreSlim then serialises any save that is still running on a
    /// thread the await chain doesn't see (defence in depth — see Bug 1).
    /// </remarks>
    internal async Task FlushPendingSaveAsync()
    {
        var cts = _saveDebounceCts;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { /* race-safe */ }
        }
        var inFlight = _inFlightSave;
        if (inFlight is not null)
        {
            try { await inFlight.ConfigureAwait(false); }
            catch { /* DebouncedSaveAsync logs internally; we just need the in-flight write to finish before ours runs. */ }
        }
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>Test seam: snapshot the current persisted-order list (including ghosts).</summary>
    internal IReadOnlyList<WidgetLayoutItem> SnapshotPersistedItems() => _persistedItems.ToArray();

    // --- internal merge / event handlers --------------------------------------------

    private void OnPerPilotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildItems();

    /// <summary>
    /// Plan 24 lifecycle handler: latches <see cref="_statsActivated"/> on
    /// the first observed transition into <see cref="SessionWidgetMode.Running"/>
    /// or <see cref="SessionWidgetMode.Paused"/>, then triggers a rebuild
    /// so the SessionConfig / SessionStats descriptor renderability
    /// re-evaluates against the new Mode.
    /// </summary>
    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionController.Mode)) return;
        var mode = _sessionController.Mode;
        if (mode == SessionWidgetMode.Running || mode == SessionWidgetMode.Paused)
        {
            _statsActivated = true;
        }
        RebuildItems();
    }

    private void RebuildItems()
    {
        _suppressSave = true;
        try
        {
            // Tear down handlers on items being removed.
            foreach (var existing in Items)
            {
                UnsubscribeItem(existing);
            }
            Items.Clear();

            // Plan 24 first-activation rule: insert SessionStats at slot 0 of
            // _persistedItems on the first activation if no SessionStats entry
            // exists yet. Subsequent activations use the saved slot.
            if (_statsActivated && !_persistedItems.Any(i => i.Descriptor is WidgetDescriptor.SessionStats))
            {
                _persistedItems.Insert(0, new WidgetLayoutItem(new WidgetDescriptor.SessionStats(), Hidden: false));
            }

            // Snapshot live per-pilot widgets keyed by pilot_id.
            var liveByPilot = new Dictionary<long, PerPilotWidgetViewModel>();
            foreach (var w in _perPilots.Widgets)
            {
                liveByPilot[w.PilotId] = w;
            }

            // Walk the persisted order; materialise items for renderable descriptors.
            foreach (var pi in _persistedItems)
            {
                switch (pi.Descriptor)
                {
                    case WidgetDescriptor.SessionStats
                        when _statsActivated:
                        AddItem(new WidgetItem(pi.Descriptor, _sessionStatsVm, "Session Stats", pi.Hidden));
                        break;
                    case WidgetDescriptor.Pilot p
                        when liveByPilot.TryGetValue(p.PilotId, out var pilotVm):
                        AddItem(new WidgetItem(pi.Descriptor, pilotVm, pilotVm.PilotName, pi.Hidden));
                        liveByPilot.Remove(p.PilotId);
                        break;
                    default:
                        // Skip: ghost Pilot (pilot_id not in current per-pilot collection),
                        // OR SessionStats when !_statsActivated.
                        break;
                }
            }

            // Append any live pilots not yet in the persisted order (newly-active).
            // PerPilotWidgetCoordinator already enumerates alphabetically, so this preserves alpha order.
            foreach (var w in liveByPilot.Values)
            {
                var desc = new WidgetDescriptor.Pilot(w.PilotId);
                _persistedItems.Add(new WidgetLayoutItem(desc, Hidden: false));
                AddItem(new WidgetItem(desc, w, w.PilotName, isHidden: false));
            }
        }
        finally
        {
            _suppressSave = false;
        }

        OnPropertyChanged(nameof(HasHiddenWidgets));
        OnPropertyChanged(nameof(HiddenItems));

        // If we appended new pilots above OR inserted the first-activation
        // SessionStats entry, _persistedItems was mutated; persist.
        ScheduleSave();
    }

    private void AddItem(WidgetItem item)
    {
        Items.Add(item);
        SubscribeItem(item);
    }

    private void SubscribeItem(WidgetItem item)
    {
        // PropertyChanged on the item: react to IsHidden flips.
        var handler = new PropertyChangedEventHandler((_, e) => OnItemPropertyChanged(item, e));
        item.PropertyChanged += handler;
        _itemPropChangedHandlers[item] = handler;

        // HideRequested on the inner VM: translate to Hide(descriptor).
        var descriptorCopy = item.Descriptor;
        Action hideHandler = () => Hide(descriptorCopy);
        item.ViewModel.HideRequested += hideHandler;
        _hideHandlers[descriptorCopy] = hideHandler;
    }

    private void UnsubscribeItem(WidgetItem item)
    {
        if (_itemPropChangedHandlers.TryGetValue(item, out var pcHandler))
        {
            item.PropertyChanged -= pcHandler;
            _itemPropChangedHandlers.Remove(item);
        }
        if (_hideHandlers.TryGetValue(item.Descriptor, out var hHandler))
        {
            item.ViewModel.HideRequested -= hHandler;
            _hideHandlers.Remove(item.Descriptor);
        }
    }

    private void OnItemPropertyChanged(WidgetItem item, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WidgetItem.IsHidden)) return;
        // Sync the persisted entry's Hidden flag.
        for (int i = 0; i < _persistedItems.Count; i++)
        {
            if (_persistedItems[i].Descriptor.Equals(item.Descriptor))
            {
                _persistedItems[i] = _persistedItems[i] with { Hidden = item.IsHidden };
                break;
            }
        }
        OnPropertyChanged(nameof(HasHiddenWidgets));
        OnPropertyChanged(nameof(HiddenItems));
        ScheduleSave();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressSave) return;
        if (e.Action != NotifyCollectionChangedAction.Move) return;

        // The user (or the GongSolutions drop handler) moved an item. Mirror the move into _persistedItems
        // so the persisted order matches the live order. Because _persistedItems may include ghost entries
        // not in Items, we cannot index-mirror directly; we extract the moved descriptor + its target neighbour
        // descriptor and reorder _persistedItems accordingly.
        if (e.NewItems is null || e.NewItems.Count == 0) return;
        var moved = (WidgetItem)e.NewItems[0]!;

        // Find the existing entry in _persistedItems and remove it.
        WidgetLayoutItem? movedEntry = null;
        for (int i = 0; i < _persistedItems.Count; i++)
        {
            if (_persistedItems[i].Descriptor.Equals(moved.Descriptor))
            {
                movedEntry = _persistedItems[i];
                _persistedItems.RemoveAt(i);
                break;
            }
        }
        if (movedEntry is null)
        {
            _log.LogWarning("Items.Move fired for descriptor {Descriptor} not present in persisted order; ignoring.",
                moved.Descriptor);
            return;
        }

        // Determine new position relative to the live items neighbours, projected onto _persistedItems.
        // Simplest: walk Items left-to-right; for each item BEFORE the moved index, find its position in
        // _persistedItems and insert the moved entry just AFTER the previous-item position. If moved is at
        // index 0, insert at _persistedItems index 0.
        int newLiveIdx = e.NewStartingIndex;
        if (newLiveIdx <= 0)
        {
            _persistedItems.Insert(0, movedEntry);
            ScheduleSave();
            return;
        }
        var prevLive = Items[newLiveIdx - 1];
        int prevPersistedIdx = -1;
        for (int i = 0; i < _persistedItems.Count; i++)
        {
            if (_persistedItems[i].Descriptor.Equals(prevLive.Descriptor))
            {
                prevPersistedIdx = i;
                break;
            }
        }
        if (prevPersistedIdx < 0)
        {
            // Should not happen — the previous live item must be in _persistedItems too.
            _log.LogWarning("Items.Move could not locate prev-live descriptor {Descriptor} in persisted order; appending.",
                prevLive.Descriptor);
            _persistedItems.Add(movedEntry);
        }
        else
        {
            _persistedItems.Insert(prevPersistedIdx + 1, movedEntry);
        }
        ScheduleSave();
    }

    // --- save scheduling ------------------------------------------------------------

    private void ScheduleSave()
    {
        if (_suppressSave || _disposed) return;
        _saveDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        // Track the latest in-flight task so FlushPendingSaveAsync can await
        // it before issuing its own SaveAsync (Bug 1 fix).
        _inFlightSave = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_saveDebounceInterval, token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        try
        {
            await SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LayoutCoordinator debounced save failed.");
        }
    }

    private async Task SaveAsync()
    {
        if (_disposed) return;
        // Serialise concurrent SaveAsync calls so a slow in-flight debounced
        // save (one whose Task.Delay completed just before its CTS was
        // cancelled) cannot overwrite a freshly-flushed snapshot. See Bug 1
        // (2026-05-04) — without this, the test fixture's 1 ms debounce
        // exposed a ~20% race in `Hide_flips_IsHidden_and_HasHiddenWidgets_and_persists`.
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var snapshot = new WidgetLayout(_persistedItems.ToArray());
            await _settings.SetWidgetLayoutAsync(snapshot).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
