namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

/// <summary>
/// Reflection-grounded tests that the real widget VM types declare the
/// expected <see cref="StatRowAttribute"/> set. These tests guard
/// against accidentally dropping or misordering an attribute when the VMs
/// are edited.
/// </summary>
public class StatRowAttributeReflectionTests
{
    [Fact]
    public void PerPilotWidgetViewModel_has_8_reorderable_and_2_hideonly_attributed_properties()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(PerPilotWidgetViewModel), saved: null);

        resolution.Reorderable.Should().HaveCount(8);
        // [StatRow("Pilot",...)] lives on DisplayName (not PilotName) so the header
        // line updates live when the role changes ("PilotName" -> "PilotName [Booster]").
        resolution.Reorderable.Select(r => r.Key).Should().Equal(
            "DisplayName",
            "M3PerSecond",
            "IskPerHour",
            "TotalIskMined",
            "TotalM3Mined",
            "TotalM3FromCrits",
            "CritCount",
            "CritChance");

        resolution.HideOnly.Should().HaveCount(2);
        resolution.HideOnly.Select(r => r.Key).Should().Contain(new[] { "LastEventAt", "LastUpdated" });
    }

    [Fact]
    public void PerPilotWidgetViewModel_DisplayName_default_emphasis_is_Header0()
    {
        // The attribute lives on DisplayName, not PilotName.
        var resolution = StatRowsBuilder.Resolve(typeof(PerPilotWidgetViewModel), saved: null);
        var displayName = resolution.Reorderable.First(r => r.Key == "DisplayName");

        displayName.Emphasis.Should().Be(StatEmphasis.Header0);
    }

    [Fact]
    public void SessionStatsViewModel_has_10_reorderable_and_1_hideonly_attributed_properties()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SessionStatsViewModel), saved: null);

        resolution.Reorderable.Should().HaveCount(10);
        resolution.Reorderable.Select(r => r.Key).Should().Equal(
            "DisplaySessionName", // -2
            "TotalTimeMined",     // -1
            "M3PerSecond",        // 1
            "IskPerHour",         // 2
            "TotalIskMined",      // 3
            "TotalM3Mined",       // 4
            "TotalM3FromCrits",   // 5
            "CritCount",          // 6
            "CritChance",         // 7
            "CritIskPercent");    // 8

        resolution.HideOnly.Should().ContainSingle().Which.Key.Should().Be("LastUpdated");
    }

    [Fact]
    public void SessionStatsViewModel_DisplaySessionName_default_emphasis_is_Header1()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SessionStatsViewModel), saved: null);
        var name = resolution.Reorderable.First(r => r.Key == "DisplaySessionName");

        name.Emphasis.Should().Be(StatEmphasis.Header1);
    }

    [Fact]
    public void SessionStatsViewModel_TotalTimeMined_default_emphasis_is_Hero()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SessionStatsViewModel), saved: null);
        var time = resolution.Reorderable.First(r => r.Key == "TotalTimeMined");

        time.Emphasis.Should().Be(StatEmphasis.Hero);
    }

}
