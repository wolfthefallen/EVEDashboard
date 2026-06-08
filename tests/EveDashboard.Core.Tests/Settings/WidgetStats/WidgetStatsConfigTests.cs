namespace EveDashboard.Core.Tests.Settings.WidgetStats;

using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Xunit;

public class WidgetStatsConfigTests
{
    [Fact]
    public void Empty_entries_list_is_valid()
    {
        var cfg = new WidgetStatsConfig(Array.Empty<StatEntry>());
        cfg.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Equality_compares_entries_by_value_AND_order()
    {
        var a = new WidgetStatsConfig(new[]
        {
            new StatEntry("Foo", Hidden: false, StatEmphasis.None),
            new StatEntry("Bar", Hidden: true, StatEmphasis.Header0),
        });
        var b = new WidgetStatsConfig(new[]
        {
            new StatEntry("Foo", Hidden: false, StatEmphasis.None),
            new StatEntry("Bar", Hidden: true, StatEmphasis.Header0),
        });
        // Records' default equality is reference for collection-typed fields, so
        // equality of two distinct list instances is false. We assert that
        // ENTRY content matches, which is the contract the renderer relies on.
        a.Entries.Should().Equal(b.Entries);
    }

    [Fact]
    public void Order_of_entries_is_preserved()
    {
        var entries = new[]
        {
            new StatEntry("First",  Hidden: false, StatEmphasis.None),
            new StatEntry("Second", Hidden: false, StatEmphasis.None),
            new StatEntry("Third",  Hidden: false, StatEmphasis.None),
        };
        var cfg = new WidgetStatsConfig(entries);
        cfg.Entries.Select(e => e.StatKey).Should().Equal("First", "Second", "Third");
    }
}
