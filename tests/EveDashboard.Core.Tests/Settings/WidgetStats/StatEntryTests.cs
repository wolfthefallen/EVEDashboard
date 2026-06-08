namespace EveDashboard.Core.Tests.Settings.WidgetStats;

using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Xunit;

public class StatEntryTests
{
    [Fact]
    public void Equality_compares_all_three_fields()
    {
        var a = new StatEntry("Foo", Hidden: false, StatEmphasis.Header0);
        var b = new StatEntry("Foo", Hidden: false, StatEmphasis.Header0);
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_differentiates_on_StatKey()
    {
        var a = new StatEntry("Foo", Hidden: false, StatEmphasis.None);
        var b = new StatEntry("Bar", Hidden: false, StatEmphasis.None);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_differentiates_on_Hidden()
    {
        var a = new StatEntry("Foo", Hidden: false, StatEmphasis.None);
        var b = new StatEntry("Foo", Hidden: true, StatEmphasis.None);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_differentiates_on_Emphasis()
    {
        var a = new StatEntry("Foo", Hidden: false, StatEmphasis.None);
        var b = new StatEntry("Foo", Hidden: false, StatEmphasis.Header1);
        a.Should().NotBe(b);
    }
}
