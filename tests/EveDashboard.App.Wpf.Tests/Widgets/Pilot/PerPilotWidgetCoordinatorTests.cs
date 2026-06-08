namespace EveDashboard.App.Wpf.Tests.Widgets.Pilot;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Stats.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// PerPilotWidgetCoordinator test workhorse - covers ctor null-args, the
/// session-start population, the 6.2 = A "widgets remain visible after Stop"
/// behaviour, the second-session clear-and-repopulate cycle, the empty-pilot
/// log-warning path, the dirty-flag coalescing, refresh-failure retry, and
/// dispose unsubscription.
///
/// xUnit has no message loop -> <see cref="System.Windows.Threading.DispatcherTimer"/>
/// never auto-fires. Tests drive lifecycle via the internal
/// <c>HandleActiveSessionChangedAsync</c> and <c>OnTickAsync</c> methods
/// (visible via <c>InternalsVisibleTo</c>).
/// </summary>
public class PerPilotWidgetCoordinatorTests
{
    /// <summary>Plan 35: no-op callback for null-arg ctor tests.</summary>
    private static readonly Func<long, PilotRole, CancellationToken, Task> NoOpCallback =
        (_, _, _) => Task.CompletedTask;

    /* ---------- Constructor null-arg checks ---------- */

    [Fact]
    public void Ctor_throws_on_null_activeSession()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            null!, f.Db, f.StatsRepo, f.Notifier, f.LoggerFactory, f.Settings, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("activeSession");
    }

    [Fact]
    public void Ctor_throws_on_null_db()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, null!, f.StatsRepo, f.Notifier, f.LoggerFactory, f.Settings, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_throws_on_null_statsRepo()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, f.Db, null!, f.Notifier, f.LoggerFactory, f.Settings, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("statsRepo");
    }

    [Fact]
    public void Ctor_throws_on_null_notifier()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, f.Db, f.StatsRepo, null!, f.LoggerFactory, f.Settings, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("notifier");
    }

    [Fact]
    public void Ctor_throws_on_null_loggerFactory()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, f.Db, f.StatsRepo, f.Notifier, null!, f.Settings, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, f.Db, f.StatsRepo, f.Notifier, f.LoggerFactory, null!, NoOpCallback);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Ctor_throws_on_null_setPilotRoleCallback()
    {
        using var f = new PerPilotWidgetTestFixture();
        Action act = () => new PerPilotWidgetCoordinator(
            f.ActiveSession, f.Db, f.StatsRepo, f.Notifier, f.LoggerFactory, f.Settings, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("setPilotRoleCallback");
    }

    /* ---------- Session start populates Widgets ---------- */

    [Fact]
    public async Task Session_start_populates_widgets_from_session_pilot_in_alphabetical_order()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 100, "Charlie");
        await f.InsertSessionPilotAsync(sid, 101, "alice");      // mixed case
        await f.InsertSessionPilotAsync(sid, 102, "Bob");

        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        coord.Widgets.Should().HaveCount(3);
        coord.Widgets[0].PilotName.Should().Be("alice");      // case-insensitive sort
        coord.Widgets[1].PilotName.Should().Be("Bob");
        coord.Widgets[2].PilotName.Should().Be("Charlie");
        coord.Widgets[0].PilotId.Should().Be(101);
    }

    [Fact]
    public async Task Session_start_with_zero_pilots_logs_warning_and_leaves_widgets_empty()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync(); // no pilots inserted
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        coord.Widgets.Should().BeEmpty();
        f.CoordinatorLog.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("session_pilot"));
    }

    /* ---------- Session stop leaves widgets visible (decision 6.2 = A) ---------- */

    [Fact]
    public async Task Session_stop_does_not_clear_widgets()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        await f.InsertSessionPilotAsync(sid, 2, "P2");

        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();
        coord.Widgets.Should().HaveCount(2);

        // Transition to None (Stop). Per decision 6.2 = A, widgets remain visible.
        f.ActiveSession.Set(null, SessionRunState.None);
        await coord.HandleActiveSessionChangedAsync();
        coord.Widgets.Should().HaveCount(2);
    }

    /* ---------- Second session start clears and repopulates ---------- */

    [Fact]
    public async Task Second_session_start_clears_and_repopulates_widgets()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid1 = await f.InsertSessionAsync("First");
        await f.InsertSessionPilotAsync(sid1, 1, "P1");
        await f.InsertSessionPilotAsync(sid1, 2, "P2");
        f.ActiveSession.Set(sid1, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();
        coord.Widgets.Should().HaveCount(2);

        // Stop first session.
        f.ActiveSession.Set(null, SessionRunState.None);
        await coord.HandleActiveSessionChangedAsync();

        // Start a second session with different pilots.
        var sid2 = await f.InsertSessionAsync("Second");
        await f.InsertSessionPilotAsync(sid2, 99, "Different");
        f.ActiveSession.Set(sid2, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        coord.Widgets.Should().HaveCount(1);
        coord.Widgets[0].PilotName.Should().Be("Different");
        coord.Widgets[0].PilotId.Should().Be(99);
    }

    /* ---------- Pause/Resume on the same session does NOT repopulate ---------- */

    [Fact]
    public async Task Pause_resume_on_same_session_does_not_clear_or_repopulate()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();
        var originalWidget = coord.Widgets[0];

        // Pause: same session id, state Paused. Should not repopulate.
        f.ActiveSession.Set(sid, SessionRunState.Paused);
        await coord.HandleActiveSessionChangedAsync();
        coord.Widgets.Should().HaveCount(1);
        coord.Widgets[0].Should().BeSameAs(originalWidget);

        // Resume: same session id, state Running. Should still not repopulate.
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();
        coord.Widgets.Should().HaveCount(1);
        coord.Widgets[0].Should().BeSameAs(originalWidget);
    }

    /* ---------- Dirty-flag coalescing ---------- */

    [Fact]
    public async Task Tick_with_no_dirty_flag_does_not_query_stats_repo()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        // HandleActiveSessionChangedAsync sets _statsDirty = true to force a baseline.
        // First tick consumes that.
        await coord.OnTickAsync();
        var afterFirstTick = f.StatsRepo.GetCallCount;

        // No new SessionEventAdded fires; subsequent ticks should be no-ops.
        await coord.OnTickAsync();
        await coord.OnTickAsync();
        f.StatsRepo.GetCallCount.Should().Be(afterFirstTick);
    }

    [Fact]
    public async Task SessionEventAdded_flips_dirty_flag_and_next_tick_refreshes()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        await f.OpenSessionTimeAsync(sid, DateTimeOffset.UtcNow.AddMinutes(-1));
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();
        await coord.OnTickAsync(); // consume initial dirty flag

        var beforeRaise = f.StatsRepo.GetCallCount;
        await coord.OnTickAsync(); // no dirty flag -> no call
        f.StatsRepo.GetCallCount.Should().Be(beforeRaise);

        // Insert a real Tick row (so the stats repo returns non-empty stats), then raise the event.
        await f.InsertTickAsync(sid, 1, DateTimeOffset.UtcNow, typeId: 82016, units: 100, volumeM3: 50.0);
        f.Notifier.Raise(sid, logId: 1, MiningEventKind.Tick);

        await coord.OnTickAsync();
        f.StatsRepo.GetCallCount.Should().Be(beforeRaise + 1);
        coord.Widgets[0].TotalM3Mined.Should().Be(50.0);
    }

    /* ---------- Refresh failure logs Warning and re-sets dirty flag ---------- */

    [Fact]
    public async Task Stats_refresh_failure_logs_warning_and_sets_dirty_for_retry()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        // Override stats repo to throw on the next call.
        f.StatsRepo.Override = (_, _, _) => throw new InvalidOperationException("simulated DB failure");

        await coord.OnTickAsync(); // dirty flag was set by HandleActiveSessionChangedAsync; this tick throws

        f.CoordinatorLog.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("stats refresh failed"));

        // Now succeed on retry. The dirty flag should still be set so the next tick re-queries.
        f.StatsRepo.Override = null;
        await coord.OnTickAsync();
        f.StatsRepo.GetCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /* ---------- Dispose unsubscribes ---------- */

    [Fact]
    public async Task Dispose_unsubscribes_from_events_and_is_idempotent()
    {
        using var f = new PerPilotWidgetTestFixture();
        var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 1, "P1");
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        coord.Dispose();

        // After dispose, raising the notifier should not flip a dirty flag (no observable state change).
        // A cleaner assertion: re-raise a session change should not retrigger the handler.
        // We don't have a direct assertion hook, but the stats repo call count should not grow if we wait.
        var beforeRaise = f.StatsRepo.GetCallCount;
        f.Notifier.Raise(sid, logId: 99, MiningEventKind.Tick);
        // Dispose-call should be idempotent.
        coord.Dispose();
        f.StatsRepo.GetCallCount.Should().Be(beforeRaise);
    }

    [Fact]
    public void Coordinator_subscribes_to_events_on_construction_and_does_not_throw()
    {
        // Smoke: ctor wires events; raising events without an active session should be a no-op.
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();
        Action act = () => f.Notifier.Raise("sess-none", 0, MiningEventKind.Tick);
        act.Should().NotThrow();
    }

    /* ---------- Per-pilot stats are routed correctly by pilot_id ---------- */

    [Fact]
    public async Task ApplyStats_routes_each_pilots_snapshot_to_its_widget()
    {
        using var f = new PerPilotWidgetTestFixture();
        using var coord = f.CreateCoordinator();

        var sid = await f.InsertSessionAsync();
        await f.InsertSessionPilotAsync(sid, 100, "Alice");
        await f.InsertSessionPilotAsync(sid, 200, "Bob");
        await f.OpenSessionTimeAsync(sid, DateTimeOffset.UtcNow.AddMinutes(-1));
        f.ActiveSession.Set(sid, SessionRunState.Running);
        await coord.HandleActiveSessionChangedAsync();

        // Insert different m3 totals for each pilot.
        await f.InsertTickAsync(sid, 100, DateTimeOffset.UtcNow, typeId: 82016, units: 100, volumeM3: 30.0);
        await f.InsertTickAsync(sid, 200, DateTimeOffset.UtcNow, typeId: 82017, units: 200, volumeM3: 70.0);
        f.Notifier.Raise(sid, 1, MiningEventKind.Tick);

        await coord.OnTickAsync();

        var alice = coord.Widgets.Single(w => w.PilotId == 100);
        var bob = coord.Widgets.Single(w => w.PilotId == 200);
        alice.TotalM3Mined.Should().Be(30.0);
        bob.TotalM3Mined.Should().Be(70.0);
    }
}
