namespace EveDashboard.App.Wpf.Tests.Dialogs;

using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Sessions;
using EveDashboard.Stats.Fleets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// <see cref="SessionConfigDialogViewModel"/> owns the fleet list
/// (single-select), the Create-Fleet flow, and the Start command. A fleet is
/// the only way to start a session; Start drives the controller's
/// <see cref="SessionController.StartAsync(IReadOnlyList{FleetMember}, System.Threading.CancellationToken)"/>
/// and closes the dialog on success.
/// </summary>
public class SessionConfigDialogViewModelTests
{
    private static SqliteFleetRepository Repo(SessionControllerTestFixture f) =>
        new(f.Db, NullLogger<SqliteFleetRepository>.Instance);

    private static SessionConfigDialogViewModel BuildVm(
        SessionControllerTestFixture f, SessionController controller, IFleetRepository repo) =>
        new(controller, f.Characters, repo, f.Settings, f.ConfirmStop, NullLogger<SessionConfigDialogViewModel>.Instance);

    /// <summary>Create a fleet directly through the repo (the discoverable test pilots, all Miner).</summary>
    private static async Task<string?> SeedFleetAsync(
        SessionControllerTestFixture f, IFleetRepository repo, string name, int pilotCount = 1)
    {
        var members = f.Characters.Take(pilotCount)
            .Select(c => new FleetMember(c.CharacterId, c.Name, PilotRole.Miner))
            .ToList();
        var result = await repo.CreateFleetAsync(name, members);
        return result.FleetId;
    }

    /* ---------- ctor ---------- */

    [Fact]
    public void Ctor_throws_on_null_controller()
    {
        using var f = new SessionControllerTestFixture();
        Action act = () => new SessionConfigDialogViewModel(null!, f.Characters, Repo(f), f.Settings, f.ConfirmStop, NullLogger<SessionConfigDialogViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("controller");
    }

    [Fact]
    public void Ctor_throws_on_null_fleetRepo()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        Action act = () => new SessionConfigDialogViewModel(controller, f.Characters, null!, f.Settings, f.ConfirmStop, NullLogger<SessionConfigDialogViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("fleetRepo");
    }

    /* ---------- Start CanExecute ---------- */

    [Fact]
    public async Task StartCommand_CanExecute_false_when_no_fleet_selected()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = BuildVm(f, controller, Repo(f));
        await vm.InitializeAsync();
        vm.SessionName = "Run";

        vm.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StartCommand_CanExecute_false_when_name_blank_even_with_fleet_selected()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Alpha");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();
        vm.Fleets[0].IsSelected = true;
        vm.SessionName = "   ";

        vm.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StartCommand_CanExecute_true_when_fleet_selected_and_name_present()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Alpha");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();
        vm.Fleets[0].IsSelected = true;
        vm.SessionName = "Run";

        vm.StartCommand.CanExecute(null).Should().BeTrue();
    }

    /* ---------- InitializeAsync ---------- */

    [Fact]
    public async Task InitializeAsync_with_no_fleets_sets_HasNoFleets_and_selects_nothing()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = BuildVm(f, controller, Repo(f));

        await vm.InitializeAsync();

        vm.HasNoFleets.Should().BeTrue();
        vm.SelectedFleet.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_auto_selects_the_default_fleet()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Alpha");
        var defaultId = await SeedFleetAsync(f, repo, "Bravo");
        f.Settings.DefaultFleetId = defaultId;   // make Bravo the default
        using var vm = BuildVm(f, controller, repo);

        await vm.InitializeAsync();

