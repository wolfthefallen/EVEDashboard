namespace EveDashboard.SessionIO.Json;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads a <c>session_id</c> JSON value into a string, tolerating BOTH a JSON
/// string (plan-38 sha256 ids) and a JSON number (legacy integer ids from
/// pre-plan-38 export files). Always writes a JSON string.
/// </summary>
/// <remarks>
/// Plan 38: export files carry the sha256 id as a string. Legacy files carry an
/// integer. The importer recomputes the sha256 when the value is not already a
/// 64-char hex hash (see <c>EveDashboard.Core.Sessions.SessionId.LooksLikeSha256</c>),
/// so a legacy integer simply surfaces as its decimal string here and is then
/// recomputed downstream.
/// </remarks>
public sealed class FlexibleSessionIdJsonConverter : JsonConverter<string>
{
    /// <inheritdoc />
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Expected string or number for session_id, got {reader.TokenType}."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
