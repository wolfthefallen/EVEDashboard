namespace EveDashboard.App.Wpf.Tests.Converters;

using System.Globalization;
using System.Windows;
using EveDashboard.App.Wpf.Converters;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

/// <summary>
/// Unit tests for <see cref="StatEmphasisToFontWeightConverter"/>. Header0/1/2
/// are Bold; None and Hero are Normal (matching the active-session emphasis rows).
/// </summary>
public class StatEmphasisToFontWeightConverterTests
{
    private static readonly StatEmphasisToFontWeightConverter Converter = new();

    private static object Convert(object value) =>
        Converter.Convert(value, typeof(FontWeight), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(StatEmphasis.Header0)]
    [InlineData(StatEmphasis.Header1)]
    [InlineData(StatEmphasis.Header2)]
    public void Convert_headers_are_bold(StatEmphasis emphasis)
    {
        Convert(emphasis).Should().Be(FontWeights.Bold);
    }

    [Theory]
    [InlineData(StatEmphasis.None)]
    [InlineData(StatEmphasis.Hero)]
    public void Convert_none_and_hero_are_normal(StatEmphasis emphasis)
    {
        Convert(emphasis).Should().Be(FontWeights.Normal);
    }

    [Fact]
    public void Convert_non_emphasis_value_is_normal()
    {
        Convert("nope").Should().Be(FontWeights.Normal);
    }

    [Fact]
    public void ConvertBack_throws()
    {
        Action act = () => Converter.ConvertBack(FontWeights.Bold, typeof(StatEmphasis), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}

/// <summary>
/// Unit tests for <see cref="StatEmphasisToFontSizeConverter"/>. Verifies
/// the emphasis -&gt; size mapping and that None / non-emphasis values return
/// <see cref="DependencyProperty.UnsetValue"/> so the bound element keeps its
/// inherited (today's) size.
/// </summary>
public class StatEmphasisToFontSizeConverterTests
{
    private static readonly StatEmphasisToFontSizeConverter Converter = new();

    private static object Convert(object value) =>
        Converter.Convert(value, typeof(double), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(StatEmphasis.Header0, 14.0)]
    [InlineData(StatEmphasis.Header1, 16.0)]
    [InlineData(StatEmphasis.Header2, 20.0)]
    [InlineData(StatEmphasis.Hero, 22.0)]
    public void Convert_maps_each_emphasis_to_its_font_size(StatEmphasis emphasis, double expected)
    {
        Convert(emphasis).Should().Be(expected);
    }

    [Fact]
    public void Convert_None_returns_UnsetValue_so_target_inherits()
    {
        Convert(StatEmphasis.None).Should().Be(DependencyProperty.UnsetValue);
    }

    [Fact]
    public void Convert_non_emphasis_value_returns_UnsetValue()
    {
        Convert("not-an-emphasis").Should().Be(DependencyProperty.UnsetValue);
    }

    [Fact]
    public void ConvertBack_throws()
    {
        Action act = () => Converter.ConvertBack(14.0, typeof(StatEmphasis), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}
