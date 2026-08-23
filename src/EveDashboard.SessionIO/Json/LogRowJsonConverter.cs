namespace EveDashboard.SessionIO.Json;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveDashboard.SessionIO.Tables;

/// <summary>
/// Custom JSON converter for <see cref="LogRow"/>. Reads each property by name;
/// defaults missing <c>role</c> to <c>"Miner"</c> so older JSON files (which did
/// not carry the <c>role</c> field) round-trip cleanly into a destination DB
/// whose <c>log</c> table requires it.
/// </summary>
/// <remarks>
/// Writing always emits every field including <c>role</c>; reading tolerates a
/// missing <c>role</c>. Unknown properties on the JSON object are skipped via
/// <see cref="Utf8JsonReader.Skip"/>. Per the project's
/// <c>SnakeCaseLower</c> naming policy in
/// <see cref="JsonSerializerOptionsFactory.Default"/>, the field names on the
/// wire are <c>log_id</c>, <c>session_id</c>, <c>pilot_id</c>,
/// <c>eve_timestamp</c>, <c>event_kind</c>, <c>type_id</c>, <c>units</c>,
/// <c>volume_m3</c>, <c>role</c>.
/// </remarks>
public sealed class LogRowJsonConverter : JsonConverter<LogRow>
{
    /// <inheritdoc />
    public override LogRow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for LogRow, got {reader.TokenType}.");
        }

        long    logId        = 0;
        string  sessionId    = string.Empty;   // sha256 string (rewritten on import regardless)
        long    pilotId      = 0;
        string  eveTimestamp = string.Empty;
        string  eventKind    = string.Empty;
        long?   typeId       = null;
        long?   units        = null;
        double? volumeM3     = null;
        string  role         = "Miner";   // default if absent (files written before the role field existed)

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new LogRow(logId, sessionId, pilotId, eveTimestamp, eventKind, typeId, units, volumeM3, role);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "log_id":        logId        = reader.GetInt64(); break;
                case "session_id":    sessionId    = reader.TokenType == JsonTokenType.Number
                                                         ? reader.GetInt64().ToString(CultureInfo.InvariantCulture)
                                                         : reader.GetString() ?? string.Empty; break;
                case "pilot_id":      pilotId      = reader.GetInt64(); break;
                case "eve_timestamp": eveTimestamp = reader.GetString() ?? string.Empty; break;
                case "event_kind":    eventKind    = reader.GetString() ?? string.Empty; break;
                case "type_id":       typeId       = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt64(); break;
                case "units":         units        = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt64(); break;
                case "volume_m3":     volumeM3     = reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble(); break;
                case "role":          role         = reader.GetString() ?? "Miner"; break;
                default:              reader.Skip(); break;
            }
        }

        throw new JsonException("Unexpected end of JSON while reading LogRow.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LogRow value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("log_id", value.LogId);
        writer.WriteString("session_id", value.SessionId);
        writer.WriteNumber("pilot_id", value.PilotId);
        writer.WriteString("eve_timestamp", value.EveTimestamp);
        writer.WriteString("event_kind", value.EventKind);
        if (value.TypeId   is { } tid) writer.WriteNumber("type_id", tid);    else writer.WriteNull("type_id");
        if (value.Units    is { } u)   writer.WriteNumber("units", u);        else writer.WriteNull("units");
        if (value.VolumeM3 is { } v)   writer.WriteNumber("volume_m3", v);    else writer.WriteNull("volume_m3");
        writer.WriteString("role", value.Role);
        writer.WriteEndObject();
    }
}
