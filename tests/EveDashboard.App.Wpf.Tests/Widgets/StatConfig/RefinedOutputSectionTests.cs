namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;
using FluentAssertions;

/// <summary>
/// Plan 43: the opt-in "Refined output" section on
/// <see cref="StatConfigurableWidgetViewModel"/> — merge (a material never
/// disappears once seen; quantities grow; sorted by quantity desc) + visibility
/// resolution (explicit override wins, else the auto-show default). Exercised
/// through a minimal concrete test widget.
/// </summary>
public class RefinedOutputSectionTests
{
    /// <summary>Minimal concrete widget that opts into the refined section and exposes the protected merge.</summary>
    private sealed class TestWidget : StatConfigurableWidgetViewModel
    {
        public TestWidget(ISettingsRepository settings, string scopeKey)
            : base(
                vmType:     typeof(TestWidget),
                settings:   settings,
                settingKey: "test_stats_config",
                load:       _ => Task.FromResult<WidgetStatsConfig?>(null),
                save:       (_, _) => Task.CompletedTask,
                log:        null)
        {
            // EnableRefinedOutput hydrates auto-show + overrides synchronously here,
            // because FakeSettingsRepository returns already-completed tasks.
            EnableRefinedOutput(scopeKey);
        }

        public void Merge(params RefinedMaterialAmount[] snapshot) => MergeRefinedOutput(snapshot);

        public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static RefinedMaterialAmount Mat(long id, string name, long qty) => new(id, name, qty);

    [Fact]
    public void AutoShow_on_no_override_shows_a_newly_merged_material()
    {
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000));

        w.HasRefinedOutput.Should().BeTrue();
        w.RefinedOutput.Should().ContainSingle().Which.Name.Should().Be("Tritanium");
    }

    [Fact]
    public void AutoShow_off_no_override_hides_a_newly_merged_material()
    {
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = false };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000));

        w.RefinedOutput.Should().BeEmpty("auto-show off means a new material stays hidden until explicitly shown");
    }

    [Fact]
    public void Explicit_hide_overrides_auto_show_on_sticky()
    {
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true };
        settings.RefinedMaterialsVisibility[string.Empty] = new Dictionary<long, bool> { [34] = false };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000), Mat(35, "Pyerite", 500));

        // Tritanium explicitly hidden (sticky), Pyerite follows auto-show (visible).
        w.RefinedOutput.Select(r => r.Name).Should().Equal("Pyerite");
    }

    [Fact]
    public void Explicit_show_overrides_auto_show_off()
    {
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = false };
        settings.RefinedMaterialsVisibility[string.Empty] = new Dictionary<long, bool> { [34] = true };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000), Mat(35, "Pyerite", 500));

        // Tritanium explicitly shown; Pyerite hidden (auto-show off).
        w.RefinedOutput.Select(r => r.Name).Should().Equal("Tritanium");
    }

    [Fact]
    public void Merge_updates_quantity_for_a_seen_material_and_sorts_by_quantity_desc()
    {
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 100), Mat(35, "Pyerite", 500));
        // A later snapshot with grown quantities; Tritanium overtakes Pyerite.
        w.Merge(Mat(34, "Tritanium", 900), Mat(35, "Pyerite", 500));

        w.RefinedOutput.Select(r => (r.Name, r.Quantity))
            .Should().Equal(("Tritanium", 900L), ("Pyerite", 500L));
    }
}
