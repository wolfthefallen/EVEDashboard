namespace EveDashboard.App.Wpf.Reporting;

using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Reporting.Widgets;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Settings;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>Mode driving which content subtree renders in the Reports window's right pane.</summary>
public enum BreakdownContentMode
{
    /// <summary>One session selected — render existing label-value-pair widgets.</summary>
    Single,

    /// <summary>Two or more sessions selected — render compare-mode comparison tables.</summary>
    Compare,
}

/// <summary>Pair of (per-pilot-stats, per-pilot-ore) widgets for one pilot in the saved session — single mode.</summary>
/// <param name="Pilot">Saved-data per-pilot stats VM.</param>
/// <param name="Ore">Saved-data per-pilot per-ore breakdown VM.</param>
public sealed record PerPilotEntry(
    SavedPerPilotWidgetViewModel Pilot,
    PerOreBreakdownViewModel     Ore);

/// <summary>
/// Root content VM for the Reports window's right pane. Drives single-mode
/// (1 selected session) or compare-mode (2+ selected sessions) rendering via
/// the <see cref="Mode"/> property; the view toggles which content subtree
/// is visible. Loads data on every selection change via <see cref="LoadAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Single mode mirrors the original Plan-25 flow: build
/// <see cref="SavedSessionStatsViewModel"/> + session-level
/// <see cref="PerOreBreakdownViewModel"/> + per-pilot
/// <see cref="PerPilotEntry"/> instances. Compare mode (2+ sessions, Plan 36)
/// instead builds a single <see cref="CompareSessionViewModel"/> with two
/// inner tables (Cumulative + Rates) — per-pilot widgets and per-ore tables
/// are intentionally dropped in compare mode.
/// </para>
/// <para>
/// Disposes prior widgets before rebuilding so settings invocation lists do
/// not retain dead VMs across selection switches — applies to both the
/// stat-configurable single-mode VMs and the <see cref="IDisposable"/>
/// compare-mode VM.
/// </para>
/// </remarks>
public sealed partial class BreakdownContentViewModel : ObservableObject
{
    private readonly ISessionStatsRepository _statsRepo;
    private readonly IReportingQueries _queries;
    private readonly ISettingsRepository _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BreakdownContentViewModel> _log;

    // ---- Mode + single-mode bound properties ----------------------------

    /// <summary>Which content subtree is visible. The XAML toggles Visibility via a converter binding.</summary>
    [ObservableProperty]
    private BreakdownContentMode _mode = BreakdownContentMode.Single;

    /// <summary>Single-mode session-stats VM, or null when compare mode / empty selection.</summary>
    [ObservableProperty]
    private SavedSessionStatsViewModel? _sessionStats;

    /// <summary>Single-mode session-level per-ore VM, or null when compare mode / empty selection.</summary>
    [ObservableProperty]
    private PerOreBreakdownViewModel? _sessionOreBreakdown;

    /// <summary>Single-mode per-pilot stack — one entry per pilot in the saved session.</summary>
    [ObservableProperty]
    private IReadOnlyList<PerPilotEntry> _perPilotEntries = Array.Empty<PerPilotEntry>();

    // ---- Compare-mode bound properties ----------------------------------

    /// <summary>Plan 36: the single compare-mode widget VM, or null when single mode / empty selection.</summary>
    [ObservableProperty]
    private CompareSessionViewModel? _compareSession;

    // ---- Shared chrome --------------------------------------------------

    /// <summary>
    /// True while a <see cref="LoadAsync"/> chain is running. Plan 34: bound
    /// by the Reports right pane to swap the breakdown widgets for the
    /// sprite-atlas loading spinner. Stays <c>true</c> across rapid bursts of
    /// queued loads — only the last-queued task clears it (see the
    /// <c>ContinueWith</c> in <see cref="LoadAsync"/>).
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Plan 34 — head of the serialization chain of in-flight load tasks.
    /// Each call to <see cref="LoadAsync"/> appends a new continuation onto
    /// this task; the new continuation only runs after the previous one
    /// completes. Rapid checkbox toggles thus run in arrival order without
    /// racing on the bound <see cref="ObservableObject"/> properties or
    /// <c>DisposePriorWidgets</c>.
    /// </summary>
    private Task _loadChain = Task.CompletedTask;

