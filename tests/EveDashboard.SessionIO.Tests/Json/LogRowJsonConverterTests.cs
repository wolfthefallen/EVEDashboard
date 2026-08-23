namespace EveDashboard.SessionIO.Tests.Json;

using System.Text.Json;
using EveDashboard.SessionIO.Tables;
using FluentAssertions;

/// <summary>
/// <see cref="EveDashboard.SessionIO.Json.LogRowJsonConverter"/> defaults
/// missing <c>role</c> to <c>"Miner"</c> so JSON files exported before pilot
/// roles existed reimport cleanly. Role-carrying files round-trip lossless.
/// </summary>
public class LogRowJsonConverterTests
{
    // The factory's static Default is internal; we mirror its shape here for direct
    // converter testing. Production round-trip uses the factory's shared options.
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented        = false,
        };
        options.Converters.Add(new EveDashboard.SessionIO.Json.LogRowJsonConverter());
        return options;
    }

    [Fact]
    public void Read_pre_plan_35_payload_without_role_defaults_to_Miner()
    {
        const string json = """
            {
                "log_id": 555,
                "session_id": 42,
                "pilot_id": 90000001,
                "eve_timestamp": "2026-05-07T10:01:00Z",
                "event_kind": "Tick",
                "type_id": 1230,
                "units": 100,
                "volume_m3": 10.0
            }
            """;

        var row = JsonSerializer.Deserialize<LogRow>(json, Options());

        row.Should().NotBeNull();
        row!.Role.Should().Be("Miner", "missing role field defaults to Miner");
        row.LogId.Should().Be(555);
        row.SessionId.Should().Be("42");
        row.PilotId.Should().Be(90000001);
        row.EveTimestamp.Should().Be("2026-05-07T10:01:00Z");
        row.EventKind.Should().Be("Tick");
        row.TypeId.Should().Be(1230);
        row.Units.Should().Be(100);
        row.VolumeM3.Should().Be(10.0);
    }

    [Theory]
    [InlineData("Miner")]
    [InlineData("Booster")]
    [InlineData("Cyno")]
    [InlineData("Combat")]
    public void Read_post_plan_35_payload_preserves_role(string explicitRole)
    {
        var json = $$"""
            {
                "log_id": 1,
                "session_id": 1,
                "pilot_id": 1,
                "eve_timestamp": "2026-05-07T10:01:00Z",
                "event_kind": "Tick",
                "type_id": 1230,
                "units": 100,
                "volume_m3": 10.0,
                "role": "{{explicitRole}}"
            }
            """;

        var row = JsonSerializer.Deserialize<LogRow>(json, Options());

        row.Should().NotBeNull();
        row!.Role.Should().Be(explicitRole);
    }

    [Fact]
    public void Write_emits_role_field()
    {
        var row = new LogRow(1, "1", 1, "2026-05-07T10:01:00Z", "Tick", 1230, 100, 10.0, "Cyno");

        var json = JsonSerializer.Serialize(row, Options());

        json.Should().Contain("\"role\":\"Cyno\"");
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var original = new LogRow(
            LogId:        12345,
            SessionId:    "99",
            PilotId:      90_000_001L,
            EveTimestamp: "2026-05-07T10:01:23.456Z",
            EventKind:    "Crit",
            TypeId:       1230,
            Units:        250,
            VolumeM3:     25.0,
            Role:         "Booster");
        var options = Options();

        var json = JsonSerializer.Serialize(original, options);
        var roundTripped = JsonSerializer.Deserialize<LogRow>(json, options);

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void RoundTrip_preserves_null_optional_fields()
    {
        // Residue rows: TypeId = null, Units = null, VolumeM3 = null.
        var original = new LogRow(
            LogId:        1,
            SessionId:    "1",
            PilotId:      1,
            EveTimestamp: "2026-05-07T10:01:00Z",
            EventKind:    "Residue",
            TypeId:       null,
            Units:        null,
            VolumeM3:     null,
            Role:         "Miner");
        var options = Options();

        var json = JsonSerializer.Serialize(original, options);
        var roundTripped = JsonSerializer.Deserialize<LogRow>(json, options);

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Read_unknown_property_is_skipped()
    {
        // Forward compat — a future field that lands in a JSON file should not
        // cause a JsonException; the converter just skips unknown properties.
        const string json = """
            {
                "log_id": 1,
                "session_id": 1,
                "pilot_id": 1,
                "eve_timestamp": "2026-05-07T10:01:00Z",
                "event_kind": "Tick",
                "type_id": 1230,
                "units": 100,
                "volume_m3": 10.0,
                "role": "Miner",
                "future_field": "ignored"
            }
            """;

        var act = () => JsonSerializer.Deserialize<LogRow>(json, Options());
        act.Should().NotThrow();
    }
}
