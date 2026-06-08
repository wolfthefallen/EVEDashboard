namespace EveDashboard.Core.Tests.Settings.WidgetStats;

using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Xunit;

public class StatRowAttributeTests
{
    private sealed class Sample
    {
        [StatRow("Plain")]
        public string PlainProp { get; } = string.Empty;

        [StatRow("Custom", Format = "N2", Reorderable = false, DefaultOrder = 42, DefaultEmphasis = StatEmphasis.Hero)]
        public string CustomProp { get; } = string.Empty;
    }

    [Fact]
    public void Defaults_match_documented_values()
    {
        var attr = typeof(Sample).GetProperty(nameof(Sample.PlainProp))!.GetCustomAttributes(typeof(StatRowAttribute), inherit: true)
            .Cast<StatRowAttribute>().Single();

        attr.Label.Should().Be("Plain");
        attr.Format.Should().BeNull();
        attr.Reorderable.Should().BeTrue();
        attr.DefaultOrder.Should().Be(0);
        attr.DefaultEmphasis.Should().Be(StatEmphasis.None);
    }

    [Fact]
    public void Custom_values_are_set_via_init()
    {
        var attr = typeof(Sample).GetProperty(nameof(Sample.CustomProp))!.GetCustomAttributes(typeof(StatRowAttribute), inherit: true)
            .Cast<StatRowAttribute>().Single();

        attr.Label.Should().Be("Custom");
        attr.Format.Should().Be("N2");
        attr.Reorderable.Should().BeFalse();
        attr.DefaultOrder.Should().Be(42);
        attr.DefaultEmphasis.Should().Be(StatEmphasis.Hero);
    }

    [Fact]
    public void Constructing_with_null_label_throws()
    {
        Action act = () => _ = new StatRowAttribute(label: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