        vm.SelectedFleet.Should().NotBeNull();
        vm.SelectedFleet!.Name.Should().Be("Bravo");
        vm.SelectedFleet.IsSelected.Should().BeTrue();
    }

    /* ---------- single-select ---------- */

    [Fact]
    public async Task Selecting_another_fleet_deselects_the_first()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Alpha");
        await SeedFleetAsync(f, repo, "Bravo");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();

        vm.Fleets[0].IsSelected = true;
        vm.Fleets[1].IsSelected = true;

        vm.Fleets[0].IsSelected.Should().BeFalse("single-select must deselect the first fleet");
        vm.SelectedFleet.Should().BeSameAs(vm.Fleets[1]);
    }

    /* ---------- Start execution ---------- */

    [Fact]
    public async Task StartCommand_with_discoverable_fleet_starts_session_and_closes_dialog()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Alpha");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();
        vm.Fleets[0].IsSelected = true;
        vm.SessionName = "Fleet run";

        await vm.StartCommand.ExecuteAsync(null);

        try
        {
            vm.ShouldClose.Should().BeTrue("Start succeeded so the dialog closes");
            vm.StartError.Should().BeNull();
            controller.Mode.Should().Be(SessionWidgetMode.Paused);
        }
        finally
        {
            await controller.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task StartCommand_with_no_discoverable_pilots_sets_StartError_and_keeps_dialog_open()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        // A fleet whose only pilot id is NOT a discoverable character.
        await repo.CreateFleetAsync("Ghosts", new[] { new FleetMember(999_999L, "Ghost", PilotRole.Miner) });
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();
        vm.Fleets[0].IsSelected = true;
        vm.SessionName = "Ghost run";

        await vm.StartCommand.ExecuteAsync(null);

        vm.ShouldClose.Should().BeFalse("Start is blocked when no fleet pilot is discoverable");
        vm.StartError.Should().NotBeNullOrEmpty();
        controller.Mode.Should().Be(SessionWidgetMode.Config, "no session was started");
    }

    /* ---------- Create / Save fleet ---------- */

    [Fact]
    public async Task CreateFleet_reveals_picker_populated_with_all_characters()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = BuildVm(f, controller, Repo(f));
        await vm.InitializeAsync();

        vm.CreateFleetCommand.Execute(null);

        vm.IsCreatingFleet.Should().BeTrue();
        vm.PilotPicker.Should().HaveCount(f.Characters.Count);
        vm.AddFleetCommand.CanExecute(null).Should().BeFalse("no pilot picked yet");
    }

    [Fact]
    public async Task SaveFleet_persists_the_fleet_and_selects_it()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();

        vm.CreateFleetCommand.Execute(null);
        vm.PilotPicker[0].IsSelected = true;
        vm.PilotPicker[0].Role = PilotRole.Booster;
        vm.AddFleetCommand.Execute(null);
        vm.NewFleetName = "My Fleet";
        await vm.SaveFleetCommand.ExecuteAsync(null);

        vm.IsCreatingFleet.Should().BeFalse();
        vm.Fleets.Should().ContainSingle(r => r.Name == "My Fleet");
        vm.SelectedFleet.Should().NotBeNull();
        vm.SelectedFleet!.Name.Should().Be("My Fleet");

        // The role chosen in the picker round-trips into the saved fleet.
        var saved = await repo.GetFleetsAsync();
        saved.Should().ContainSingle();
        saved[0].Members.Should().ContainSingle().Which.Role.Should().Be(PilotRole.Booster);
    }

    [Fact]
    public async Task SaveFleet_with_duplicate_name_sets_SaveError_and_stays_in_confirm()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        await SeedFleetAsync(f, repo, "Dup");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();

        vm.CreateFleetCommand.Execute(null);
        vm.PilotPicker[0].IsSelected = true;
        vm.AddFleetCommand.Execute(null);
        vm.NewFleetName = "Dup";
        await vm.SaveFleetCommand.ExecuteAsync(null);

        vm.SaveError.Should().NotBeNullOrEmpty();
        vm.IsConfirmingFleet.Should().BeTrue("the confirm panel stays open so the user can fix the name");
        (await repo.GetFleetsAsync()).Should().ContainSingle("the duplicate was not persisted");
    }

    /* ---------- manage: make default / delete ---------- */

    [Fact]
    public async Task MakeDefault_sets_the_setting_and_marks_the_row()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        var id = await SeedFleetAsync(f, repo, "Alpha");
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();

        await vm.MakeDefaultCommand.ExecuteAsync(vm.Fleets[0]);

        f.Settings.DefaultFleetId.Should().Be(id);
        vm.Fleets[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_removes_the_fleet_and_clears_the_default_when_it_was_default()
    {
        using var f = new SessionControllerTestFixture(dialogResult: true); // confirm dialog returns true
        using var controller = f.CreateViewModel();
        var repo = Repo(f);
        var id = await SeedFleetAsync(f, repo, "Alpha");
        f.Settings.DefaultFleetId = id;
        using var vm = BuildVm(f, controller, repo);
        await vm.InitializeAsync();

        await vm.DeleteFleetCommand.ExecuteAsync(vm.Fleets[0]);

        vm.Fleets.Should().BeEmpty();
        f.Settings.DefaultFleetId.Should().BeNull("deleting the default fleet clears the default pointer");
        (await repo.GetFleetsAsync()).Should().BeEmpty();
    }

    /* ---------- sort persistence ---------- */

    [Fact]
    public async Task Changing_the_sort_order_persists_it()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = BuildVm(f, controller, Repo(f));
        await vm.InitializeAsync();

        vm.SortOrder = PilotSortOrder.NameAsc;

        f.Settings.PilotSortOrder.Should().Be(PilotSortOrder.NameAsc);
    }

    /* ---------- reset ---------- */

    [Fact]
    public async Task ResetForNewOpen_clears_transient_create_and_error_state()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = BuildVm(f, controller, Repo(f));
        await vm.InitializeAsync();
        vm.CreateFleetCommand.Execute(null);
        vm.NewFleetName = "WIP";

        vm.ResetForNewOpen();

        vm.ShouldClose.Should().BeFalse();
        vm.IsCreatingFleet.Should().BeFalse();
        vm.IsConfirmingFleet.Should().BeFalse();
        vm.NewFleetName.Should().BeEmpty();
        vm.StartError.Should().BeNull();
    }
}
