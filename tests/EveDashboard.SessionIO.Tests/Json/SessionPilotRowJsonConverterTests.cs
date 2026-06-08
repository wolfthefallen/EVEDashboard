namespace EveDashboard.SessionIO.Tests.Json;

using System.Text.Json;
using EveDashboard.SessionIO.Tables;
using FluentAssertions;

/// <summary>
/// Plan 35: <see cref="EveDashboard.SessionIO.Json.SessionPilotRowJsonConverter"/>
/// defaults missing <c>role</c> to <c>"Miner"</c> so pre-plan-35 JSON files
/// reimport cleanly.
/// </summary>
public class SessionPilotRowJsonConverterTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented        = false,
        };
        options.Converters.Add(new EveDashboard.SessionIO.Json.SessionPilotRowJsonConverter());
        return options;
    }

    [Fact]
    public void Read_pre_plan_35_payload_without_role_defaults_to_Miner()
    {
        const string json = """
            {
                "session_id": 42,
                "pilot_id": 90000001
            }
            """;

        var row = JsonSerializer.Deserialize<SessionPilotRow>(json, Options());

        row.Should().NotBeNull();
        row!.Role.Should().Be("Miner");
        row.SessionId.Should().Be("42");
        row.PilotId.Should().Be(90000001);
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
                "session_id": 1,
                "pilot_id": 1,
                "role": "{{explicitRole}}"
            }
            """;

        var row = JsonSerializer.Deserialize<SessionPilotRow>(json, Options());

        row.Should().NotBeNull();
        row!.Role.Should().Be(explicitRole);
    }

    [Fact]
    public void Write_emits_role_field()
    {
        var row = new SessionPilotRow("42", 90_000_001L, "Booster");

        var json = JsonSerializer.Serialize(row, Options());

        json.Should().Contain("\"role\":\"Booster\"");
    }

    [Fact]
    public void RoundTrip_preserves_all_fields()
    {
        var original = new SessionPilotRow("42", 90_000_001L, "Cyno");
        var options = Options();

        var json = JsonSerializer.Serialize(original, options);
        var roundTripped = JsonSerializer.Deserialize<SessionPilotRow>(json, options);

        roundTripped.Should().BeEquivalentTo(original);
    }
}
