namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

public partial class StatRowsRendererTests
{
    public sealed partial class SampleVm : ObservableObject
    {
        [ObservableProperty]
        [property: StatRow("Speed", Format = "N2", DefaultOrder = 1)]
        private double _speed;

        [ObservableProperty]
        [property: StatRow("Count", DefaultOrder = 2)]
        private long _count;
    }

    [Fact]
    public void ApplyDescriptors_renders_initial_formatted_values()
    {
        var src = new SampleVm { Speed = 3.14159, Count = 42 };
        using var renderer = new StatRowsRenderer(src);
        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved: null);

        renderer.ApplyDescriptors(resolution.Reorderable);

        renderer.Stats.Should().HaveCount(2);
        renderer.Stats[0].Key.Should().Be("Speed");
        renderer.Stats[0].FormattedValue.Should().Be(3.14159.ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        renderer.Stats[1].Key.Should().Be("Count");
        renderer.Stats[1].FormattedValue.Should().Be("42");
    }

    [Fact]
    public void Source_property_change_updates_matching_StatRowViewModel_FormattedValue()
    {
        var src = new SampleVm { Speed = 1.0, Count = 0 };
        using var renderer = new StatRowsRenderer(src);
        renderer.ApplyDescriptors(StatRowsBuilder.Resolve(typeof(SampleVm), null).Reorderable);
        var speedRow = renderer.Stats.First(r => r.Key == "Speed");
        var countRow = renderer.Stats.First(r => r.Key == "Count");

        src.Speed = 2.5;
        src.Count = 99;

        speedRow.FormattedValue.Should().Be(2.5.ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        countRow.FormattedValue.Should().Be("99");
    }

    [Fact]
    public void FormattedValue_uses_invariant_culture_regardless_of_thread_culture()
    {
        // Regression guard: the live widgets must format ISK/m3 identically to the
        // Reports panes (which hardcode InvariantCulture). Under a comma-decimal
        // locale, a grouped value must still render invariant ("1,234.50"), NOT the
        // locale form ("1.234,50"). All rendering here is synchronous, so setting
        // the thread culture is safe under parallel test execution (no thread hop).
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var src = new SampleVm { Speed = 1234.5, Count = 42 };
            using var renderer = new StatRowsRenderer(src);
            renderer.ApplyDescriptors(StatRowsBuilder.Resolve(typeof(SampleVm), null).Reorderable);

            renderer.Stats.First(r => r.Key == "Speed").FormattedValue.Should().Be("1,234.50");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Hidden_descriptors_are_filtered_out_of_Stats()
    {
        var src = new SampleVm();
        using var renderer = new StatRowsRenderer(src);
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Speed", true,  StatEmphasis.None),
            new StatEntry("Count", false, StatEmphasis.None),
        });
        var resolution = StatRowsBuilder.Resolve(typeof(SampleVm), saved);

        renderer.ApplyDescriptors(resolution.Reorderable);

        renderer.Stats.Select(r => r.Key).Should().Equal("Count");
    }

    [Fact]
    public void Null_source_throws()
    {
        Action act = () => _ = new StatRowsRenderer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void HideOnly_descriptor_passed_to_ApplyDescriptors_throws()
    {
        var src = new SampleVm();
        using var renderer = new StatRowsRenderer(src);
        var hideOnly = new StatRowDescriptor(
            Key: "Footer", Label: "Footer", Format: null, Reorderable: false,
            Hidden: false, Emphasis: StatEmphasis.None,
            PropInfo: typeof(SampleVm).GetProperty("Speed")!);

        Action act = () => renderer.ApplyDescriptors(new[] { hideOnly });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Dispose_unsubscribes_so_post_dispose_changes_dont_update_rows()
    {
        var src = new SampleVm { Speed = 1.0 };
        var renderer = new StatRowsRenderer(src);
        renderer.ApplyDescriptors(StatRowsBuilder.Resolve(typeof(SampleVm), null).Reorderable);
        var speedRow = renderer.Stats.First(r => r.Key == "Speed");

        renderer.Dispose();
        src.Speed = 99.9;

        // The row's FormattedValue should still be the pre-Dispose value.
        speedRow.FormattedValue.Should().NotContain("99");
    }
}
