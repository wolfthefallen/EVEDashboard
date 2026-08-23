namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Integration tests over <see cref="SessionStatsViewModel"/>'s
/// Stats collection + visibility flag + SettingChanged rebuild path. Mirrors
/// the per-pilot tests at one widget level up (singleton key, no pilot id
/// suffix).
/// </summary>
public class SessionStatsViewModelStatsTests
{
    private static (SessionControllerTestFixture, SessionStatsViewModel, FakeSettingsRepository) NewVm()
    {
        var f = new SessionControllerTestFixture();
        var ctrl = f.CreateViewModel();
        var settings = new FakeSettingsRepository();
        var vm = new SessionStatsViewModel(ctrl, settings, NullLogger<SessionStatsViewModel>.Instance);
        vm.RebuildStatsAsync().GetAwaiter().GetResult();
        return (f, vm, settings);
    }

    [Fact]
    public void Default_config_renders_10_reorderable_stats_in_default_order()
    {
        var (f, vm, _) = NewVm();
        using (f) using (vm)
        {
            vm.Stats.Select(r => r.Key).Should().Equal(
                "DisplaySessionName",
                "TotalTimeMined",
                "M3PerSecond",
                "IskPerHour",
                "TotalIskMined",
                "TotalM3Mined",
                "TotalM3FromCrits",
                "CritCount",
                "CritChance",
                "CritIskPercent");
        }
    }

    [Fact]
    public void DisplaySessionName_carries_Header1_emphasis_and_TotalTimeMined_carries_Hero()
    {
        var (f, vm, _) = NewVm();
        using (f) using (vm)
        {
            vm.Stats.First(r => r.Key == "DisplaySessionName").Emphasis.Should().Be(StatEmphasis.Header1);
            vm.Stats.First(r => r.Key == "TotalTimeMined").Emphasis.Should().Be(StatEmphasis.Hero);
        }
    }

    [Fact]
    public async Task Saved_config_with_reordering_drives_Stats_order()
    {
        var (f, vm, settings) = NewVm();
        using (f) using (vm)
        {
            await settings.SetSessionStatsConfigAsync(new WidgetStatsConfig(new[]
            {
                new StatEntry("CritChance", false, StatEmphasis.None),
                new StatEntry("M3PerSecond", false, StatEmphasis.None),
            }));
            // SettingChanged was raised by SetSessionStatsConfigAsync; allow the
            // async rebuild to settle.
            await Task.Yield();
            await Task.Yield();

            vm.Stats.Select(r => r.Key).Should().StartWith(new[] { "CritChance", "M3PerSecond" });
        }
    }

    [Fact]
    public async Task Hidden_stat_is_filtered_from_Stats()
    {
        var (f, vm, settings) = NewVm();
        using (f) using (vm)
        {
            await settings.SetSessionStatsConfigAsync(new WidgetStatsConfig(new[]
            {
                new StatEntry("DisplaySessionName", true, StatEmphasis.Header1),
            }));
            await Task.Yield();
            await Task.Yield();

            vm.Stats.Select(r => r.Key).Should().NotContain("DisplaySessionName");
        }
    }

    [Fact]
    public async Task Hidden_LastUpdated_flips_IsLastUpdatedVisible_to_false()
    {
        var (f, vm, settings) = NewVm();
        using (f) using (vm)
        {
            await settings.SetSessionStatsConfigAsync(new WidgetStatsConfig(new[]
            {
                new StatEntry("LastUpdated", true, StatEmphasis.None),
            }));
            await Task.Yield();
            await Task.Yield();

            vm.IsLastUpdatedVisible.Should().BeFalse();
        }
    }

    [Fact]
    public async Task SettingChanged_for_unrelated_key_does_NOT_trigger_rebuild()
    {
        var (f, vm, settings) = NewVm();
        using (f) using (vm)
        {
            var originalKeys = vm.Stats.Select(r => r.Key).ToArray();

            settings.RaiseSettingChangedForTests("theme");
            await Task.Yield();
            await Task.Yield();

            vm.Stats.Select(r => r.Key).Should().Equal(originalKeys);
        }
    }

    [Fact]
    public void Forwarded_property_change_updates_matching_StatRowViewModel_FormattedValue()
    {
        var (f, vm, _) = NewVm();
        using (f) using (vm)
        {
            var ctrl = f.ActiveSession;
            // Get the controller via the fixture (it's already wired into vm).
            // The forwarding VM raises OnPropertyChanged via OnControllerPropertyChanged.
            var sessionCtrl = (SessionController)typeof(SessionStatsViewModel).GetField(
                "_controller", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(vm)!;

            var m3Row = vm.Stats.First(r => r.Key == "M3PerSecond");
            sessionCtrl.M3PerSecond = 5.5;

            m3Row.FormattedValue.Should().Contain("5.5");
        }
    }
}
