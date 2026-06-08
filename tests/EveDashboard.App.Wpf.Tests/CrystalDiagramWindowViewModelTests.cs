namespace EveDashboard.App.Wpf.Tests;

using EveDashboard.App.Wpf;
using EveDashboard.App.Wpf.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Plan 35 unit tests for <see cref="CrystalDiagramWindowViewModel"/>. Covers
/// the initialize-from-settings flow, the unknown-id fallback, and the
/// persistence write-back on user pick.
/// </summary>
public class CrystalDiagramWindowViewModelTests
{
    private static CrystalDiagramWindowViewModel NewVm(FakeSettingsRepository settings) =>
        new(settings, NullLogger<CrystalDiagramWindowViewModel>.Instance);

    [Fact]
    public async Task InitializeAsync_with_null_persisted_id_selects_all()
    {
        var settings = new FakeSettingsRepository { SelectedCrystalAnomaly = null };
        var vm = NewVm(settings);

        await vm.InitializeAsync();

        vm.SelectedEntry.Id.Should().Be("all");
    }

    [Fact]
    public async Task InitializeAsync_with_known_id_selects_matching_entry()
    {
        var settings = new FakeSettingsRepository { SelectedCrystalAnomaly = "griemeer" };
        var vm = NewVm(settings);

        await vm.InitializeAsync();

        vm.SelectedEntry.Id.Should().Be("griemeer");
    }

    [Fact]
    public async Task InitializeAsync_with_unknown_id_falls_back_to_all_and_does_not_crash()
    {
        var settings = new FakeSettingsRepository { SelectedCrystalAnomaly = "not-a-real-id" };
        var vm = NewVm(settings);

        await vm.InitializeAsync();

        vm.SelectedEntry.Id.Should().Be("all");
    }

    [Fact]
    public async Task InitializeAsync_does_not_persist_the_value_it_just_read()
    {
        var settings = new FakeSettingsRepository { SelectedCrystalAnomaly = "veldspar" };
        var vm = NewVm(settings);

        await vm.InitializeAsync();

        settings.SetSelectedCrystalAnomalyCount.Should().Be(0);
    }

    [Fact]
    public async Task Setting_SelectedEntry_persists_via_settings_repository()
    {
        var settings = new FakeSettingsRepository();
        var vm = NewVm(settings);
        await vm.InitializeAsync();

        var veldspar = vm.Entries.Single(e => e.Id == "veldspar");
        vm.SelectedEntry = veldspar;

        // Fire-and-forget write — give the continuation a chance to run.
        await Task.Yield();

        settings.SelectedCrystalAnomaly.Should().Be("veldspar");
        settings.SetSelectedCrystalAnomalyCount.Should().Be(1);
    }

    [Fact]
    public async Task Setting_SelectedEntry_to_same_value_does_not_double_persist()
    {
        var settings = new FakeSettingsRepository();
        var vm = NewVm(settings);
        await vm.InitializeAsync();

        var griemeer = vm.Entries.Single(e => e.Id == "griemeer");
        vm.SelectedEntry = griemeer;
        await Task.Yield();
        vm.SelectedEntry = griemeer;
        await Task.Yield();

        settings.SetSelectedCrystalAnomalyCount.Should().Be(1);
    }
}
