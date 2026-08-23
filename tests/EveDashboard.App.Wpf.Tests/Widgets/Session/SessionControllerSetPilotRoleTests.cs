namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// <c>SessionController.SetPilotRoleAsync</c> UPDATE-s
/// <c>session_pilot.role</c>, mirrors the change into
/// <c>ActiveSessionContext</c>'s role map (which fires
/// <see cref="IActiveSessionContext.RolesChanged"/>), and flips the
/// <c>_statsDirty</c> flag.
/// </summary>
public class SessionControllerSetPilotRoleTests
{
    [Fact]
    public async Task SetPilotRoleAsync_throws_when_no_session_active()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        // Do not Start — no active session.

        Func<Task> act = async () =>
            await vm.SetPilotRoleAsync(f.Characters[0].CharacterId, PilotRole.Booster);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPilotRoleAsync_updates_session_pilot_role()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";
        await vm.StartAsync(f.Roster());

        try
        {
            var pid = f.Characters[0].CharacterId;
            await vm.SetPilotRoleAsync(pid, PilotRole.Cyno);

            var role = await f.Db.QueryScalarAsync<string>(
                "SELECT role FROM session_pilot WHERE pilot_id = $pid",
                new Dictionary<string, object?> { ["$pid"] = pid });
            role.Should().Be("Cyno");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task SetPilotRoleAsync_updates_in_memory_role_map()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";
        await vm.StartAsync(f.Roster());

        try
        {
            var pid = f.Characters[0].CharacterId;
            await vm.SetPilotRoleAsync(pid, PilotRole.Combat);

            f.ActiveSession.GetRole(pid).Should().Be(PilotRole.Combat);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task SetPilotRoleAsync_fires_RolesChanged_with_correct_payload()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";
        await vm.StartAsync(f.Roster());

        try
        {
            PilotRoleChangedEventArgs? captured = null;
            f.ActiveSession.RolesChanged += (s, e) => captured = e;

            var pid = f.Characters[0].CharacterId;
            await vm.SetPilotRoleAsync(pid, PilotRole.Booster);

            captured.Should().NotBeNull();
            captured!.PilotId.Should().Be(pid);
            captured.OldRole.Should().Be(PilotRole.Miner);
            captured.NewRole.Should().Be(PilotRole.Booster);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task SetPilotRoleAsync_no_op_when_role_unchanged_does_not_fire_event()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";
        await vm.StartAsync(f.Roster());

        try
        {
            var pid = f.Characters[0].CharacterId;
            // Default is Miner; re-set to Miner should NOT fire the event.
            var fireCount = 0;
            f.ActiveSession.RolesChanged += (s, e) => fireCount++;

            await vm.SetPilotRoleAsync(pid, PilotRole.Miner);

            fireCount.Should().Be(0, "no event should fire when the role doesn't actually change");
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }
}
