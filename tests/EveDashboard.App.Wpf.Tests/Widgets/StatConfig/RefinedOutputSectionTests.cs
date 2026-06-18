namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;
using FluentAssertions;

/// <summary>
/// Plan 43: the opt-in "Refined output" section on
/// <see cref="StatConfigurableWidgetViewModel"/> — replace-from-snapshot (the
/// section mirrors the latest snapshot exactly; within a session quantities grow
/// because snapshots are cumulative, and an empty snapshot clears the section —
/// bug 1, 2026-06-18; sorted by quantity desc) + visibility resolution (explicit
/// override wins, else the auto-show default). Exercised through a minimal
/// concrete test widget.
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

    [Fact]
    public void Empty_snapshot_clears_the_section()
    {
        // Bug 1 (2026-06-18): a new session pushes an empty snapshot; the section
        // MUST reset. Before the fix the empty merge was a no-op and the prior
        // session's materials persisted in the singleton SessionStats widget.
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000), Mat(35, "Pyerite", 500));
        w.RefinedOutput.Should().HaveCount(2);

        w.Merge(/* empty snapshot = new session with no refined output yet */);

        w.RefinedOutput.Should().BeEmpty("an empty snapshot (new session) clears the refined-output section");
    }

    [Fact]
    public void Material_absent_from_a_later_snapshot_is_dropped()
    {
        // Replace-from-snapshot contract: the section mirrors the latest snapshot,
        // so a material not present in a later snapshot disappears. (In production
        // this cannot happen mid-session because snapshots are cumulative; this
        // locks the contract that makes the new-session reset work.)
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000), Mat(35, "Pyerite", 500));
        w.Merge(Mat(34, "Tritanium", 1000));

        w.RefinedOutput.Select(r => r.Name).Should().Equal("Tritanium");
    }

    [Fact]
    public void Pilot_scope_hides_section_when_toggle_off()
    {
        // Plan 49 (issue #1): a per-pilot widget with the master toggle OFF never
        // shows the refined-output section, regardless of auto-show / overrides.
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true, ShowPilotRefinedOutput = false };
        using var w = new TestWidget(settings, scopeKey: "123");

        w.Merge(Mat(34, "Tritanium", 1000));

        w.RefinedOutput.Should().BeEmpty("the per-pilot refined-output toggle is off");
    }

    [Fact]
    public void Session_scope_unaffected_by_pilot_toggle()
    {
        // Plan 49: the Session widget (scope "") is NEVER gated by the per-pilot toggle.
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true, ShowPilotRefinedOutput = false };
        using var w = new TestWidget(settings, scopeKey: string.Empty);

        w.Merge(Mat(34, "Tritanium", 1000));

        w.RefinedOutput.Should().ContainSingle().Which.Name.Should().Be("Tritanium");
    }

    [Fact]
    public async Task Pilot_scope_reshows_section_when_toggle_flipped_on()
    {
        // Plan 49: flipping the per-pilot toggle ON live-updates the section via the
        // SettingChanged -> HydrateRefinedAsync path (no restart). Mirrors the
        // SettingChanged async pattern in PerPilotWidgetViewModelStatsTests.
        var settings = new FakeSettingsRepository { AutoShowRefinedMaterials = true, ShowPilotRefinedOutput = false };
        using var w = new TestWidget(settings, scopeKey: "123");
        w.Merge(Mat(34, "Tritanium", 1000));
        w.RefinedOutput.Should().BeEmpty();

        settings.ShowPilotRefinedOutput = true;
        settings.RaiseSettingChangedForTests("show_pilot_refined_output");
        await Task.Yield();
        await Task.Yield();
        w.Merge(Mat(34, "Tritanium", 1000));

        w.RefinedOutput.Should().ContainSingle().Which.Name.Should().Be("Tritanium");
    }
}
