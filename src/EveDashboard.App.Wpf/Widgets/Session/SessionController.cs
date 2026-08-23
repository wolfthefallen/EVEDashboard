namespace EveDashboard.App.Wpf.Widgets.Session;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Realtime;
using EveDashboard.Stats.Events;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Writer;
using Microsoft.Extensions.Logging;

/// <summary>
/// Controller behind the SessionConfig + SessionStats widget pair.
/// Owns the four-mode state machine
/// (<see cref="SessionWidgetMode.Config"/> / <see cref="SessionWidgetMode.Running"/> /
/// <see cref="SessionWidgetMode.Paused"/> / <see cref="SessionWidgetMode.Stopped"/>),
/// the lifecycle SQL transitions (Start / Pause / Resume / Stop, each as one
/// transaction), the 1 Hz <see cref="DispatcherTimer"/> that drives both stats
/// refresh and elapsed-time tick, the dirty-flag pattern fed by
/// <see cref="ISessionEventNotifier.SessionEventAdded"/>, and all bound stat
/// fields. The pilot picker lives in the New Session dialog, not here.
/// The two thin VMs
/// <c>SessionConfigViewModel</c> and <c>SessionStatsViewModel</c> hold a
/// reference to this controller and forward the bound fields each view needs.
/// </summary>
/// <remarks>
/// <para>
/// Stats refresh is a single 1 Hz coalesced tick driven by a dirty flag, not
/// per-event updates; that one timer drives both stats refresh and the
/// elapsed-time tick, and runs only while Running. ISK fields are populated by
/// the ISK pricing pipeline that runs inside
/// <c>SqliteSessionStatsRepository.GetSessionStatsAsync</c>.
/// </para>
/// <para>
/// Lifecycle SQL transitions live here, not in the VMs. The widget
/// deliberately never auto-resumes at app launch — the user must click Resume
/// after <see cref="InitializeAsync"/> detects an open <c>session_time</c> row
/// (Mode lands in <see cref="SessionWidgetMode.Paused"/> for that
/// case; there is no separate "Resume" mode). Both <see cref="SessionEventWriter"/> and
/// <see cref="RealtimeLogWatcher"/> are started/stopped per-session; Pause
/// and Resume buttons leave both alone (Pause merely flips
/// <see cref="IActiveSessionContext"/> state to Paused, which the writer
/// reads on every <c>WriteAsync</c> to drop events).
/// </para>
/// <para>
/// App-launch-with-open-session-row shares the
/// <see cref="SessionWidgetMode.Paused"/> mode value: it
/// surfaces the same <c>[Resume, Stop]</c> chrome buttons as in-session
/// pause does, and the dispatch between the two paths happens in
/// <see cref="ResumeAsync"/> via the <c>_sessionStartUtc</c> null-check
/// (null = app-launch heavy path; set = in-session light path). A single
/// Stop command covers both <see cref="SessionWidgetMode.Running"/> and
/// <see cref="SessionWidgetMode.Paused"/> transitions to
/// <see cref="SessionWidgetMode.Stopped"/>; discarding a paused session is
/// Stop -> NewSession, two clicks not one.
/// </para>
/// </remarks>
public sealed partial class SessionController : WidgetBaseViewModel, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ISqliteDatabase _db;
    private readonly ISessionStatsRepository _statsRepo;
    private readonly ISessionEventNotifier _notifier;
    private readonly SessionEventWriter _writer;
    private readonly RealtimeLogWatcher _watcher;
    private readonly ActiveSessionContext _activeSession;
    private readonly IConfirmDialog _confirmStop;
    private readonly GamelogDiscoveryService _discovery;
    private readonly IReadOnlyList<Character> _allCharacters;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<SessionController> _log;

    private DispatcherTimer? _tickTimer;
    private volatile bool _statsDirty;

    private DateTimeOffset? _sessionStartUtc;
    private TimeSpan _pausedDurationAccumulated;
    private DateTimeOffset? _currentPauseStartUtc;
    private string? _activeSessionId;
    private bool _disposed;

    /// <summary>
    /// Wall-clock time of the most recent qualifying (Tick or Crit)
    /// event for the active session. Reset to <c>null</c> at Start / Stop;
    /// reset to <c>DateTimeOffset.UtcNow</c> when transitioning Paused -&gt;
    /// Running so the idle timer starts from the resume moment, not from the
    /// stale pre-pause baseline. Used by the 1 Hz tick to drive auto-pause.
    /// </summary>
    private DateTimeOffset? _lastQualifyingEventUtc;

    /// <summary>
    /// Cached auto-pause idle threshold in minutes. Hydrated from
    /// <see cref="ISettingsRepository.GetAutoPauseIdleMinutesAsync"/> in
    /// <see cref="InitializeAsync"/>; refreshed via the
    /// <see cref="ISettingsRepository.SettingChanged"/> hook so user edits
    /// in Settings take effect on the next 1 Hz tick.
    /// </summary>
    private int _autoPauseIdleMinutes = 5;

    /// <summary>
    /// Current visible mode of the controller. One of
    /// <see cref="SessionWidgetMode.Config"/>,
    /// <see cref="SessionWidgetMode.Running"/>,
    /// <see cref="SessionWidgetMode.Paused"/>, or
    /// <see cref="SessionWidgetMode.Stopped"/>. Drives chrome-bar button
    /// visibility and which Session-area widget is in
    /// <see cref="EveDashboard.App.Wpf.Layout.LayoutCoordinator.Items"/>
    /// (Config -> SessionConfig; Running/Paused/Stopped -> SessionStats).
    /// </summary>
    [ObservableProperty]
    private SessionWidgetMode _mode = SessionWidgetMode.Config;

    /// <summary>Raw user-typed session name (Config mode). Template variables expanded at Start.</summary>
    [ObservableProperty]
    private string _sessionName = string.Empty;

    /// <summary>Resolved (template-expanded) name shown in the SessionStats widget.</summary>
    [ObservableProperty]
    private string _displaySessionName = string.Empty;

    /// <summary>Live elapsed-time string (HH:mm:ss). Updated every tick in Running mode (frozen in Paused/Stopped).</summary>
    [ObservableProperty]
    private string _totalTimeMined = "00:00:00";

    /// <summary>Total m^3 mined this session (sum of <c>volume_m3</c> across <c>log</c>).</summary>
    [ObservableProperty]
    private double _totalM3Mined;

    /// <summary>m^3 per second over active duration. 0 if no active duration yet.</summary>
    [ObservableProperty]
    private double _m3PerSecond;

    /// <summary>Estimated session-wide ISK mined; sourced from the ISK pricing pipeline.</summary>
    [ObservableProperty]
    private double _totalIskMined;

    /// <summary>Estimated session-wide ISK per hour; sourced from the ISK pricing pipeline.</summary>
    [ObservableProperty]
    private double _iskPerHour;

    /// <summary>Total m^3 from crit events.</summary>
    [ObservableProperty]
    private double _totalM3FromCrits;

    /// <summary>Number of crit events.</summary>
    [ObservableProperty]
    private long _critCount;

    /// <summary>Crit chance fraction (crits / (crits + ticks)). 0 if no events yet.</summary>
    [ObservableProperty]
    private double _critChance;

    /// <summary><c>TotalIskFromCrits / TotalIskMined</c>; 0 when total ISK is 0 (divide-by-zero guard). Sourced from the snapshot.</summary>
    [ObservableProperty]
    private double _critIskPercent;

    /// <summary>Session-wide refined-material output (yield-applied, quantity desc). Sourced from the stats snapshot; drives the SessionStats widget's "Refined output" section.</summary>
    [ObservableProperty]
    private IReadOnlyList<RefinedMaterialAmount> _sessionRefinedOutput = Array.Empty<RefinedMaterialAmount>();

    /// <summary>Construct the ViewModel. All dependencies are required.</summary>
    /// <param name="db">Singleton session SQLite wrapper.</param>
    /// <param name="statsRepo">Session-stats query layer.</param>
    /// <param name="notifier">Session-event notifier (<see cref="SessionEventWriter"/> implements this).</param>
    /// <param name="writer">Session-event writer; per-session lifecycle managed here.</param>
    /// <param name="watcher">Realtime log watcher; per-session lifecycle managed here.</param>
    /// <param name="activeSession">Mutable active-session context (concrete type — only the widget mutates).</param>
    /// <param name="confirmStop">Stop-confirmation dialog seam.</param>
    /// <param name="discovery">Gamelog discovery service. Re-invoked at Start time to refresh
    ///     each selected pilot's <c>LogFilePath</c> against the live filesystem — handles the
    ///     case where EVE created a newer log file for a pilot between app launch (when the
    ///     <paramref name="allCharacters"/> list was captured) and the user clicking Start.</param>
    /// <param name="allCharacters">All EVE characters discovered at app startup. Used as the fallback
    ///     snapshot when Start-time discovery throws (the per-pilot picker now lives in the New Session dialog).</param>
    /// <param name="settings">Settings repository — used to read/refresh the auto-pause idle threshold.</param>
    /// <param name="log">Logger for lifecycle / SQL / refresh diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SessionController(
        ISqliteDatabase db,
        ISessionStatsRepository statsRepo,
        ISessionEventNotifier notifier,
        SessionEventWriter writer,
        RealtimeLogWatcher watcher,
        ActiveSessionContext activeSession,
        IConfirmDialog confirmStop,
        GamelogDiscoveryService discovery,
        IReadOnlyList<Character> allCharacters,
        ISettingsRepository settings,
        ILogger<SessionController> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(statsRepo);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(confirmStop);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(allCharacters);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);

        _db = db;
        _statsRepo = statsRepo;
        _notifier = notifier;
        _writer = writer;
        _watcher = watcher;
        _activeSession = activeSession;
        _confirmStop = confirmStop;
        _discovery = discovery;
        _allCharacters = allCharacters;
        _settings = settings;
        _log = log;

        _notifier.SessionEventAdded += OnSessionEventAdded;

        // Live refresh of the cached idle-minutes threshold on
        // Settings -> Apply.
        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>
    /// Raised when the controller auto-pauses the session on idle.
    /// MainWindow subscribes and plays <c>Miners_have_stopped.wav</c>.
    /// </summary>
    public event EventHandler? AutoPauseSoundRequested;

    /// <summary>
    /// Raised when the controller auto-resumes the session on a
    /// Tick/Crit arrival during pause. MainWindow subscribes and plays
    /// <c>Mining_detected.wav</c>.
    /// </summary>
    public event EventHandler? AutoResumeSoundRequested;

    /// <summary>
    /// Refresh the widget's bound state. The Session widget refreshes via the
    /// 1 Hz tick timer's dirty-flag pattern; this manual
    /// hook from <see cref="WidgetBaseViewModel"/> is provided for parity but
    /// just forces an immediate stats re-query when the widget is in Running
    /// mode.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public override async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_activeSessionId is not { } sid) return;
        try
        {
            var stats = await _statsRepo.GetSessionStatsAsync(sid, DateTimeOffset.UtcNow, allowLazyFetch: true, ct).ConfigureAwait(true);
            ApplyStatsSnapshot(stats);
            LastUpdated = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Manual RefreshAsync failed for session {SessionId}.", sid);
        }
    }

    /// <summary>
    /// Detect any open <c>session_time</c> row at startup. If present, enter
    /// <see cref="SessionWidgetMode.Paused"/> (frozen elapsed-time + chrome
    /// shows <c>Resume</c> + <c>Stop</c>); otherwise stay in
    /// <see cref="SessionWidgetMode.Config"/>. Hydrates the SessionStats
    /// widget with the prior session's name + frozen totals so the user
    /// sees what they're resuming. The <c>is_pause</c> flag of the open row
    /// is not surfaced here — <see cref="ResumeFromAppLaunchPausedAsync"/>
    /// re-queries it when the user clicks Resume.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Hydrate the auto-pause idle threshold once at startup. The
        // repo defaults to 5 / clamps to [1, 60] internally; we just cache.
        try
        {
            _autoPauseIdleMinutes = await _settings.GetAutoPauseIdleMinutesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read auto_pause_idle_minutes at startup; defaulting to 5.");
            _autoPauseIdleMinutes = 5;
        }

        var openRows = await _db.QueryAsync(
            "SELECT session_time_id, session_id, is_pause FROM session_time " +
            "WHERE stop_time IS NULL ORDER BY session_time_id DESC LIMIT 1",
            r => new { SessionTimeId = r.GetInt64(0), SessionId = r.GetString(1), IsPause = r.GetInt32(2) },
            null, ct);

        if (openRows.Count == 0)
        {
            Mode = SessionWidgetMode.Config;
            return;
        }

        var open = openRows[0];
        _activeSessionId = open.SessionId;

        // Hydrate display fields for the Paused-at-launch SessionStats render:
        // session name + frozen elapsed.
        var nameRows = await _db.QueryAsync(
            "SELECT name FROM session WHERE session_id = $sid LIMIT 1",
            r => r.GetString(0),
            new Dictionary<string, object?> { ["$sid"] = open.SessionId }, ct);
        DisplaySessionName = nameRows.Count > 0 ? nameRows[0] : string.Empty;

        var stats = await _statsRepo.GetSessionStatsAsync(open.SessionId, DateTimeOffset.UtcNow, allowLazyFetch: true, ct);
        TotalTimeMined = FormatElapsed(stats.TotalTimeMined);

        Mode = SessionWidgetMode.Paused;
        _log.LogInformation(
            "SessionController detected open session_time row session_id={SessionId} is_pause={IsPause}; entering Paused mode (app-launch path).",
            open.SessionId, open.IsPause);
    }

    /* ----- Start (Config -> Paused), driven by the New Session dialog VM ----- */

    /// <summary>
    /// Start a session from a fleet roster. Resolves the roster against
    /// a fresh gamelog-discovery snapshot, writes the session in one transaction
    /// with each pilot in the fleet's assigned role, and lands in
    /// <see cref="SessionWidgetMode.Paused"/> (Start-as-paused).
    /// </summary>
    /// <param name="roster">The selected fleet's members. Only <see cref="FleetMember.PilotId"/>
    ///     and <see cref="FleetMember.Role"/> are consumed — the freshly-discovered name wins.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="SessionStartResult.Started"/> on success (or when a session was
    /// already active — the defensive guard);
    /// <see cref="SessionStartResult.NoDiscoverablePilots"/> when none of the
    /// roster's pilots are discoverable, in which case nothing is written.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="roster"/> is null.</exception>
    public async Task<SessionStartResult> StartAsync(IReadOnlyList<FleetMember> roster, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roster);

        // Bug-1 defence-in-depth: if a session is already active, refuse to start
        // a second one. A session genuinely is active, so the dialog closing is
        // the correct outcome — report Started.
        if (_activeSessionId is not null)
        {
            _log.LogWarning(
                "SessionController.StartAsync invoked while session {SessionId} is already active; ignoring (defensive guard).",
                _activeSessionId);
            return SessionStartResult.Started;
        }

        var asOf = DateTimeOffset.UtcNow;

        // Front-load the discovery resolve, before any SQL. A fresh by-id
        // discovery snapshot (falling back to launch-time characters if
        // discovery throws) gives each resolved pilot its live LogFilePath, so
        // the watcher never tails a stale file. FleetRosterResolver pairs each
        // discoverable roster id with its fleet role and reports the rest as
        // skipped.
        var discoveredById = await ResolveDiscoveredByIdAsync(ct);
        var (resolved, skipped) = FleetRosterResolver.Resolve(roster, discoveredById);

        if (resolved.Count == 0)
        {
            _log.LogWarning(
                "Start blocked: none of the {Count} fleet pilot(s) are currently discoverable.",
                roster.Count);
            return SessionStartResult.NoDiscoverablePilots;
        }
        foreach (var skippedId in skipped)
        {
            _log.LogWarning(
                "Fleet pilot {PilotId} has no current gamelog; skipping from this session.",
                skippedId);
        }

        var resolvedCharacters = resolved.Select(r => r.Char).ToList();
        // The role map is the fleet's stored roles — no prior-session lookup.
        var roleByPilotId = resolved.ToDictionary(r => r.Char.CharacterId, r => r.Role);

        var expandedName = SessionNameTemplateExpander.Expand(SessionName, resolvedCharacters, asOf);
        var tsIso = ToSqliteIso(asOf);

        // session_id is the content-addressed sha256 of (name + 0x1F +
        // first start_time). Computed BEFORE the INSERT — no last_insert_rowid.
        // tsIso is the exact start_time persisted into the is_pause row below,
        // so the startup migration and import reproduce the identical id.
        var sessionId = SessionId.Compute(expandedName, tsIso);
        await _db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session (session_id, name, notes) VALUES ($sid, $name, NULL)",
                new Dictionary<string, object?> { ["$sid"] = sessionId, ["$name"] = expandedName });

            foreach (var pilot in resolvedCharacters)
            {
                await tx.ExecuteNonQueryAsync(
                    "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                    new Dictionary<string, object?> { ["$pid"] = pilot.CharacterId, ["$name"] = pilot.Name });

                // The role comes straight from the fleet roster — there is no
                // default-to-last-role lookup.
                await tx.ExecuteNonQueryAsync(
                    "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $role)",
                    new Dictionary<string, object?>
                    {
                        ["$sid"]  = sessionId,
                        ["$pid"]  = pilot.CharacterId,
                        ["$role"] = PilotRoleExtensions.ToCanonicalString(roleByPilotId[pilot.CharacterId]),
                    });
            }

            // The first session_time row is is_pause=1 — Start lands
            // in Paused mode so the user can adjust pilot roles before mining
            // counts. The chrome bar's Continue button (formerly Resume) is
            // the user's first action; auto-resume on first Tick/Crit also
            // covers this transition.
            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
                "VALUES ($sid, $ts, NULL, 1)",
                new Dictionary<string, object?> { ["$sid"] = sessionId, ["$ts"] = tsIso });
        });

        _activeSessionId = sessionId;
        // SeedRoles BEFORE Set so ActiveSessionChanged consumers
        // (PerPilotWidgetCoordinator) see a populated role map when they handle
        // the event and read GetRole(pilotId) for each widget's initialRole.
        _activeSession.SeedRoles(roleByPilotId);
        // Land in Paused — Start-as-paused. ActiveSession.State must
        // be Paused so the writer's drop rules treat events correctly until
        // the user clicks Continue or a Tick/Crit auto-resumes.
        _activeSession.Set(sessionId, SessionRunState.Paused);

        // Wire the writer's auto-resume callback BEFORE starting the
        // writer + watcher, so the very first event arriving while paused can
        // trigger an auto-resume (with that event preserved into the log).
        _writer.SetAutoResumeCallback(AutoResumeAsync);

        // Order matters: writer subscribes to RowChanged, so it must be started
        // BEFORE the watcher's first emitted event flows through. The resolved
        // characters carry their fresh LogFilePath (from the discovery snapshot
        // above), so no post-COMMIT re-resolve is needed.
        await _writer.StartAsync(sessionId, default);
        await _watcher.StartAsync(sessionId, resolvedCharacters, default);

        DisplaySessionName = expandedName;
        _sessionStartUtc = asOf;
        _pausedDurationAccumulated = TimeSpan.Zero;
        // Set _currentPauseStartUtc=asOf so the user's Continue click
        // takes the in-session ResumeAsync light path. The accumulator math
        // (now - sessionStart) - pausedAccumulated then evaluates to 0 at the
        // moment of Continue, so elapsed time begins ticking from 00:00:00.
        _currentPauseStartUtc = asOf;
        _lastQualifyingEventUtc = null;
        Mode = SessionWidgetMode.Paused;
        ResetStatFieldsToZero();
        // SessionStats keeps the prior session's TotalTimeMined
        // through Stop -> NewSession; reset it explicitly here so the moment
        // between Start and the first 1Hz tick doesn't render stale time.
        TotalTimeMined = "00:00:00";
        // The tick timer stays STOPPED while in Paused. It starts on
        // first Continue / auto-resume via TransitionPausedToRunningAsync.

        _log.LogInformation(
            "SessionController started session {SessionId} ('{Name}') with {PilotCount} pilots; landed in Paused (Start-as-paused).",
            sessionId, expandedName, resolvedCharacters.Count);
        return SessionStartResult.Started;
    }

    /* ----- SetPilotRoleAsync (Running/Paused, any time) ----- */

    /// <summary>
    /// Update a pilot's role mid-session. UPDATE-s <c>session_pilot.role</c>
    /// in SQL, mirrors the change into the in-memory role map (which fires
    /// <see cref="IActiveSessionContext.RolesChanged"/>), and flips
    /// <c>_statsDirty</c> so the next 1Hz tick refreshes the bound widgets.
    /// </summary>
    /// <remarks>
    /// No explicit refresh trigger needed — the existing dirty-flag mechanism
    /// (see <see cref="OnTickAsync"/>) re-queries via the established path on
    /// the next tick. <c>RolesChanged</c> fires from inside
    /// <c>ActiveSessionContext.SetRole</c> so the per-pilot widget VM updates
    /// its border / DisplayName immediately, between ticks.
    /// </remarks>
    /// <param name="pilotId">EVE character id of the pilot whose role is changing.</param>
    /// <param name="newRole">Replacement role.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if no session is active.</exception>
    public async Task SetPilotRoleAsync(long pilotId, PilotRole newRole, CancellationToken ct = default)
    {
        if (_activeSessionId is not { } sid)
        {
            throw new InvalidOperationException(
                "SetPilotRoleAsync called with no active session.");
        }

        await _db.ExecuteNonQueryAsync(
            "UPDATE session_pilot SET role = $role WHERE session_id = $sid AND pilot_id = $pid",
            new Dictionary<string, object?>
            {
                ["$sid"]  = sid,
                ["$pid"]  = pilotId,
                ["$role"] = PilotRoleExtensions.ToCanonicalString(newRole),
            },
            ct).ConfigureAwait(false);

        _activeSession.SetRole(pilotId, newRole);
        _statsDirty = true;
        _log.LogInformation(
            "SetPilotRoleAsync session={SessionId} pilot={PilotId} role={Role}.",
            sid, pilotId, newRole);
    }

    /* ----- Pause command (Running -> Paused) ----- */

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (_activeSessionId is not { } sid) return;
        if (Mode == SessionWidgetMode.Paused) return;

        await TransitionRunningToPausedAsync(DateTimeOffset.UtcNow);
        _log.LogInformation("SessionController paused session {SessionId} (manual).", sid);
    }

    /// <summary>
    /// Shared SQL + in-memory transition Running -&gt; Paused. Used by
    /// the manual <see cref="PauseAsync"/> command, the
    /// <see cref="AutoPauseAsync"/> idle-driven path, and any future caller
    /// that needs the same atomic close-active + open-pause + state-flip.
    /// Does not log or play any notification — those are caller concerns.
    /// </summary>
    /// <param name="nowUtc">Wall-clock time used for both the SQL timestamps and the in-memory <see cref="_currentPauseStartUtc"/>.</param>
    private async Task TransitionRunningToPausedAsync(DateTimeOffset nowUtc)
    {
        if (_activeSessionId is not { } sid) return;
        var tsIso = ToSqliteIso(nowUtc);

        await _db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync(
                "UPDATE session_time SET stop_time = $ts " +
                "WHERE session_id = $sid AND stop_time IS NULL",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });

            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
                "VALUES ($sid, $ts, NULL, 1)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });
        });

        _activeSession.Set(sid, SessionRunState.Paused);
        _currentPauseStartUtc = nowUtc;
        Mode = SessionWidgetMode.Paused;
        StopTickTimer();
    }

    /// <summary>
    /// Idle-driven auto-pause path. Invoked from <see cref="OnTickAsync"/>
    /// when <see cref="ShouldAutoPause"/> returns true. Performs the same
    /// transition as the manual Pause command, then raises
    /// <see cref="AutoPauseSoundRequested"/> so the MainWindow code-behind can
    /// play the auto-pause WAV.
    /// </summary>
    private async Task AutoPauseAsync()
    {
        if (Mode != SessionWidgetMode.Running) return;
        if (_activeSessionId is not { } sid) return;

        await TransitionRunningToPausedAsync(DateTimeOffset.UtcNow);
        AutoPauseSoundRequested?.Invoke(this, EventArgs.Empty);
        _log.LogInformation(
            "SessionController auto-paused session {SessionId} on {Minutes}-minute idle.",
            sid, _autoPauseIdleMinutes);
    }

    /// <summary>
    /// Idle-decision helper. Returns true iff
    /// <paramref name="lastQualifying"/> is non-null and at least
    /// <paramref name="thresholdMinutes"/> minutes elapsed between it and
    /// <paramref name="now"/>. Internal so the test project (which has
    /// InternalsVisibleTo) can drive it without a controller instance.
    /// </summary>
    /// <param name="lastQualifying">Wall-clock time of the most recent Tick/Crit, or <c>null</c> if none yet.</param>
    /// <param name="now">Reference time, typically <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <param name="thresholdMinutes">Idle threshold in minutes.</param>
    /// <returns><c>true</c> if the session should auto-pause; <c>false</c> otherwise.</returns>
    internal static bool ShouldAutoPause(DateTimeOffset? lastQualifying, DateTimeOffset now, int thresholdMinutes)
    {
        if (lastQualifying is not { } last) return false;
        return (now - last) >= TimeSpan.FromMinutes(thresholdMinutes);
    }

    /* ----- Resume command (Paused -> Running) ----- */

    /// <summary>
    /// Resume command. Dispatches between two paths via the
    /// <c>_sessionStartUtc is null</c> discriminator: null = app-launch path
    /// (writer/watcher not yet started) -> heavy
    /// <see cref="ResumeFromAppLaunchPausedAsync"/>; set = in-session path
    /// (writer/watcher already running) -> light path inline below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>_sessionStartUtc</c> is set in <see cref="StartAsync"/> and at the
    /// end of <see cref="ResumeFromAppLaunchPausedAsync"/>; reset in
    /// <see cref="StopInternalAsync"/>. <see cref="InitializeAsync"/> does
    /// NOT set it, so on fresh app launch with an open session_time row it
    /// remains null when the user clicks Resume — the heavy path runs once,
    /// then subsequent in-session Pause/Resume cycles use the light path.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (Mode != SessionWidgetMode.Paused) return;
        if (_activeSessionId is null) return;

        if (_sessionStartUtc is null)
        {
            await ResumeFromAppLaunchPausedAsync();
            return;
        }

        // Light path: in-session resume from pause (manual Continue click).
        // Writer/watcher already running; the same path is used for
        // Start-as-paused because StartAsync sets _sessionStartUtc.
        await TransitionPausedToRunningAsync(DateTimeOffset.UtcNow);
        _log.LogInformation("SessionController resumed session {SessionId} from in-session pause (manual Continue).", _activeSessionId);
    }

    /// <summary>
    /// Shared SQL + in-memory transition Paused -&gt; Running. Used by
    /// the manual <see cref="ResumeAsync"/> light path and by
    /// <see cref="AutoResumeAsync"/>. Caller is responsible for ensuring this
    /// runs on the UI dispatcher (Mode + StartTickTimer + bound props).
    /// </summary>
    /// <param name="nowUtc">Wall-clock time used for both the SQL timestamps and the pause-duration accumulator.</param>
    private async Task TransitionPausedToRunningAsync(DateTimeOffset nowUtc)
    {
        if (_activeSessionId is not { } sid) return;
        var tsIso = ToSqliteIso(nowUtc);

        await _db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync(
                "UPDATE session_time SET stop_time = $ts " +
                "WHERE session_id = $sid AND stop_time IS NULL",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });

            await tx.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
                "VALUES ($sid, $ts, NULL, 0)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });
        });

        _activeSession.Set(sid, SessionRunState.Running);

        if (_currentPauseStartUtc is { } pauseStart)
        {
            _pausedDurationAccumulated += nowUtc - pauseStart;
            _currentPauseStartUtc = null;
        }
        Mode = SessionWidgetMode.Running;
        // Baseline the idle timer to the resume moment so the next
        // auto-pause is measured from here, not from an old pre-pause Tick.
        _lastQualifyingEventUtc = nowUtc;
        StartTickTimer();
    }

    /// <summary>
    /// Invoked by <see cref="SessionEventWriter"/>'s callback when a
    /// Tick/Crit arrives during pause. Marshals to the UI dispatcher, runs the
    /// shared Paused -&gt; Running transition, and raises
    /// <see cref="AutoResumeSoundRequested"/> so MainWindow plays the WAV.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent: a no-op if the session is already Running or no session is
    /// active. The writer's single-flight guard ensures concurrent paused
    /// Tick/Crit callers share one invocation, but this method's own
    /// guard is belt-and-braces against any future caller.
    /// </para>
    /// <para>
    /// The marshal is required because the writer's consumer / WriteAsync
    /// runs on background threads; <c>Mode</c> changes and any WPF-bound
    /// property write must happen on the UI dispatcher.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token from the writer's WriteAsync.</param>
    public Task AutoResumeAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return AutoResumeOnUiThreadAsync();
        }
        return dispatcher.InvokeAsync(AutoResumeOnUiThreadAsync).Task.Unwrap();
    }

    private async Task AutoResumeOnUiThreadAsync()
    {
        if (Mode != SessionWidgetMode.Paused) return;
        if (_activeSessionId is not { } sid) return;

        await TransitionPausedToRunningAsync(DateTimeOffset.UtcNow).ConfigureAwait(true);
        AutoResumeSoundRequested?.Invoke(this, EventArgs.Empty);
        _log.LogInformation(
            "SessionController auto-resumed session {SessionId} on incoming Tick/Crit.", sid);
    }

    /// <summary>
    /// Resume button click after <see cref="InitializeAsync"/> detected an
    /// open <c>session_time</c> row at app launch. Heavy path: re-queries
    /// the open row's <c>is_pause</c> flag, transitions the SQL state
    /// (close pause row + open active row, if needed), starts the writer +
    /// watcher (which were not running), hydrates timer/stat fields from
    /// the prior session's accumulated duration, sets
    /// <c>_sessionStartUtc</c> so subsequent Pause/Resume cycles use the
    /// light path in <see cref="ResumeAsync"/>, and lands in
    /// <see cref="SessionWidgetMode.Running"/>.
    /// </summary>
    private async Task ResumeFromAppLaunchPausedAsync()
    {
        if (_activeSessionId is not { } sid) return;
        var nowUtc = DateTimeOffset.UtcNow;

        // If the open row is a pause row, close it and open a new active row;
        // if it is already an active row, leave it alone.
        var openRows = await _db.QueryAsync(
            "SELECT is_pause FROM session_time WHERE session_id = $sid AND stop_time IS NULL LIMIT 1",
            r => r.GetInt32(0),
            new Dictionary<string, object?> { ["$sid"] = sid }, default);
        var wasPaused = openRows.Count > 0 && openRows[0] == 1;

        if (wasPaused)
        {
            var tsIso = ToSqliteIso(nowUtc);
            await _db.WithTransactionAsync(async (tx, _) =>
            {
                await tx.ExecuteNonQueryAsync(
                    "UPDATE session_time SET stop_time = $ts " +
                    "WHERE session_id = $sid AND stop_time IS NULL",
                    new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });

                await tx.ExecuteNonQueryAsync(
                    "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
                    "VALUES ($sid, $ts, NULL, 0)",
                    new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso });
            });
        }

        // Bootstrap timer state from current cumulative active duration.
        var stats = await _statsRepo.GetSessionStatsAsync(sid, nowUtc, allowLazyFetch: true, default);
        _sessionStartUtc = nowUtc - stats.TotalTimeMined;
        _pausedDurationAccumulated = TimeSpan.Zero;
        _currentPauseStartUtc = null;

        // Hydrate the visible stat fields from the same snapshot.
        ApplyStatsSnapshot(stats);

        // Pilots for this session — needed by the watcher.
        // Also read role here so we can re-seed the in-memory role map. The
        // map was empty (app just launched); without this seed, the writer's GetRole
        // calls would default every event to Miner.
        var pilotRows = await _db.QueryAsync(
            "SELECT pilot_id, role FROM session_pilot WHERE session_id = $sid",
            static r => (PilotId: r.GetInt64(0), Role: r.GetString(1)),
            new Dictionary<string, object?> { ["$sid"] = sid }, default);
        var pilotIdSet = new HashSet<long>(pilotRows.Select(p => p.PilotId));
        var sessionPilots = _allCharacters.Where(c => pilotIdSet.Contains(c.CharacterId)).ToList();
        var roleByPilotId = pilotRows.ToDictionary(
            p => p.PilotId,
            p => PilotRoleExtensions.Parse(p.Role));
        _activeSession.SeedRoles(roleByPilotId);

        _activeSession.Set(sid, SessionRunState.Running);
        // Wire the auto-resume callback for the rest of this session's
        // lifetime — any subsequent manual Pause + Tick/Crit during it will
        // auto-resume. Match the same ordering as StartAsync (callback wired
        // BEFORE writer/watcher start).
        _writer.SetAutoResumeCallback(AutoResumeAsync);
        await _writer.StartAsync(sid, default);
        if (sessionPilots.Count > 0)
        {
            // Same staleness fix as StartAsync — re-resolve LogFilePath against the live FS.
            var freshSessionPilots = await ResolveFreshPilotsAsync(sessionPilots, default);
            await _watcher.StartAsync(sid, freshSessionPilots, default);
        }
        else
        {
            _log.LogWarning(
                "Resumed session {SessionId} has no pilots in session_pilot; skipping watcher start.", sid);
        }

        Mode = SessionWidgetMode.Running;
        // Baseline so the idle timer doesn't immediately auto-pause
        // a freshly app-launch-resumed session.
        _lastQualifyingEventUtc = nowUtc;
        StartTickTimer();
        _log.LogInformation(
            "SessionController resumed session {SessionId} from app-launch Paused (wasPaused={WasPaused}).",
            sid, wasPaused);
    }

    /* ----- Stop command (Running/Paused -> Stopped) ----- */

    /// <summary>
    /// Stop the active session. Always shows the confirm dialog before any
    /// SQL state is changed. Covers both <see cref="SessionWidgetMode.Running"/>
    /// -> <see cref="SessionWidgetMode.Stopped"/> AND
    /// <see cref="SessionWidgetMode.Paused"/> -> <see cref="SessionWidgetMode.Stopped"/>
    /// transitions (the chrome <c>Stop</c> button is visible for both Running
    /// and Paused).
    /// </summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        if (_activeSessionId is not { } sid) return;

        // The dialog sits behind the IConfirmDialog seam (caller-supplied
        // strings) so tests can substitute a fake instead of showing a window.
        if (!_confirmStop.Show(
                title:        "Stop session?",
                body:         "This will stop the current session and it cannot be started again. If you are taking a break please pause.",
                confirmLabel: "Stop"))
        {
            return;
        }

        await StopInternalAsync(sid);
    }

    private async Task StopInternalAsync(string sid)
    {
        var tsIso = ToSqliteIso(DateTimeOffset.UtcNow);
        await _db.ExecuteNonQueryAsync(
            "UPDATE session_time SET stop_time = $ts " +
            "WHERE session_id = $sid AND stop_time IS NULL",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = tsIso },
            default);

        // Stop watcher first so no further events flow into the writer; then
        // stop the writer so its consumer drains and unsubscribes from the
        // SQL update hook.
        try { await _watcher.StopAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "Watcher StopAsync threw during session stop; continuing."); }

        try { await _writer.StopAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "Writer StopAsync threw during session stop; continuing."); }

        _activeSession.Set(null, SessionRunState.None);
        // Clear the role map so the next session's SeedRoles starts from empty
        // (the next session's roles come from its fleet roster, not from
        // whatever happened to be in the map at stop time).
        _activeSession.ClearRoles();
        StopTickTimer();

        // Lifecycle private fields ARE reset — the session is genuinely over.
        _activeSessionId = null;
        _sessionStartUtc = null;
        _pausedDurationAccumulated = TimeSpan.Zero;
        _currentPauseStartUtc = null;
        // Reset the idle baseline so the next session starts clean.
        // The writer's own StopAsync clears the auto-resume callback, but be
        // explicit here too — defence in depth against any future caller order.
        _lastQualifyingEventUtc = null;
        _writer.SetAutoResumeCallback(null);

        // Bound display props (DisplaySessionName, SessionName, TotalTimeMined,
        // the stat fields) are PRESERVED so the Stopped-mode view shows the
        // just-ended session; the next StartAsync overwrites them.
        Mode = SessionWidgetMode.Stopped;
        _log.LogInformation("SessionController stopped session {SessionId}; transitioned to Stopped mode.", sid);
    }

    /* ----- Tick timer + dirty flag ----- */

    private void OnSessionEventAdded(object? sender, SessionEventAddedEventArgs e)
    {
        // Cheap: just flip the bit; the next tick reads it.
        _statsDirty = true;

        // Only Tick and Crit count as "mining activity" — matches the
        // definition used by the volume/ISK pipelines, session totals, and
        // per-pilot widgets.
        // Residue and Depleted do not reset the idle timer.
        if (e.Kind == MiningEventKind.Tick || e.Kind == MiningEventKind.Crit)
        {
            _lastQualifyingEventUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Refresh the cached auto-pause idle threshold when the user
    /// changes it in Settings. Async because the repo call is async; the void
    /// return is fine here because failures only result in a stale cached
    /// value (next tick still uses the previous threshold).
    /// </summary>
    private async void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key != "auto_pause_idle_minutes") return;
        try
        {
            _autoPauseIdleMinutes = await _settings.GetAutoPauseIdleMinutesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to refresh auto_pause_idle_minutes on SettingChanged.");
        }
    }

    private void StartTickTimer()
    {
        // The timer MUST be bound to the UI dispatcher. Some lifecycle paths
        // (notably StartAsync's await on ResolveFreshPilotsAsync, which does
        // real async file I/O) hop the continuation to the thread pool; the
        // default DispatcherTimer ctor binds to Dispatcher.CurrentDispatcher
        // which on a thread-pool thread is a worker dispatcher with no message
        // pump, and Tick never fires. Use the explicit ctor with the UI
        // dispatcher so timer dispatch is independent of the calling thread.
        _tickTimer?.Stop();
        var uiDispatcher = Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
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
            _log.LogWarning(ex, "SessionController OnTick threw; suppressed to keep timer alive.");
        }
    }

    private async Task OnTickAsync()
    {
        // Always update elapsed-time client-side from wall clock + state.
        if (_sessionStartUtc is { } start)
        {
            var elapsed = (DateTimeOffset.UtcNow - start) - _pausedDurationAccumulated;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            TotalTimeMined = FormatElapsed(elapsed);
            LastUpdated = DateTimeOffset.UtcNow;
        }

        // Idle auto-pause check. Fires only in Running mode; the
        // transition stops the tick timer so we will not re-enter this tick
        // until the next Continue / auto-resume restarts it.
        if (Mode == SessionWidgetMode.Running
            && ShouldAutoPause(_lastQualifyingEventUtc, DateTimeOffset.UtcNow, _autoPauseIdleMinutes))
        {
            await AutoPauseAsync().ConfigureAwait(true);
            return;
        }

        // Coalesced stats refresh — only re-query when an event has arrived.
        if (_statsDirty && _activeSessionId is { } sid)
        {
            _statsDirty = false;
            try
            {
                var stats = await _statsRepo.GetSessionStatsAsync(sid, DateTimeOffset.UtcNow, allowLazyFetch: true, default).ConfigureAwait(true);
                ApplyStatsSnapshot(stats);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Stats refresh failed at tick; will retry next tick.");
                _statsDirty = true;
            }
        }
    }

    /* ----- Helpers ----- */

    private void ApplyStatsSnapshot(SessionStats stats)
    {
        TotalM3Mined     = stats.TotalM3Mined;
        M3PerSecond      = stats.M3PerSecond;
        TotalIskMined    = stats.TotalIskMined;
        IskPerHour       = stats.IskPerHour;
        TotalM3FromCrits = stats.TotalM3FromCrits;
        CritCount        = stats.CritCount;
        CritChance       = stats.CritChance;
        CritIskPercent   = stats.CritIskPercent;
        SessionRefinedOutput = stats.RefinedOutput;
    }

    private void ResetStatFieldsToZero()
    {
        TotalM3Mined     = 0;
        M3PerSecond      = 0;
        TotalIskMined    = 0;
        IskPerHour       = 0;
        TotalM3FromCrits = 0;
        CritCount        = 0;
        CritChance       = 0;
        CritIskPercent   = 0;
        SessionRefinedOutput = Array.Empty<RefinedMaterialAmount>();
    }


    /// <summary>
    /// Take a fresh gamelog-discovery snapshot keyed by character id,
    /// for resolving a fleet roster at Start (each entry carries its live
    /// <c>LogFilePath</c>). Falls back to the launch-time <c>_allCharacters</c>
    /// if discovery throws — the same warn-and-fallback as
    /// <see cref="ResolveFreshPilotsAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Discovered characters keyed by <see cref="Character.CharacterId"/>.</returns>
    private async Task<IReadOnlyDictionary<long, Character>> ResolveDiscoveredByIdAsync(CancellationToken ct)
    {
        IReadOnlyList<Character> fresh;
        try
        {
            fresh = await _discovery.DiscoverCharactersAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Discovery refresh failed at Start; falling back to launch-time characters.");
            fresh = _allCharacters;
        }

        var byId = new Dictionary<long, Character>(fresh.Count);
        foreach (var c in fresh)
        {
            byId[c.CharacterId] = c;
        }
        return byId;
    }

    /// <summary>
    /// Re-resolve each selected pilot's LogFilePath against the live filesystem.
    /// EVE creates a fresh log file when a character logs in; the
    /// <see cref="GamelogDiscoveryService"/> snapshot taken at app launch may be
    /// stale by the time the user clicks Start. We keep the user's selection
    /// (matched on character id) but swap in the fresh <c>LogFilePath</c>.
    /// Pilots whose char id is not in the fresh snapshot fall back to their
    /// original record (with a Warning log).
    /// </summary>
    private async Task<IReadOnlyList<Character>> ResolveFreshPilotsAsync(
        IReadOnlyList<Character> selectedPilots, CancellationToken ct)
    {
        IReadOnlyList<Character> freshAll;
        try
        {
            freshAll = await _discovery.DiscoverCharactersAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Discovery refresh failed at Start; falling back to launch-time pilot paths.");
            return selectedPilots;
        }

        var freshById = freshAll.ToDictionary(c => c.CharacterId);
        var result = new List<Character>(selectedPilots.Count);
        foreach (var p in selectedPilots)
        {
            if (freshById.TryGetValue(p.CharacterId, out var fresh))
            {
                if (!string.Equals(fresh.LogFilePath, p.LogFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogInformation(
                        "Pilot {CharId} log file refreshed: '{Old}' -> '{New}' (EVE created a newer log file after app launch).",
                        p.CharacterId, p.LogFilePath, fresh.LogFilePath);
                }
                result.Add(fresh);
            }
            else
            {
                _log.LogWarning(
                    "Pilot {CharId} ('{Name}') not present in fresh discovery; tailing the launch-time path '{Path}'.",
                    p.CharacterId, p.Name, p.LogFilePath);
                result.Add(p);
            }
        }
        return result;
    }

    /// <summary>Format the elapsed time for the bound TotalTimeMined display. Hours can exceed 23.</summary>
    internal static string FormatElapsed(TimeSpan elapsed)
    {
        var total = (long)elapsed.TotalHours;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}",
            total, elapsed.Minutes, elapsed.Seconds);
    }

    private static string ToSqliteIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Dispose: unsubscribe + stop tick timer + unhook pilot listeners.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _notifier.SessionEventAdded -= OnSessionEventAdded;
        _settings.SettingChanged -= OnSettingChanged;
        StopTickTimer();
        _disposed = true;
    }
}
