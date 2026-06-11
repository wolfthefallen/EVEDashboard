namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// Plan 37 — Start-as-paused. Start writes the first session_time row with
/// is_pause=1, lands in <see cref="SessionWidgetMode.Paused"/>, sets
/// <see cref="IActiveSessionContext.State"/> to
/// <see cref="SessionRunState.Paused"/>, and wires the writer's auto-resume
/// callback. The tick timer is NOT started — it kicks off on the user's first
/// Continue click (or an auto-resume). Plan 42: Start takes a fleet roster.
/// </summary>
public class SessionControllerStartPausedTests
{
    [Fact]
    public async Task StartAsync_writes_session_time_with_is_pause_1()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Plan 37 Start";

        await vm.StartAsync(f.Roster());
        try
        {
            var rows = await f.Db.QueryAsync(
                "SELECT is_pause FROM session_time WHERE stop_time IS NULL",
                static r => r.GetInt32(0));

            rows.Should().ContainSingle();
            rows[0].Should().Be(1, "Start lands in Paused (plan 37) — the first session_time row must carry is_pause=1");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_lands_in_Paused_mode_and_state()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Plan 37 Start";

        await vm.StartAsync(f.Roster());
        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Paused);
            f.ActiveSession.State.Should().Be(SessionRunState.Paused);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_elapsed_time_stays_at_zero_until_Continue()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Plan 37 Start";

        await vm.StartAsync(f.Roster());
        try
        {
            // Elapsed shows 00:00:00 immediately after Start (no tick timer
            // running, no time accrual while paused).
            vm.TotalTimeMined.Should().Be("00:00:00");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_then_ResumeCommand_takes_in_session_light_path_so_elapsed_starts_at_zero()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Plan 37 Continue Path";

        await vm.StartAsync(f.Roster());
        try
        {
            // After Continue, the session_time row sequence is: row 1 (is_pause=1,
            // CLOSED), row 2 (is_pause=0, OPEN). Confirms Resume took the light
            // path — not the app-launch heavy path which would have skipped a
            // close+open transaction.
            await vm.ResumeCommand.ExecuteAsync(null);

            var rows = await f.Db.QueryAsync(
                "SELECT is_pause, stop_time FROM session_time ORDER BY session_time_id",
                static r => (IsPause: r.GetInt32(0), StopTime: r.IsDBNull(1) ? null : r.GetString(1)));

            rows.Should().HaveCount(2);
            rows[0].IsPause.Should().Be(1);
            rows[0].StopTime.Should().NotBeNull("the Start-time pause row must be closed by Continue");
            rows[1].IsPause.Should().Be(0);
            rows[1].StopTime.Should().BeNull("the new active row must be open");

            vm.Mode.Should().Be(SessionWidgetMode.Running);
            f.ActiveSession.State.Should().Be(SessionRunState.Running);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartAsync_is_noop_with_warning_log_when_called_while_a_session_is_already_active()
    {
        // Bug-1 follow-up: defensive guard against double-Start (a UI signal
        // bug previously let the New Session dialog stay open after Start,
        // exposing the user to clicking Start a second time which then crashed
        // inside _writer.StartAsync). Plan 42 decision 3.2 = A: the guard now
        // returns SessionStartResult.Started (a session genuinely is active) so
        // the dialog still closes — but no second session is created.
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "First";
        await vm.StartAsync(f.Roster());

        try
        {
            var firstSessionId = f.ActiveSession.ActiveSessionId;
            firstSessionId.Should().NotBeNull("preconditions: first Start put a session in flight");

            // Simulate a double-click: change the name, then call Start again.
            // Pre-fix this threw "writer already started"; post-fix it's a quiet
            // no-op (returns Started) with a Warning log.
            vm.SessionName = "Second (should be ignored)";

            SessionStartResult? secondResult = null;
            Func<Task> act = async () => secondResult = await vm.StartAsync(f.Roster(2));
            await act.Should().NotThrowAsync();
            secondResult.Should().Be(SessionStartResult.Started, "the guard reports Started so the dialog closes");

            // Original session is untouched.
            f.ActiveSession.ActiveSessionId.Should().Be(firstSessionId);
            vm.Mode.Should().Be(SessionWidgetMode.Paused);

            // No second session row was inserted.
            var sessionCount = await f.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session");
            sessionCount.Should().Be(1);

            f.WidgetLog.Entries.Should().Contain(e =>
                e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
                e.Message.Contains("already active", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StopAsync_clears_last_qualifying_event_marker_for_next_session()
    {
        // Indirect: after Stop, a fresh session must not see a stale
        // _lastQualifyingEventUtc that would trigger an immediate auto-pause.
        // We assert by starting + stopping + starting again; the second session
        // lands in Paused without errors and the state machine is clean.
        using var f = new SessionControllerTestFixture();
        using (var vm1 = f.CreateViewModel())
        {
            vm1.SessionName = "First";
            await vm1.StartAsync(f.Roster());
            await vm1.StopCommand.ExecuteAsync(null);
        }

        using (var vm2 = f.CreateViewModel())
        {
            vm2.SessionName = "Second";
            await vm2.StartAsync(f.Roster());
            try
            {
                vm2.Mode.Should().Be(SessionWidgetMode.Paused);
                f.ActiveSession.State.Should().Be(SessionRunState.Paused);
            }
            finally
            {
                await vm2.StopCommand.ExecuteAsync(null);
            }
        }
    }
}
