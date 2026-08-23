namespace EveDashboard.App.Wpf.Widgets.Pilot;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.Stats.Events;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Owns the lifecycle of the per-pilot widget collection. Subscribes
/// to <see cref="IActiveSessionContext.ActiveSessionChanged"/> to populate
/// <see cref="Widgets"/> on session start, and to
/// <see cref="ISessionEventNotifier.SessionEventAdded"/> to flip a dirty flag
/// that the 1 Hz <see cref="DispatcherTimer"/> drains via
/// <see cref="ISessionStatsRepository.GetSessionStatsAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The widget collection is deliberately NOT cleared on
/// session Stop - widgets remain visible with their final stats until the next
/// session starts. On the next session start the collection is cleared and
/// repopulated from the new <c>session_pilot</c> set.
/// </para>
/// <para>
/// The <c>session_pilot</c> set is fixed
/// at session Start; mid-session pilot adds are not supported. This coordinator
/// enumerates pilots once at session-start time.
/// </para>
/// <para>
/// Threading: <see cref="IActiveSessionContext.ActiveSessionChanged"/> fires
/// synchronously on the calling thread - typically the WPF UI dispatcher in
/// production (the Session widget calls <c>ActiveSessionContext.Set</c> from
/// dispatcher-marshalled command handlers). The 1 Hz <see cref="DispatcherTimer"/>
/// is bound to <see cref="Application.Current"/>'s dispatcher (or
/// <see cref="Dispatcher.CurrentDispatcher"/> as fallback) per
/// <see cref="Session.SessionController"/>'s precedent so timer dispatch
/// is independent of the calling thread.
/// </para>
/// </remarks>
public sealed class PerPilotWidgetCoordinator : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly IActiveSessionContext _activeSession;
    private readonly ISqliteDatabase _db;
    private readonly ISessionStatsRepository _statsRepo;
    private readonly ISessionEventNotifier _notifier;
    private readonly ISettingsRepository _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PerPilotWidgetCoordinator> _log;

    /// <summary>
    /// Callback that forwards a role change from a per-pilot widget
    /// to <c>SessionController.SetPilotRoleAsync</c>. Wired via a DI factory in
    /// <c>ServiceRegistration.cs</c> — the factory
    /// resolves <c>SessionController</c> lazily inside the delegate so the
    /// coordinator and controller singletons don't form a DI cycle.
    /// </summary>
    private readonly Func<long, PilotRole, CancellationToken, Task> _setPilotRoleCallback;

    private DispatcherTimer? _tickTimer;
    private volatile bool _statsDirty;
    private string? _populatedForSessionId;
    private bool _disposed;

    /// <summary>One <see cref="PerPilotWidgetViewModel"/> per pilot in the active session. Populated by <see cref="OnActiveSessionChanged"/>.</summary>
    public ObservableCollection<PerPilotWidgetViewModel> Widgets { get; } = new();

    /// <summary>Construct a coordinator. Subscribes to events on construction; owns the timer for the app lifetime.</summary>
    /// <param name="activeSession">Read view of the active session id + run state.</param>
    /// <param name="db">Session SQLite wrapper. Used to enumerate <c>session_pilot</c> at session start.</param>
    /// <param name="statsRepo">Stats query layer; one call per dirty tick returns a session-wide snapshot.</param>
    /// <param name="notifier">Session-event notifier; flips the dirty flag on every new <c>log</c> row.</param>
    /// <param name="loggerFactory">Logger factory used to construct per-widget <see cref="ILogger{T}"/>s.</param>
    /// <param name="settings">Settings repository — passed through to each per-pilot widget VM so it can subscribe to its <c>per_pilot_stats_config.&lt;PilotId&gt;</c> key.</param>
    /// <param name="setPilotRoleCallback">Forwards role-change requests from per-pilot widgets to <c>SessionController.SetPilotRoleAsync</c>. Wired via DI factory so the coordinator and controller singletons don't form a DI cycle.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public PerPilotWidgetCoordinator(
        IActiveSessionContext activeSession,
        ISqliteDatabase db,
        ISessionStatsRepository statsRepo,
        ISessionEventNotifier notifier,
        ILoggerFactory loggerFactory,
        ISettingsRepository settings,
        Func<long, PilotRole, CancellationToken, Task> setPilotRoleCallback)
    {
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(statsRepo);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(setPilotRoleCallback);
        _activeSession = activeSession;
        _db = db;
        _statsRepo = statsRepo;
        _notifier = notifier;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _setPilotRoleCallback = setPilotRoleCallback;
        _log = loggerFactory.CreateLogger<PerPilotWidgetCoordinator>();

        _activeSession.ActiveSessionChanged += OnActiveSessionChanged;
        _notifier.SessionEventAdded += OnSessionEventAdded;
    }

    private async void OnActiveSessionChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(async () =>
            {
                try { await HandleActiveSessionChangedAsync().ConfigureAwait(true); }
                catch (Exception ex) { _log.LogWarning(ex, "PerPilotWidgetCoordinator failed to handle ActiveSessionChanged."); }
            });
            return;
        }
        try { await HandleActiveSessionChangedAsync().ConfigureAwait(true); }
        catch (Exception ex) { _log.LogWarning(ex, "PerPilotWidgetCoordinator failed to handle ActiveSessionChanged."); }
    }

    /// <summary>
    /// Test-seam for the session-change handler. Production calls this via
    /// the dispatcher-marshalled <see cref="OnActiveSessionChanged"/>; tests
    /// invoke it directly because xUnit has no message loop to drive
    /// <see cref="DispatcherTimer"/> or <c>Dispatcher.InvokeAsync</c>.
    /// Marked internal; visible to <c>EveDashboard.App.Wpf.Tests</c> via
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    internal async Task HandleActiveSessionChangedAsync()
    {
        var sid = _activeSession.ActiveSessionId;
        var state = _activeSession.State;

        // Clear-and-stop on transition to None.
        if (state == SessionRunState.None || sid is null)
        {
            StopTickTimer();
            // Widgets deliberately remain visible with final stats. Do NOT clear.
            return;
        }

        // Already populated for this session id - do not re-populate (e.g. on Pause/Resume which also fires the event).
        if (sid == _populatedForSessionId)
        {
            return;
        }

        // Transition to a new active session: dispose existing per-pilot VMs (each
        // subscribes to ISettingsRepository.SettingChanged — without
        // explicit Dispose the singleton repo's invocation list keeps the VMs
        // rooted indefinitely AND wrong-pilot rebuilds would still fire), then
        // clear and repopulate from session_pilot.
        foreach (var existing in Widgets)
        {
            try { existing.Dispose(); }
            catch (Exception ex) { _log.LogWarning(ex, "Per-pilot widget Dispose threw at session change; suppressed."); }
        }
        Widgets.Clear();
        _populatedForSessionId = sid;

        var rows = await _db.QueryAsync(
            EnumerateSessionPilotsSql,
            // Include sp.role so initialRole can flow to the new VM.
            r => (PilotId: r.GetInt64(0), Name: r.GetString(1), Role: r.GetString(2)),
            new Dictionary<string, object?> { ["$sid"] = sid });

        if (rows.Count == 0)
        {
            _log.LogWarning(
                "Session {SessionId} has no rows in session_pilot at activation time - per-pilot column will be empty.",
                sid);
        }

        foreach (var row in rows)
        {
            Widgets.Add(new PerPilotWidgetViewModel(
                row.PilotId,
                row.Name,
                // PilotRoleExtensions.Parse is strict-case against the four canonical
                // role strings; any other value throws (which would indicate corruption since
                // the DB has a CHECK constraint).
                PilotRoleExtensions.Parse(row.Role),
                _settings,
                _activeSession,
                _setPilotRoleCallback,
                _loggerFactory.CreateLogger<PerPilotWidgetViewModel>()));
        }

        // Force a first refresh so the just-created widgets show baseline stats (zeros for a fresh session;
        // real values when re-populating across a Resume after the writer has persisted prior events).
        _statsDirty = true;
        StartTickTimer();
    }

    private void OnSessionEventAdded(object? sender, SessionEventAddedEventArgs e) => _statsDirty = true;

    private void StartTickTimer()
    {
        _tickTimer?.Stop();
        var uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _tickTimer = new DispatcherTimer(
            TickInterval, DispatcherPriority.Normal, OnTick, uiDispatcher);
        _tickTimer.Start();
    }

    private void StopTickTimer()
    {
        if (_tickTimer is null) return;
        _tickTimer.Stop();
        _tickTimer.Tick -= OnTick;
        _tickTimer = null;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        try
        {
            await OnTickAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PerPilotWidgetCoordinator OnTick threw; suppressed to keep timer alive.");
        }
    }

    /// <summary>Test-seam for the dirty-tick refresh. Same rationale as <see cref="HandleActiveSessionChangedAsync"/>.</summary>
    internal async Task OnTickAsync()
    {
        if (!_statsDirty) return;
        if (_activeSession.ActiveSessionId is not { } sid) return;
        _statsDirty = false;
        try
        {
            var stats = await _statsRepo.GetSessionStatsAsync(sid, DateTimeOffset.UtcNow, allowLazyFetch: true).ConfigureAwait(true);
            foreach (var widget in Widgets)
            {
                if (stats.ByCharacter.TryGetValue(widget.PilotId, out var charStats))
                {
                    widget.ApplyStats(charStats);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PerPilotWidgetCoordinator stats refresh failed at tick; will retry next tick.");
            _statsDirty = true;
        }
    }

    /// <summary>Unsubscribe events, stop the tick timer, and dispose every per-pilot VM. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _activeSession.ActiveSessionChanged -= OnActiveSessionChanged; } catch { /* defensive */ }
        try { _notifier.SessionEventAdded -= OnSessionEventAdded; } catch { /* defensive */ }
        StopTickTimer();
        foreach (var w in Widgets)
        {
            try { w.Dispose(); } catch { /* defensive */ }
        }
    }

    // sp.role is selected so the coordinator can pass initialRole to the VM at
    // construction time (without this, the widget would briefly flash Miner before
    // the first stats snapshot updates Role).
    private const string EnumerateSessionPilotsSql = """
        SELECT sp.pilot_id, p.name, sp.role
        FROM session_pilot sp
        INNER JOIN pilot p ON p.pilot_id = sp.pilot_id
        WHERE sp.session_id = $sid
        ORDER BY p.name COLLATE NOCASE ASC
        """;
}
