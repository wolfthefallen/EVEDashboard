namespace EveDashboard.App.Wpf.Tests.Widgets.Pilot;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.Core.Sessions;
using EveDashboard.Stats.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// PerPilotWidgetViewModel test workhorse - covers ctor null-args, ApplyStats
/// field copy, ISK stub assertion (per <c>MEMORY.md -&gt; feedback_no_test_shimming</c>),
/// the no-op RefreshAsync override, and the LastUpdated stamp.
/// </summary>
public class PerPilotWidgetViewModelTests
{
    private static Func<long, PilotRole, CancellationToken, Task> NoOpCallback() =>
        (_, _, _) => Task.CompletedTask;

    private static PerPilotWidgetViewModel NewVm(
        long pilotId = 123,
        string name = "Pilot",
        PilotRole initialRole = PilotRole.Miner,
        FakeSettingsRepository? settings = null,
        ActiveSessionContext? activeSession = null) =>
        new(
            pilotId,
            name,
            initialRole,
            settings ?? new FakeSettingsRepository(),
            activeSession ?? new ActiveSessionContext(),
            NoOpCallback(),
            NullLogger<PerPilotWidgetViewModel>.Instance);

    [Fact]
    public void Ctor_throws_on_null_pilotName()
    {
        Action act = () => new PerPilotWidgetViewModel(
            123, null!, PilotRole.Miner, new FakeSettingsRepository(),
            new ActiveSessionContext(), NoOpCallback(),
            NullLogger<PerPilotWidgetViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pilotName");
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        Action act = () => new PerPilotWidgetViewModel(
            123, "Pilot", PilotRole.Miner, null!,
            new ActiveSessionContext(), NoOpCallback(),
            NullLogger<PerPilotWidgetViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Ctor_throws_on_null_activeSession()
    {
        Action act = () => new PerPilotWidgetViewModel(
            123, "Pilot", PilotRole.Miner, new FakeSettingsRepository(),
            null!, NoOpCallback(),
            NullLogger<PerPilotWidgetViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("activeSession");
    }

    [Fact]
    public void Ctor_throws_on_null_setRoleCallback()
    {
        Action act = () => new PerPilotWidgetViewModel(
            123, "Pilot", PilotRole.Miner, new FakeSettingsRepository(),
            new ActiveSessionContext(), null!,
            NullLogger<PerPilotWidgetViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("setRoleCallback");
    }

    [Fact]
    public void Ctor_throws_on_null_log()
    {
        Action act = () => new PerPilotWidgetViewModel(
            123, "Pilot", PilotRole.Miner, new FakeSettingsRepository(),
            new ActiveSessionContext(), NoOpCallback(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    [Fact]
    public void Ctor_assigns_pilotId_and_pilotName_immutably()
    {
        using var vm = NewVm(42, "TestPilot");
        vm.PilotId.Should().Be(42);
        vm.PilotName.Should().Be("TestPilot");
    }

    [Fact]
    public void ApplyStats_throws_on_null_stats()
    {
        using var vm = NewVm(1, "p");
        Action act = () => vm.ApplyStats(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("stats");
    }

    [Fact]
    public void ApplyStats_copies_all_seven_mining_fields_and_LastEventAt()
    {
        using var vm = NewVm(1, "p");
        var lastEvent = DateTimeOffset.Parse("2026-05-03T12:34:56Z");
        var stats = new CharacterStats(
            PilotId:           1,
            LastEventAt:       lastEvent,
            TotalM3Mined:      1234.5,
            M3PerSecond:       0.42,
            TotalIskMined:     0,
            IskPerHour:        0,
            TotalIskFromCrits: 0,
            CritIskPercent:    0,
            TotalM3FromCrits:  500.5,
            CritCount:         7,
            CritChance:        0.125,
            Role:              PilotRole.Miner);

        vm.ApplyStats(stats);

        vm.LastEventAt.Should().Be(lastEvent);
        vm.M3PerSecond.Should().Be(0.42);
        vm.TotalM3Mined.Should().Be(1234.5);
        vm.TotalM3FromCrits.Should().Be(500.5);
        vm.CritCount.Should().Be(7);
        vm.CritChance.Should().Be(0.125);
    }

    [Fact]
    public void ApplyStats_with_stub_zero_ISK_leaves_ISK_fields_at_zero()
    {
        using var vm = NewVm(1, "p");
        var stats = new CharacterStats(
            PilotId: 1, LastEventAt: null,
            TotalM3Mined: 100.0, M3PerSecond: 1.0,
            TotalIskMined: 0, IskPerHour: 0,
            TotalIskFromCrits: 0, CritIskPercent: 0,
            TotalM3FromCrits: 0, CritCount: 0, CritChance: 0,
            Role: PilotRole.Miner);

        vm.ApplyStats(stats);

        vm.TotalIskMined.Should().Be(0);
        vm.IskPerHour.Should().Be(0);
    }

    [Fact]
    public void ApplyStats_with_null_LastEventAt_leaves_field_null()
    {
        using var vm = NewVm(1, "p");
        var stats = new CharacterStats(
            PilotId: 1, LastEventAt: null,
            TotalM3Mined: 0, M3PerSecond: 0,
            TotalIskMined: 0, IskPerHour: 0,
            TotalIskFromCrits: 0, CritIskPercent: 0,
            TotalM3FromCrits: 0, CritCount: 0, CritChance: 0,
            Role: PilotRole.Miner);

        vm.ApplyStats(stats);

        vm.LastEventAt.Should().BeNull();
    }

    [Fact]
    public void ApplyStats_stamps_LastUpdated_to_a_recent_UTC_instant()
    {
        using var vm = NewVm(1, "p");
        var stats = new CharacterStats(1, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, PilotRole.Miner);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        vm.ApplyStats(stats);

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        vm.LastUpdated.Should().NotBeNull();
        vm.LastUpdated!.Value.Should().BeOnOrAfter(before);
        vm.LastUpdated!.Value.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task RefreshAsync_is_a_no_op_returning_completed_task()
    {
        using var vm = NewVm(1, "p");
        var task = vm.RefreshAsync();
        task.Should().BeSameAs(Task.CompletedTask);
        await task; // does not throw
    }

    [Fact]
    public void HasStatConfig_is_true_so_chrome_cog_button_renders()
    {
        using var vm = NewVm();
        vm.HasStatConfig.Should().BeTrue();
        vm.StatConfigPopup.Should().NotBeNull();
    }
}
