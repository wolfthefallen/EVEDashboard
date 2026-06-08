namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// Plan 35: when a session starts, each pilot's role defaults to the role they
/// held in their most-recent prior <c>session_pilot</c> row. Pilots with no
/// prior history default to <see cref="PilotRole.Miner"/>.
/// </summary>
public class SessionControllerStartRoleDefaultTests
{
    [Fact]
    public async Task FirstTime_pilot_defaults_to_Miner()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "First Run";
        vm.Pilots[0].IsSelected = true;

        await vm.StartCommand.ExecuteAsync(null);

        try
        {
            var roles = await f.Db.QueryAsync(
                "SELECT pilot_id, role FROM session_pilot",
                static r => (PilotId: r.GetInt64(0), Role: r.GetString(1)));
            roles.Should().ContainSingle();
            roles[0].Role.Should().Be("Miner");

            f.ActiveSession.GetRole(roles[0].PilotId).Should().Be(PilotRole.Miner);
        }
        finally
        {
            await vm.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task Second_session_inherits_role_from_most_recent_prior_session_pilot_row()
    {
        using var f = new SessionControllerTestFixture();

        // Round 1: start a session with the pilot, change their role to Cyno, stop.
        using (var vm = f.CreateViewModel())
        {
            vm.SessionName = "First";
            vm.Pilots[0].IsSelected = true;
            await vm.StartCommand.ExecuteAsync(null);
            var pid = f.Characters[0].CharacterId;
            await vm.SetPilotRoleAsync(pid, PilotRole.Cyno);
            await vm.StopCommand.ExecuteAsync(null);
        }

        // Round 2: start a new session with the same pilot. Expected: role inherits Cyno.
        using (var vm = f.CreateViewModel())
        {
            vm.SessionName = "Second";
            vm.Pilots[0].IsSelected = true;
            await vm.StartCommand.ExecuteAsync(null);
            try
            {
                var roles = await f.Db.QueryAsync(
                    """
                    SELECT sp.role
                    FROM session_pilot sp
                    JOIN session s ON s.session_id = sp.session_id
                    WHERE s.name = 'Second'
                    """,
                    static r => r.GetString(0));
                roles.Should().ContainSingle();
                roles[0].Should().Be("Cyno");

                f.ActiveSession.GetRole(f.Characters[0].CharacterId).Should().Be(PilotRole.Cyno);
            }
            finally
            {
                await vm.StopCommand.ExecuteAsync(null);
            }
        }
    }

    [Fact]
    public async Task SeedRoles_runs_BEFORE_Set_so_GetRole_works_when_ActiveSessionChanged_fires()
    {
        // Decision 2.5 cross = A: the ordering invariant. If SeedRoles happened AFTER
        // Set(Running), an ActiveSessionChanged handler that reads GetRole would see Miner
        // (the defensive default) for every pilot instead of the resolved role.

        using var f = new SessionControllerTestFixture();

        // Pre-seed pilot history so the second session's lookup finds Booster.
        using (var vm = f.CreateViewModel())
        {
            vm.SessionName = "Setup";
            vm.Pilots[0].IsSelected = true;
            await vm.StartCommand.ExecuteAsync(null);
            await vm.SetPilotRoleAsync(f.Characters[0].CharacterId, PilotRole.Booster);
            await vm.StopCommand.ExecuteAsync(null);
        }

        // Subscribe to ActiveSessionChanged BEFORE Start; when the event fires, read
        // GetRole and capture what it sees.
        PilotRole? observedRole = null;
        f.ActiveSession.ActiveSessionChanged += (s, e) =>
        {
            if (f.ActiveSession.ActiveSessionId is not null)
            {
                observedRole = f.ActiveSession.GetRole(f.Characters[0].CharacterId);
            }
        };

        using (var vm = f.CreateViewModel())
        {
            vm.SessionName = "Observe";
            vm.Pilots[0].IsSelected = true;
            await vm.StartCommand.ExecuteAsync(null);
            try
            {
                observedRole.Should().Be(PilotRole.Booster,
                    "the role map MUST be seeded before Set fires ActiveSessionChanged");
            }
            finally
            {
                await vm.StopCommand.ExecuteAsync(null);
            }
        }
    }

    [Fact]
    public async Task Stop_clears_the_role_map()
    {
        using var f = new SessionControllerTestFixture();
        using var vm = f.CreateViewModel();
        vm.SessionName = "Run";
        vm.Pilots[0].IsSelected = true;
        await vm.StartCommand.ExecuteAsync(null);
        await vm.SetPilotRoleAsync(f.Characters[0].CharacterId, PilotRole.Cyno);

        // Precondition: role map is populated.
        f.ActiveSession.GetRole(f.Characters[0].CharacterId).Should().Be(PilotRole.Cyno);

        await vm.StopCommand.ExecuteAsync(null);

        // Post: GetRole returns the defensive default Miner because the map is empty.
        f.ActiveSession.GetRole(f.Characters[0].CharacterId).Should().Be(PilotRole.Miner);
    }
}
