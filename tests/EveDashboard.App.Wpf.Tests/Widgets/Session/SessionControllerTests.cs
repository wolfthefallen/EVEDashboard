namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using System.Globalization;
using System.IO;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Sessions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// SessionController test workhorse — covers state-machine transitions,
/// lifecycle SQL row INSERTs/UPDATEs, IConfirmDialog cancel/confirm
/// branches, template-variable expansion at Start, the ISK-stub assertion
/// (asserts the real zero the stub returns), and Resume-mode hydration
/// from prior open <c>session_time</c> rows.
///
/// DispatcherTimer.Tick is not exercised in xUnit (no message loop without an
/// STA pump); timer-driven elapsed-time tick + coalesced stats refresh are
/// covered by the manual test doc.
/// </summary>
public class SessionControllerTests
{
    /* ---------- Constructor null-arg checks ---------- */

    [Fact]
    public void Ctor_throws_on_null_db()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            null!, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_throws_on_null_statsRepo()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, null!, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("statsRepo");
    }

    [Fact]
    public void Ctor_throws_on_null_notifier()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, null!, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("notifier");
    }

    [Fact]
    public void Ctor_throws_on_null_writer()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, null!, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void Ctor_throws_on_null_watcher()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, null!, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("watcher");
    }

    [Fact]
    public void Ctor_throws_on_null_activeSession()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, null!,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("activeSession");
    }

    [Fact]
    public void Ctor_throws_on_null_confirmStop()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            null!, f.Discovery, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("confirmStop");
    }

    [Fact]
    public void Ctor_throws_on_null_discovery()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, null!, f.Characters, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("discovery");
    }

    [Fact]
    public void Ctor_throws_on_null_allCharacters()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, null!, f.Settings, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("allCharacters");
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, null!, NullLogger<SessionController>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Ctor_throws_on_null_log()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionController(
            f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
            f.ConfirmStop, f.Discovery, f.Characters, f.Settings, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    /* ---------- InitializeAsync mode detection ---------- */

    [Fact]
    public async Task InitializeAsync_with_no_open_session_time_starts_in_Config_mode()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();

        await vm.InitializeAsync();

        vm.Mode.Should().Be(SessionWidgetMode.Config);
    }

    [Fact]
    public async Task InitializeAsync_with_open_session_time_is_pause_zero_starts_in_Paused_mode()
    {
        using var f = new SessionControllerTestFixture();
        await SeedOpenSessionAsync(f, isPause: 0, sessionName: "Prior Run");
        using var vm = f.CreateViewModel();

        await vm.InitializeAsync();

        vm.Mode.Should().Be(SessionWidgetMode.Paused);
        vm.DisplaySessionName.Should().Be("Prior Run");
    }

    [Fact]
    public async Task InitializeAsync_with_open_session_time_is_pause_one_starts_in_Paused_mode()
    {
        using var f = new SessionControllerTestFixture();
        await SeedOpenSessionAsync(f, isPause: 1, sessionName: "Paused Run");
        using var vm = f.CreateViewModel();

        await vm.InitializeAsync();

        vm.Mode.Should().Be(SessionWidgetMode.Paused);
    }

    /* ---------- Start lifecycle ----------
       Start is driven by a fleet roster passed to StartAsync(roster). The
       CanExecute logic (name + a selected fleet) lives in
       SessionConfigDialogViewModel and is covered by its tests. */

    [Fact]
    public async Task StartAsync_inserts_session_session_pilot_session_time_rows_and_transitions_to_Paused()
    {
        // Start lands in Paused (Start-as-paused) so the user can
        // configure pilot roles before mining counts. The first session_time
        // row carries is_pause=1.
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";

        await vm.StartAsync(f.Roster(2));

        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Paused);

            var sessionCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session");
            sessionCount.Should().Be(1);

            var pilotCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_pilot");
            pilotCount.Should().Be(2);

            var timeRowCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE stop_time IS NULL AND is_pause = 1");
            timeRowCount.Should().Be(1);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_resets_TotalTimeMined_so_pre_tick_render_does_not_show_stale_prior_session_time()
    {
        // Regression guard: after Stop the SessionStats widget continues to show
        // the just-ended session's frozen elapsed time. When Start fires, the
        // new TotalTimeMined must be zeroed BEFORE the first 1Hz tick so the
        // user doesn't see the stale time flash on screen for ~1 second.
        using var f = new SessionControllerTestFixture(dialogResult: true);
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);
        vm.TotalTimeMined = "00:01:23"; // simulate accumulated elapsed time
        await vm.StopCommand.ExecuteAsync(null);
        vm.TotalTimeMined.Should().Be("00:01:23",
            "preconditions: Stop preserves the prior session's frozen elapsed time");

        await StartAsync(f, vm);

        try
        {
            vm.TotalTimeMined.Should().Be("00:00:00");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_expands_template_variables_into_session_name()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "{{$DATE}} run with {{$CHARS}}";

        await vm.StartAsync(f.Roster());

        try
        {
            var name = await StringScalarAsync(f, "SELECT name FROM session LIMIT 1");
            name.Should().NotContain("{{$");
            name.Should().Contain("TestPilotOne");
            name.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} run with TestPilotOne$");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_mutates_active_session_context_to_Paused_with_new_id()
    {
        // ActiveSession.State flips to Paused (not Running) so
        // the writer's drop rules treat events correctly until the user clicks
        // Continue or a Tick/Crit auto-resumes.
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";

        f.ActiveSession.ActiveSessionId.Should().BeNull();
        f.ActiveSession.State.Should().Be(SessionRunState.None);

        await vm.StartAsync(f.Roster());

        try
        {
            f.ActiveSession.ActiveSessionId.Should().NotBeNull();
            f.ActiveSession.State.Should().Be(SessionRunState.Paused);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    /* ---------- Pause / Resume lifecycle ---------- */

    [Fact]
    public async Task PauseAsync_closes_active_session_time_and_inserts_pause_row()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);

        try
        {
            await vm.PauseCommand.ExecuteAsync(null);

            var pauseRowCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 1 AND stop_time IS NULL");
            pauseRowCount.Should().Be(1);

            var closedActiveCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 0 AND stop_time IS NOT NULL");
            closedActiveCount.Should().Be(1);

            f.ActiveSession.State.Should().Be(SessionRunState.Paused);
            vm.Mode.Should().Be(SessionWidgetMode.Paused);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task ResumeAsync_from_pause_closes_pause_row_and_inserts_active_row()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);
        await vm.PauseCommand.ExecuteAsync(null);

        try
        {
            await vm.ResumeCommand.ExecuteAsync(null);

            var openActiveCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 0 AND stop_time IS NULL");
            openActiveCount.Should().Be(1);

            // The StartAsync helper does Start (writes is_pause=1) +
            // Resume (closes it), so by the time the explicit manual Pause + Resume
            // cycle finishes there are TWO closed pause rows in session_time:
            // one from Start-as-paused, one from the manual Pause/Resume.
            var closedPauseCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 1 AND stop_time IS NOT NULL");
            closedPauseCount.Should().Be(2);

            f.ActiveSession.State.Should().Be(SessionRunState.Running);
            vm.Mode.Should().Be(SessionWidgetMode.Running);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    /* ---------- Stop lifecycle ---------- */

    [Fact]
    public async Task StopAsync_dialog_cancelled_does_nothing()
    {
        using var f = new SessionControllerTestFixture(dialogResult: false);
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);

        try
        {
            await vm.StopCommand.ExecuteAsync(null);

            f.ConfirmStop.ShowCount.Should().Be(1);
            vm.Mode.Should().Be(SessionWidgetMode.Running);
            f.ActiveSession.State.Should().Be(SessionRunState.Running);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StopAsync_dialog_confirmed_closes_session_time_and_transitions_to_Stopped()
    {
        // Stop lands in Stopped mode (not Config).
        using var f = new SessionControllerTestFixture(dialogResult: true);
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);

        await vm.StopCommand.ExecuteAsync(null);

        f.ConfirmStop.ShowCount.Should().Be(1);
        vm.Mode.Should().Be(SessionWidgetMode.Stopped);

        var openCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE stop_time IS NULL");
        openCount.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_resets_active_session_lifecycle_but_preserves_display_state()
    {
        // Lifecycle private fields + ActiveSessionContext still reset
        // (the session is genuinely over), but bound display props PRESERVED so
        // the Stopped-mode view shows the just-ended session. (The controller
        // has no pilot list — the fleet picker lives in the dialog.)
        using var f = new SessionControllerTestFixture(dialogResult: true);
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);

        await vm.StopCommand.ExecuteAsync(null);

        // Lifecycle reset.
        f.ActiveSession.ActiveSessionId.Should().BeNull();
        f.ActiveSession.State.Should().Be(SessionRunState.None);

        // Display props preserved (the Stopped-mode view binds to these).
        vm.DisplaySessionName.Should().NotBeEmpty(
            "DisplaySessionName must remain visible in Stopped mode");
    }

    /* ---------- Resume from app-launch Paused ---------- */

    [Fact]
    public async Task ResumeAsync_from_app_launch_paused_was_running_starts_writer_watcher_and_transitions_to_Running()
    {
        using var f = new SessionControllerTestFixture();
        await SeedOpenSessionAsync(f, isPause: 0, sessionName: "Prior Run", participatingPilots: f.Characters.Take(1).ToList());
        using var vm = f.CreateViewModel();
        await vm.InitializeAsync();
        vm.Mode.Should().Be(SessionWidgetMode.Paused);

        await vm.ResumeCommand.ExecuteAsync(null);

        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Running);
            f.ActiveSession.State.Should().Be(SessionRunState.Running);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task ResumeAsync_from_app_launch_paused_was_paused_inserts_active_row_and_transitions_to_Running()
    {
        using var f = new SessionControllerTestFixture();
        await SeedOpenSessionAsync(f, isPause: 1, sessionName: "Paused Run", participatingPilots: f.Characters.Take(1).ToList());
        using var vm = f.CreateViewModel();
        await vm.InitializeAsync();
        vm.Mode.Should().Be(SessionWidgetMode.Paused);

        await vm.ResumeCommand.ExecuteAsync(null);

        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Running);

            var openActiveCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 0 AND stop_time IS NULL");
            openActiveCount.Should().Be(1);

            var closedPauseCount = await ScalarLongAsync(f, "SELECT COUNT(*) FROM session_time WHERE is_pause = 1 AND stop_time IS NOT NULL");
            closedPauseCount.Should().Be(1);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    /* ---------- Fresh-discovery resolve at Start (regression: 2026-05-03 user mining session lost events) ---------- */

    [Fact]
    public async Task StartAsync_resolves_roster_from_fresh_discovery_snapshot_not_stale_launch_path()
    {
        // Reproduces the user's bug: EVE created a newer log file for the pilot
        // between app launch (when the Character record was captured) and Start.
        // Start front-loads a FRESH discovery snapshot and resolves the roster
        // against it, so the watcher follows the live file — the stale launch-time
        // path is never used (discovery IS the source now; the old post-COMMIT
        // "log file refreshed" reconcile is gone).
        var dir = Path.Combine(Path.GetTempPath(), "evemining-stalepath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const long charId = 2_111_222_333L;
            const string header =
                "------------------------------------------------------------\r\n" +
                "  Gamelog\r\n" +
                "  Listener: Stale Pilot\r\n" +
                "  Session Started: 2026.05.03 00:00:00\r\n" +
                "------------------------------------------------------------\r\n";

            // Old file = what discovery would have captured at app launch.
            var oldPath = Path.Combine(dir, $"20260101_120000_{charId}.txt");
            await File.WriteAllTextAsync(oldPath, header);

            // Fixture: pilot's Character record points at oldPath.
            using var f = new SessionControllerTestFixture(
                dialogResult: true,
                characters: new[] { new EveDashboard.Core.Domain.Character(charId, "Stale Pilot", oldPath) });

            // Custom Discovery against the real gamelog dir (NOT the fixture's empty one).
            var discovery = new EveDashboard.Mining.Gamelogs.GamelogDiscoveryService(
                dir, Microsoft.Extensions.Logging.Abstractions.NullLogger<EveDashboard.Mining.Gamelogs.GamelogDiscoveryService>.Instance);
            using var vm = new SessionController(
                f.Db, f.StatsRepo, f.Writer, f.Writer, f.Watcher, f.ActiveSession,
                f.ConfirmStop, discovery, f.Characters, f.Settings, f.WidgetLog);

            // After fixture construction but BEFORE Start: EVE creates a newer log file.
            var newPath = Path.Combine(dir, $"20260503_040929_{charId}.txt");
            await File.WriteAllTextAsync(newPath, header);

            vm.SessionName = "Stale-path repro";
            var result = await vm.StartAsync(new[] { new FleetMember(charId, "Stale Pilot", PilotRole.Miner) });

            try
            {
                // The pilot is discoverable (via the fresh snapshot, which picks the
                // newer file by its YYYYMMDD prefix), so the session starts with that
                // pilot — it was NOT skipped as undiscoverable.
                result.Should().Be(SessionStartResult.Started);

                var pilotRows = await f.Db.QueryAsync(
                    "SELECT pilot_id FROM session_pilot", r => r.GetInt64(0), null, default);
                pilotRows.Should().ContainSingle().Which.Should().Be(charId);

                f.WidgetLog.Entries.Should().NotContain(
                    e => e.Message.Contains("skipping", StringComparison.OrdinalIgnoreCase),
                    "the pilot is discoverable so it must not be skipped");
            }
            finally
            {
                await vm.StopCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* test cleanup */ }
        }
    }

    /* ---------- ISK stub assertion (assert the real stub value, never shim it) ---------- */

    [Fact]
    public async Task IskFields_render_zero_until_plan_18()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        await StartAsync(f, vm);

        try
        {
            await vm.RefreshAsync();

            vm.TotalIskMined.Should().Be(0d);
            vm.IskPerHour.Should().Be(0d);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    /* ---------- Elapsed-time formatter ---------- */

    [Fact]
    public void FormatElapsed_renders_zero_as_double_padded_zero()
    {
        SessionController.FormatElapsed(TimeSpan.Zero).Should().Be("00:00:00");
    }

    [Fact]
    public void FormatElapsed_handles_hours_above_24_without_rolling_to_days()
    {
        SessionController.FormatElapsed(TimeSpan.FromHours(30) + TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(7))
            .Should().Be("30:15:07");
    }

    /* ---------- Helpers ---------- */

    /// <summary>
    /// Helper that brings the controller to the Running state in one step.
    /// Start lands in Paused (Start-as-paused), so tests that want a
    /// Running session must follow up with ResumeCommand. Start takes a
    /// fleet roster — callers that want to assert on the post-Start state call
    /// <c>vm.StartAsync(f.Roster())</c> directly instead of this helper.
    /// </summary>
    private static async Task StartAsync(SessionControllerTestFixture f, SessionController vm)
    {
        vm.SessionName = "Run";
        await vm.StartAsync(f.Roster());
        await vm.ResumeCommand.ExecuteAsync(null); // -> Running
    }

    private static async Task<long> ScalarLongAsync(SessionControllerTestFixture f, string sql)
    {
        var rows = await f.Db.QueryAsync(sql, r => r.GetInt64(0), null, default);
        return rows.Count == 0 ? 0L : rows[0];
    }

    private static async Task<string> StringScalarAsync(SessionControllerTestFixture f, string sql)
    {
        var rows = await f.Db.QueryAsync(sql, r => r.GetString(0), null, default);
        return rows.Count == 0 ? string.Empty : rows[0];
    }

    /// <summary>Seed a <c>session</c> + <c>session_pilot</c> + open <c>session_time</c> row directly via SQL.</summary>
    private static async Task SeedOpenSessionAsync(
        SessionControllerTestFixture f,
        int isPause,
        string sessionName,
        IReadOnlyList<EveDashboard.Core.Domain.Character>? participatingPilots = null)
    {
        var pilots = participatingPilots ?? f.Characters;
        var nowIso = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        // session_id is a content-addressed sha256 hex string (no
        // autoincrement). Compute one from the (unique) session name so each seed
        // gets a distinct, well-formed id.
        var sid = EveDashboard.Core.Sessions.SessionId.Compute(sessionName, nowIso);
        await f.Db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $name, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$name"] = sessionName }, default);

        foreach (var p in pilots)
        {
            await f.Db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                new Dictionary<string, object?> { ["$pid"] = p.CharacterId, ["$name"] = p.Name }, default);
            await f.Db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = p.CharacterId }, default);
        }

        // Always insert one closed active period first so resume-from-Paused has data to compute against.
        var earlierIso = DateTimeOffset.UtcNow.AddMinutes(-30).UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        await f.Db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $start, $stop, 0)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$start"] = earlierIso, ["$stop"] = nowIso }, default);

        // Then the currently-open row of the requested kind.
        await f.Db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $ts, NULL, $isp)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$ts"] = nowIso, ["$isp"] = isPause }, default);
    }
}
