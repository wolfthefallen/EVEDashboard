namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// Plan 37 — <see cref="SessionController.AutoResumeAsync"/> performs the
/// shared Paused -&gt; Running SQL transition, raises the
/// AutoResumeSoundRequested event, and is idempotent. DispatcherTimer
/// behaviour (tick-driven auto-pause) is exercised by manual tests; this file
/// covers the direct-invoke logic the writer's callback will use.
/// </summary>
public class SessionControllerAutoResumeTests
{
    /// <summary>
    /// Run StartAsync (which lands in Paused), then directly invoke
    /// AutoResumeAsync. Asserts the SQL row sequence + state + Mode + event.
    /// </summary>
    [Fact]
    public async Task AutoResumeAsync_when_paused_closes_pause_row_opens_active_row_flips_state()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Auto-resume";
        await vm.StartAsync(f.Roster());

        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Paused);

            var resumeFired = 0;
            vm.AutoResumeSoundRequested += (_, _) => resumeFired++;

            await vm.AutoResumeAsync();

            vm.Mode.Should().Be(SessionWidgetMode.Running);
            f.ActiveSession.State.Should().Be(SessionRunState.Running);
            resumeFired.Should().Be(1, "AutoResumeSoundRequested must fire so MainWindow can play the WAV");

            // SQL state: row 1 closed (was is_pause=1 from Start), row 2 open (is_pause=0).
            var rows = await f.Db.QueryAsync(
                "SELECT is_pause, stop_time FROM session_time ORDER BY session_time_id",
                static r => (IsPause: r.GetInt32(0), StopTime: r.IsDBNull(1) ? null : r.GetString(1)));

            rows.Should().HaveCount(2);
            rows[0].IsPause.Should().Be(1);
            rows[0].StopTime.Should().NotBeNull();
            rows[1].IsPause.Should().Be(0);
            rows[1].StopTime.Should().BeNull();
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task AutoResumeAsync_is_noop_when_already_Running()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Already Running";
        await vm.StartAsync(f.Roster());
        await vm.ResumeCommand.ExecuteAsync(null); // -> Running

        try
        {
            vm.Mode.Should().Be(SessionWidgetMode.Running);
            var resumeFired = 0;
            vm.AutoResumeSoundRequested += (_, _) => resumeFired++;

            await vm.AutoResumeAsync();

            vm.Mode.Should().Be(SessionWidgetMode.Running);
            resumeFired.Should().Be(0, "AutoResumeAsync must be a no-op when already Running");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task AutoResumeAsync_is_noop_when_no_active_session()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        // No StartAsync — Mode is Config, _activeSessionId is null.

        var resumeFired = 0;
        vm.AutoResumeSoundRequested += (_, _) => resumeFired++;

        await vm.AutoResumeAsync();

        vm.Mode.Should().Be(SessionWidgetMode.Config);
        resumeFired.Should().Be(0);
    }

    [Fact]
    public async Task AutoResumeAsync_accumulates_paused_duration_correctly()
    {
        // After Start (lands Paused at t0), wait a moment so the pause window
        // accrues real elapsed time, then AutoResume. The post-resume
        // _pausedDurationAccumulated must include the start-to-resume span so
        // future elapsed-time displays exclude it.
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Pause Accum";

        await vm.StartAsync(f.Roster());
        try
        {
            // Sleep just long enough that a non-zero pause window exists.
            await Task.Delay(50);
            await vm.AutoResumeAsync();

            // TotalTimeMined right after resume should still read 00:00:00 —
            // the accumulator includes the 50 ms pause, so active duration is
            // (now - sessionStart) - paused ≈ 0.
            vm.TotalTimeMined.Should().Be("00:00:00");
            vm.Mode.Should().Be(SessionWidgetMode.Running);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }
}
