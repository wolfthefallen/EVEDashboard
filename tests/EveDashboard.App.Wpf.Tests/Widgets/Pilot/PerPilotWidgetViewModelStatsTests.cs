namespace EveDashboard.App.Wpf.Tests.Widgets.Pilot;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Integration tests over the per-pilot VM's Stats collection +
/// visibility flags + SettingChanged-driven rebuild. Verifies the wiring
/// from the [StatRow] attributes through StatRowsBuilder /
/// StatRowsRenderer / SettingChanged into the renderable Stats.
/// </summary>
public class PerPilotWidgetViewModelStatsTests
{
    private static PerPilotWidgetViewModel NewVm(FakeSettingsRepository settings, long pilotId = 42, string name = "Alice")
    {
        var vm = new PerPilotWidgetViewModel(
            pilotId,
            name,
            PilotRole.Miner,
            settings,
            new ActiveSessionContext(),
            (_, _, _) => Task.CompletedTask,
            NullLogger<PerPilotWidgetViewModel>.Instance);
        // Wait for the ctor's fire-and-forget RebuildStatsAsync to settle.
        vm.RebuildStatsAsync().GetAwaiter().GetResult();
        return vm;
    }

    [Fact]
    public void Default_config_renders_8_reorderable_stats_in_default_order()
    {
        var settings = new FakeSettingsRepository();
        using var vm = NewVm(settings);

        vm.Stats.Select(r => r.Key).Should().Equal(
            "DisplayName",
            "M3PerSecond",
            "IskPerHour",
            "TotalIskMined",
            "TotalM3Mined",
            "TotalM3FromCrits",
            "CritCount",
            "CritChance");
    }

    [Fact]
    public void Default_config_PilotName_carries_Header0_emphasis()
    {
        var settings = new FakeSettingsRepository();
        using var vm = NewVm(settings);
        vm.Stats.First(r => r.Key == "DisplayName").Emphasis.Should().Be(StatEmphasis.Header0);
    }

    [Fact]
    public void Default_config_visibility_flags_are_true()
    {
        var settings = new FakeSettingsRepository();
        using var vm = NewVm(settings);

        vm.IsLastUpdatedVisible.Should().BeTrue();
        vm.IsLastEventAtVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Saved_config_with_reordering_drives_Stats_order()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        await settings.SetPerPilotStatsConfigAsync(pilotId, new WidgetStatsConfig(new[]
        {
            new StatEntry("CritChance",  false, StatEmphasis.None),
            new StatEntry("M3PerSecond", false, StatEmphasis.None),
            new StatEntry("DisplayName",   false, StatEmphasis.Header0),
        }));
        using var vm = NewVm(settings, pilotId);

        // Saved keys come first; missing ones inserted at their DefaultOrder slot.
        // CritChance(7), M3PerSecond(1), PilotName(0) saved -> then IskPerHour(2),
        // TotalIskMined(3), TotalM3Mined(4), TotalM3FromCrits(5), CritCount(6) inserted.
        vm.Stats.Select(r => r.Key).Should().StartWith(
            new[] { "CritChance", "M3PerSecond", "DisplayName" });
    }

    [Fact]
    public async Task Hidden_stat_is_filtered_from_Stats()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        await settings.SetPerPilotStatsConfigAsync(pilotId, new WidgetStatsConfig(new[]
        {
            new StatEntry("DisplayName",   true,  StatEmphasis.Header0),
            new StatEntry("M3PerSecond", false, StatEmphasis.None),
        }));
        using var vm = NewVm(settings, pilotId);

        vm.Stats.Select(r => r.Key).Should().NotContain("DisplayName");
    }

    [Fact]
    public async Task Hidden_LastEventAt_flips_IsLastEventAtVisible_to_false()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        await settings.SetPerPilotStatsConfigAsync(pilotId, new WidgetStatsConfig(new[]
        {
            new StatEntry("LastEventAt", true, StatEmphasis.None),
        }));
        using var vm = NewVm(settings, pilotId);

        vm.IsLastEventAtVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Hidden_LastUpdated_flips_IsLastUpdatedVisible_to_false()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        await settings.SetPerPilotStatsConfigAsync(pilotId, new WidgetStatsConfig(new[]
        {
            new StatEntry("LastUpdated", true, StatEmphasis.None),
        }));
        using var vm = NewVm(settings, pilotId);

        vm.IsLastUpdatedVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SettingChanged_for_THIS_pilot_triggers_rebuild()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        using var vm = NewVm(settings, pilotId);
        var originalKeys = vm.Stats.Select(r => r.Key).ToArray();

        // Mutate the saved config and raise SettingChanged.
        settings.PerPilotStatsConfigs[pilotId] = new WidgetStatsConfig(new[]
        {
            new StatEntry("CritChance", false, StatEmphasis.None),
        });
        settings.RaiseSettingChangedForTests($"per_pilot_stats_config.{pilotId}");

        // Wait briefly for the async rebuild to complete (FakeSettingsRepository's
        // Get*Async returns synchronously so the rebuild should be done by the
        // next continuation).
        await Task.Yield();
        await Task.Yield();

        vm.Stats.First().Key.Should().Be("CritChance");
        vm.Stats.Select(r => r.Key).Should().NotEqual(originalKeys);
    }

    [Fact]
    public async Task SettingChanged_for_DIFFERENT_pilot_does_NOT_trigger_rebuild()
    {
        var settings = new FakeSettingsRepository();
        const long thisPilot = 42;
        const long otherPilot = 99;
        using var vm = NewVm(settings, thisPilot);
        var originalKeys = vm.Stats.Select(r => r.Key).ToArray();

        settings.PerPilotStatsConfigs[thisPilot] = new WidgetStatsConfig(new[]
        {
            new StatEntry("CritChance", false, StatEmphasis.None),
        });
        settings.RaiseSettingChangedForTests($"per_pilot_stats_config.{otherPilot}");

        await Task.Yield();
        await Task.Yield();

        vm.Stats.Select(r => r.Key).Should().Equal(originalKeys);
    }

    [Fact]
    public async Task SettingChanged_for_unrelated_key_does_NOT_trigger_rebuild()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        using var vm = NewVm(settings, pilotId);
        var originalKeys = vm.Stats.Select(r => r.Key).ToArray();

        settings.RaiseSettingChangedForTests("theme");

        await Task.Yield();
        await Task.Yield();

        vm.Stats.Select(r => r.Key).Should().Equal(originalKeys);
    }

    [Fact]
    public void Stat_property_change_updates_matching_StatRowViewModel_FormattedValue()
    {
        var settings = new FakeSettingsRepository();
        using var vm = NewVm(settings);
        var m3Row = vm.Stats.First(r => r.Key == "M3PerSecond");

        vm.M3PerSecond = 3.14;

        m3Row.FormattedValue.Should().Contain("3.14");
    }

    [Fact]
    public void Dispose_unsubscribes_from_settings_so_post_dispose_changes_dont_rebuild()
    {
        var settings = new FakeSettingsRepository();
        const long pilotId = 42;
        var vm = NewVm(settings, pilotId);

        vm.Dispose();

        // After dispose, raising the matching SettingChanged should not throw nor rebuild.
        Action act = () => settings.RaiseSettingChangedForTests($"per_pilot_stats_config.{pilotId}");
        act.Should().NotThrow();
    }
}