    /// <summary>
    /// Plan 34 (cancel-in-flight) — CTS for whatever load is currently active
    /// (in-flight or about to start). Each new <see cref="LoadAsync"/> call
    /// cancels this CTS and replaces it with a fresh one. Cancelled loads
    /// abort at their next <c>await</c> via <see cref="OperationCanceledException"/>;
    /// transient IDisposable widget VMs constructed inside the cancelled
    /// <c>Task.Run</c> lambda are disposed in the lambda's catch block before
    /// the exception propagates, so settings-event subscriptions never leak.
    /// </summary>
    private CancellationTokenSource? _activeCts;

    /// <summary>Construct the breakdown content VM.</summary>
    /// <param name="statsRepo">Session-stats repository.</param>
    /// <param name="queries">Reporting queries facade.</param>
    /// <param name="settings">Settings repository (passed through to widget VMs).</param>
    /// <param name="loggerFactory">Logger factory used to construct per-widget <see cref="ILogger{T}"/>s.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public BreakdownContentViewModel(
        ISessionStatsRepository statsRepo,
        IReportingQueries queries,
        ISettingsRepository settings,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(statsRepo);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _statsRepo = statsRepo;
        _queries = queries;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<BreakdownContentViewModel>();
    }

    /// <summary>
    /// Load the breakdown for the supplied selection of sessions, in selection
    /// (click) order. Disposes prior widgets, picks single-mode or compare-
    /// mode based on the count, and publishes new VM instances to the bound
    /// properties. Plan 34: flips <see cref="IsLoading"/> on synchronously
    /// before any work, yields to the dispatcher so the spinner paints first,
    /// then chains onto any in-flight load so concurrent calls run in arrival
    /// order without racing on bound properties. Only the LAST-queued call
    /// clears <see cref="IsLoading"/> so the spinner stays visible across a
    /// burst of rapid checkbox toggles.
    /// </summary>
    /// <param name="sessionsInOrder">Selected sidebar rows, ordered by user click sequence. Empty list clears the view; 1 entry = single mode; 2+ entries = compare mode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sessionsInOrder"/> is null.</exception>
    public Task LoadAsync(IReadOnlyList<SessionSummaryRow> sessionsInOrder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionsInOrder);

        // Cancel-in-flight: latest click wins. Any load currently running aborts
        // at its next await with OperationCanceledException; transient VMs it
        // built are disposed inside its Task.Run lambda's catch, so settings
        // subscriptions never leak.
        _activeCts?.Cancel();
        var newCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeCts = newCts;

        // Flip spinner on synchronously so the next render pass paints it
        // (and the just-toggled checkbox) before any work starts.
        IsLoading = true;

        // Chain onto the previous load so concurrent invocations run in
        // arrival order. Even with cancellation, we keep the chain so newly
        // arriving work always sees a consistent VM state (the cancelled
        // load's catch finishes before the new one starts).
        var previous = _loadChain;
        var thisLoad = RunChainedAsync(previous, sessionsInOrder, newCts.Token);
        _loadChain = thisLoad;

        // Only the last-queued task clears IsLoading. Mid-chain (cancelled)
        // loads leave it true so the spinner stays visible across the burst.
        // The continuation runs on the WPF dispatcher because LoadAsync is
        // only ever called from the UI thread (via SessionsSidebarViewModel.SelectionChanged).
        // CTS lifecycle: if _activeCts still references us when we complete,
        // null it BEFORE disposing — otherwise the next LoadAsync's
        // _activeCts?.Cancel() would call Cancel() on a disposed CTS and
        // throw ObjectDisposedException. If a newer load already replaced
        // _activeCts, leave it alone (it points at the newer CTS).
        _ = thisLoad.ContinueWith(_ =>
        {
            if (ReferenceEquals(_loadChain, thisLoad))
            {
                IsLoading = false;
            }
            if (ReferenceEquals(_activeCts, newCts))
            {
                _activeCts = null;
            }
            newCts.Dispose();
        }, TaskScheduler.FromCurrentSynchronizationContext());

        return thisLoad;
    }

    private async Task RunChainedAsync(
        Task previous,
        IReadOnlyList<SessionSummaryRow> sessionsInOrder,
        CancellationToken ct)
    {
        // Yield at Background priority (4) so the dispatcher processes the
        // pending Render pass (priority 7) BEFORE this continuation. The
        // render pass paints the just-toggled checkbox and the spinner.
        // Task.Yield posts at Normal (9), which is HIGHER than Render — that
        // would run us before the paint and defeat the whole purpose.
        await Dispatcher.Yield(DispatcherPriority.Background);

        // Wait for any prior load to settle before mutating shared state.
        // Swallow any exception from the prior chain (it logged its own).
        try { await previous.ConfigureAwait(true); }
        catch { /* prior load's exception was already logged in its own catch */ }

        // If we were cancelled while waiting in line, bail before touching state.
        if (ct.IsCancellationRequested) return;

        try
        {
            DisposePriorWidgets();

            if (sessionsInOrder.Count == 0)
            {
                Mode = BreakdownContentMode.Single;
                return;
            }

            if (sessionsInOrder.Count == 1)
            {
                Mode = BreakdownContentMode.Single;
                await LoadSingleAsync(sessionsInOrder[0], ct).ConfigureAwait(true);
            }
            else
            {
                Mode = BreakdownContentMode.Compare;
                await LoadCompareAsync(sessionsInOrder, ct).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled — transient VMs already disposed inside the Task.Run
            // lambda's catch (no settings subscriptions leaked). Nothing to log.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "BreakdownContentViewModel.LoadAsync failed for {Count} session(s).", sessionsInOrder.Count);
        }
    }

    private async Task LoadSingleAsync(SessionSummaryRow row, CancellationToken ct)
    {
        // Plan 34 bug 2: SQLite's "async" calls sync-complete and block the UI
        // thread. Push the entire fetch + VM-build phase onto the thread pool
        // via Task.Run so the dispatcher stays free and the sprite-atlas
        // spinner keeps animating throughout the load. We ConfigureAwait(true)
        // when awaiting the Run so the FINAL property assignments below come
        // back to the UI thread (those assignments raise PropertyChanged
        // against bound properties, which must be on the dispatcher).
        //
        // Cancel-in-flight (2026-05-17): if `ct` fires mid-load, the next
        // `await` throws OperationCanceledException. Transient IDisposable VMs
        // we've already constructed (which subscribed to ISettingsRepository
        // events) MUST be disposed before that exception escapes — otherwise
        // their event subscriptions leak. The `transient` list tracks them;
        // the catch block disposes everything and rethrows. On success we
        // Clear() the list because ownership transfers to the published
        // ObservableProperty values (their disposal happens in the next
        // DisposePriorWidgets call).
        var sessionId = row.SessionId;
        var (sessionStatsVm, sessionOreVm, entries) = await Task.Run(
            async () =>
            {
                var transient = new List<IDisposable>();
                try
                {
                    var snapshot = await _statsRepo
                        .GetSessionStatsAsync(sessionId, DateTimeOffset.UtcNow, allowLazyFetch: false, ct)
                        .ConfigureAwait(false);

                    var pilots = await _queries.GetPilotsForSessionAsync(sessionId, ct).ConfigureAwait(false);

                    var statsVm = new SavedSessionStatsViewModel(
                        sessionId, _settings, _loggerFactory.CreateLogger<SavedSessionStatsViewModel>());
                    transient.Add(statsVm);
                    statsVm.ApplyStats(snapshot);
                    statsVm.SetSessionName(row.Name);

                    var oreVm = new PerOreBreakdownViewModel(
                        _queries, _loggerFactory.CreateLogger<PerOreBreakdownViewModel>())
                    {
                        Title = "Session ore breakdown",
                    };
                    // PerOreBreakdownViewModel is not IDisposable (no settings subscription) — no cleanup needed.
                    await oreVm.LoadAsync(sessionId, pilotId: null, ct).ConfigureAwait(false);

                    var built = new List<PerPilotEntry>(pilots.Count);
                    foreach (var pilot in pilots)
                    {
                        var pilotVm = new SavedPerPilotWidgetViewModel(
                            sessionId, pilot.PilotId, pilot.PilotName,
                            _settings,
                            _loggerFactory.CreateLogger<SavedPerPilotWidgetViewModel>());
                        transient.Add(pilotVm);
                        if (snapshot.ByCharacter.TryGetValue(pilot.PilotId, out var charStats))
                        {
                            pilotVm.ApplyStats(charStats);
                        }

                        var pilotOreVm = new PerOreBreakdownViewModel(
                            _queries, _loggerFactory.CreateLogger<PerOreBreakdownViewModel>())
                        {
                            Title = $"{pilot.PilotName} ore breakdown",
                        };
                        await pilotOreVm.LoadAsync(sessionId, pilot.PilotId, ct).ConfigureAwait(false);

                        built.Add(new PerPilotEntry(pilotVm, pilotOreVm));
                    }

                    // Success — ownership transfers to the caller's publish step;
                    // do NOT dispose in the catch.
                    transient.Clear();
                    return (statsVm, oreVm, (IReadOnlyList<PerPilotEntry>)built);
                }
                catch
                {
                    // Cancellation or any other failure — dispose every VM we
                    // built so its settings subscription doesn't leak.
                    foreach (var d in transient)
                    {
                        try { d.Dispose(); } catch { /* defensive */ }
                    }
                    throw;
                }
            },
            ct).ConfigureAwait(true);

        // Cancel observed between Task.Run completion and this point: dispose
        // the VMs we built (their settings subscriptions would otherwise leak)
        // and bail without publishing.
        if (ct.IsCancellationRequested)
        {
            try { sessionStatsVm.Dispose(); } catch { /* defensive */ }
            foreach (var e in entries)
            {
                try { e.Pilot.Dispose(); } catch { /* defensive */ }
            }
            return;
        }

        // Plan 34 (revised 2026-05-17): hide the spinner BEFORE publishing
        // the widget VMs. Without this, the spinner stays visible through the
        // heavy widget render pass (5-session compare = 15-20 DataGrids
        // worth of layout + render) — and because that render pass saturates
        // the render thread, the spinner's render-thread animation halts
        // mid-rotation for 2-5 seconds, then disappears. With the spinner
        // hidden first, the user sees a brief empty pane during the render
        // ("loaded; rendering now") instead of a frozen spinner ("did it
        // crash?"). Dispatcher.Yield(Background) lets WPF do a render pass
        // for the spinner-hide BEFORE we touch the bound widget properties.
        IsLoading = false;
        await Dispatcher.Yield(DispatcherPriority.Background);

        // Back on the UI thread — safe to publish bound properties.
        SessionStats = sessionStatsVm;
        SessionOreBreakdown = sessionOreVm;
        PerPilotEntries = entries;
    }

    private async Task LoadCompareAsync(IReadOnlyList<SessionSummaryRow> sessions, CancellationToken ct)
    {
        // Plan 36: compare mode now needs only per-session SessionStats; per-pilot
        // and per-ore queries are dropped along with the widgets they fed. Same
        // dispatcher pattern as LoadSingleAsync — push the SQLite-blocking phase
        // onto the thread pool so the spinner keeps animating, ConfigureAwait(true)
        // back for the final bound-property assignment.
        var compareSessionVm = await Task.Run(
            async () =>
            {
                var transient = new List<IDisposable>();
                try
                {
                    var asOf = DateTimeOffset.UtcNow;
                    var sessionNames = sessions.Select(s => s.Name).ToArray();

                    var snapshots = await Task.WhenAll(
                        sessions.Select(s => _statsRepo.GetSessionStatsAsync(s.SessionId, asOf, allowLazyFetch: false, ct))
                    ).ConfigureAwait(false);

                    var vm = new CompareSessionViewModel(snapshots, sessionNames);
                    transient.Add(vm);

                    // Success — ownership transfers to caller's publish step.
                    transient.Clear();
                    return vm;
                }
                catch
                {
                    foreach (var d in transient)
                    {
                        try { d.Dispose(); } catch { /* defensive */ }
                    }
                    throw;
                }
            },
            ct).ConfigureAwait(true);

        // Cancel observed between Task.Run completion and this point: dispose the
        // VM we built and bail without publishing.
        if (ct.IsCancellationRequested)
        {
            try { compareSessionVm.Dispose(); } catch { /* defensive */ }
            return;
        }

        // See LoadSingleAsync for the rationale — hide the spinner BEFORE the
        // widget render pass so the user doesn't see it freeze.
        IsLoading = false;
        await Dispatcher.Yield(DispatcherPriority.Background);

        // Back on the UI thread — safe to publish bound property.
        CompareSession = compareSessionVm;
    }

    private void DisposePriorWidgets()
    {
        // Single-mode VMs (StatConfigurable, IDisposable).
        try { SessionStats?.Dispose(); } catch { /* defensive */ }
        foreach (var entry in PerPilotEntries)
        {
            try { entry.Pilot.Dispose(); } catch { /* defensive */ }
        }
        SessionStats = null;
        SessionOreBreakdown = null;
        PerPilotEntries = Array.Empty<PerPilotEntry>();

        // Plan 36: compare-mode subtree collapsed to a single VM.
        try { CompareSession?.Dispose(); } catch { /* defensive */ }
        CompareSession = null;
    }
}
