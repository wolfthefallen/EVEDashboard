namespace EveDashboard.Core.Tests.Settings.WidgetStats;

using System.Text.Json;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;
using Xunit;

public class WidgetStatsConfigJsonConverterTests
{
    [Fact]
    public void Roundtrip_preserves_entries_in_order()
    {
        var cfg = new WidgetStatsConfig(new[]
        {
            new StatEntry("M3PerSecond", Hidden: false, StatEmphasis.None),
            new StatEntry("PilotName", Hidden: true, StatEmphasis.Header0),
            new StatEntry("TotalTimeMined", Hidden: false, StatEmphasis.Hero),
        });

        var json = JsonSerializer.Serialize(cfg);
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);

        decoded.Should().NotBeNull();
        decoded!.Entries.Should().HaveCount(3);
        decoded.Entries[0].Should().Be(new StatEntry("M3PerSecond", false, StatEmphasis.None));
        decoded.Entries[1].Should().Be(new StatEntry("PilotName", true, StatEmphasis.Header0));
        decoded.Entries[2].Should().Be(new StatEntry("TotalTimeMined", false, StatEmphasis.Hero));
    }

    [Fact]
    public void Empty_array_roundtrips_to_empty_entries()
    {
        var cfg = new WidgetStatsConfig(Array.Empty<StatEntry>());
        var json = JsonSerializer.Serialize(cfg);
        json.Should().Be("[]");

        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded.Should().NotBeNull();
        decoded!.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Wire_format_is_flat_array_of_objects()
    {
        var cfg = new WidgetStatsConfig(new[]
        {
            new StatEntry("Foo", Hidden: true, StatEmphasis.Header1),
        });
        var json = JsonSerializer.Serialize(cfg);
        json.Should().Be("[{\"stat\":\"Foo\",\"hidden\":true,\"emphasis\":\"Header1\"}]");
    }

    [Fact]
    public void Missing_emphasis_field_defaults_to_None()
    {
        const string json = "[{\"stat\":\"Foo\",\"hidden\":false}]";
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded.Should().NotBeNull();
        decoded!.Entries.Should().ContainSingle();
        decoded.Entries[0].Emphasis.Should().Be(StatEmphasis.None);
    }

    [Fact]
    public void Missing_hidden_field_defaults_to_false()
    {
        const string json = "[{\"stat\":\"Foo\",\"emphasis\":\"Header0\"}]";
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded.Should().NotBeNull();
        decoded!.Entries[0].Hidden.Should().BeFalse();
        decoded.Entries[0].Emphasis.Should().Be(StatEmphasis.Header0);
    }

    [Fact]
    public void Unknown_emphasis_string_falls_back_to_None()
    {
        const string json = "[{\"stat\":\"Foo\",\"hidden\":false,\"emphasis\":\"NotARealLevel\"}]";
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded!.Entries[0].Emphasis.Should().Be(StatEmphasis.None);
    }

    [Fact]
    public void Emphasis_parse_is_case_insensitive()
    {
        const string json = "[{\"stat\":\"Foo\",\"hidden\":false,\"emphasis\":\"hero\"}]";
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded!.Entries[0].Emphasis.Should().Be(StatEmphasis.Hero);
    }

    [Fact]
    public void Missing_stat_field_throws()
    {
        const string json = "[{\"hidden\":false,\"emphasis\":\"None\"}]";
        Action act = () => JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        act.Should().Throw<JsonException>().WithMessage("*'stat'*");
    }

    [Fact]
    public void Non_array_root_throws()
    {
        const string json = "{\"stat\":\"Foo\"}";
        Action act = () => JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Unknown_top_level_property_inside_entry_is_ignored()
    {
        const string json = "[{\"stat\":\"Foo\",\"hidden\":false,\"unexpected\":42}]";
        var decoded = JsonSerializer.Deserialize<WidgetStatsConfig>(json);
        decoded.Should().NotBeNull();
        decoded!.Entries[0].StatKey.Should().Be("Foo");
    }
}
