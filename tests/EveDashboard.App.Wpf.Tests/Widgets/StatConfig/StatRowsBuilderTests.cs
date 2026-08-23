namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

public class StatRowsBuilderTests
{
    private sealed class SampleVm
    {
        [StatRow("Alpha", DefaultOrder = 1)] public int Alpha { get; set; }
        [StatRow("Bravo", DefaultOrder = 2, DefaultEmphasis = StatEmphasis.Header0)] public int Bravo { get; set; }
        [StatRow("Charlie", DefaultOrder = 3)] public int Charlie { get; set; }

        [StatRow("Footer", Reorderable = false, DefaultOrder = 9999)]
        public int Footer { get; set; }

        // No StatRow attribute — ignored entirely.
        public int Plain { get; set; }
    }

    [Fact]
    public void Saved_null_returns_reorderable_in_DefaultOrder_with_default_emphasis()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved: null);

        resolution.Reorderable.Select(r => r.Key).Should().Equal("Alpha", "Bravo", "Charlie");
        resolution.Reorderable.Should().AllSatisfy(r => r.Hidden.Should().BeFalse());
        resolution.Reorderable[0].Emphasis.Should().Be(StatEmphasis.None);   // Alpha default
        resolution.Reorderable[1].Emphasis.Should().Be(StatEmphasis.Header0); // Bravo default
        resolution.Reorderable[2].Emphasis.Should().Be(StatEmphasis.None);   // Charlie default
    }

    [Fact]
    public void Saved_null_returns_HideOnly_in_DefaultOrder_visible()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved: null);

        resolution.HideOnly.Should().ContainSingle().Which.Key.Should().Be("Footer");
        resolution.HideOnly[0].Hidden.Should().BeFalse();
    }

    [Fact]
    public void Saved_with_known_keys_preserves_saved_order()
    {
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Charlie", false, StatEmphasis.None),
            new StatEntry("Alpha",   false, StatEmphasis.None),
            new StatEntry("Bravo",   false, StatEmphasis.None),
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.Select(r => r.Key).Should().Equal("Charlie", "Alpha", "Bravo");
    }

    [Fact]
    public void Saved_with_unknown_key_skips_the_unknown_entry()
    {
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Alpha",  false, StatEmphasis.None),
            new StatEntry("Ghost",  false, StatEmphasis.None),
            new StatEntry("Bravo",  false, StatEmphasis.None),
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.Select(r => r.Key).Should().NotContain("Ghost");
        // Charlie is missing from the saved list -> inserted at its DefaultOrder = 3 slot
        // (after Alpha=1 and Bravo=2 which were saved in order).
        resolution.Reorderable.Select(r => r.Key).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void Saved_missing_known_key_appends_after_saved_prefix_in_DefaultOrder()
    {
        // Saved has Alpha + Charlie. Bravo (missing) is appended AFTER the saved
        // prefix (saved order is preserved exactly; future-
        // added stats slot in below the user's customised list, sorted by
        // DefaultOrder among themselves).
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Alpha",   false, StatEmphasis.None),
            new StatEntry("Charlie", false, StatEmphasis.None),
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.Select(r => r.Key).Should().Equal("Alpha", "Charlie", "Bravo");
    }

    [Fact]
    public void Multiple_missing_keys_appended_in_DefaultOrder_among_themselves()
    {
        // Saved = [Charlie]; missing = [Alpha (DefaultOrder=1), Bravo (DefaultOrder=2)].
        // Expected: [Charlie] + [Alpha, Bravo] (sorted among the missing set).
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Charlie", false, StatEmphasis.None),
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.Select(r => r.Key).Should().Equal("Charlie", "Alpha", "Bravo");
    }

    [Fact]
    public void Saved_with_hidden_flag_honours_it_for_reorderable_AND_hideonly()
    {
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Alpha",   false, StatEmphasis.None),
            new StatEntry("Bravo",   true,  StatEmphasis.None),
            new StatEntry("Charlie", false, StatEmphasis.None),
            new StatEntry("Footer",  true,  StatEmphasis.None),
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.First(r => r.Key == "Bravo").Hidden.Should().BeTrue();
        resolution.HideOnly.First(r => r.Key == "Footer").Hidden.Should().BeTrue();
    }

    [Fact]
    public void Saved_emphasis_overrides_DefaultEmphasis()
    {
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Alpha", false, StatEmphasis.Hero),     // Default None -> overridden to Hero
            new StatEntry("Bravo", false, StatEmphasis.None),      // Default Header0 -> overridden to None
        });

        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        resolution.Reorderable.First(r => r.Key == "Alpha").Emphasis.Should().Be(StatEmphasis.Hero);
        resolution.Reorderable.First(r => r.Key == "Bravo").Emphasis.Should().Be(StatEmphasis.None);
    }

    [Fact]
    public void Reorderable_descriptors_carry_label_format_propinfo()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved: null);
        var alpha = resolution.Reorderable.First(r => r.Key == "Alpha");

        alpha.Label.Should().Be("Alpha");
        alpha.Reorderable.Should().BeTrue();
        alpha.PropInfo.DeclaringType.Should().Be(typeof(SampleVm));
    }

    [Fact]
    public void Null_vmType_throws()
    {
        Action act = () => StatRowsBuilder.Resolve(null!, saved: null);
        act.Should().Throw<ArgumentNullException>().WithParameterName("vmType");
    }

    private sealed class CollidingVm
    {
        [StatRow("Beta",  DefaultOrder = 0)] public int Beta { get; set; }
        [StatRow("Alpha", DefaultOrder = 0)] public int Alpha { get; set; }
    }

    [Fact]
    public void DefaultOrder_collisions_break_ties_alphabetically()
    {
        var resolution = StatRowsBuilder.Resolve(typeof(CollidingVm), saved: null);
        resolution.Reorderable.Select(r => r.Key).Should().Equal("Alpha", "Beta");
    }
}
