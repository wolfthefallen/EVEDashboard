namespace EveDashboard.SessionIO.Json;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveDashboard.SessionIO.Tables;

/// <summary>
/// Custom JSON converter for <see cref="SessionPilotRow"/>. Reads each property
/// by name; defaults missing <c>role</c> to <c>"Miner"</c> so JSON files written
/// before the field existed round-trip cleanly.
/// </summary>
/// <remarks>
/// Same pattern as <see cref="LogRowJsonConverter"/> — writing emits every
/// field; reading tolerates a missing <c>role</c>. Field names match the
/// snake_case naming policy: <c>session_id</c>, <c>pilot_id</c>, <c>role</c>.
/// </remarks>
public sealed class SessionPilotRowJsonConverter : JsonConverter<SessionPilotRow>
{
    /// <inheritdoc />
    public override SessionPilotRow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for SessionPilotRow, got {reader.TokenType}.");
        }

        string sessionId = string.Empty;   // sha256 string (rewritten on import regardless)
        long   pilotId   = 0;
        string role      = "Miner";   // default if absent (files written before the role field existed)

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new SessionPilotRow(sessionId, pilotId, role);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "session_id": sessionId = reader.TokenType == JsonTokenType.Number
                                                   ? reader.GetInt64().ToString(CultureInfo.InvariantCulture)
                                                   : reader.GetString() ?? string.Empty; break;
                case "pilot_id":   pilotId   = reader.GetInt64(); break;
                case "role":       role      = reader.GetString() ?? "Miner"; break;
                default:           reader.Skip(); break;
            }
        }

        throw new JsonException("Unexpected end of JSON while reading SessionPilotRow.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, SessionPilotRow value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("session_id", value.SessionId);
        writer.WriteNumber("pilot_id", value.PilotId);
        writer.WriteString("role", value.Role);
        writer.WriteEndObject();
    }
}
