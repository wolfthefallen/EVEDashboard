namespace EveDashboard.Core.Settings.WidgetStats;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="System.Text.Json"/> converter for <see cref="WidgetStatsConfig"/>
/// (plan 20). Reads / writes a flat JSON array of entries:
/// <code>
/// [
///   { "stat": "M3PerSecond", "hidden": false, "emphasis": "None" },
///   { "stat": "PilotName",   "hidden": true,  "emphasis": "Header0" }
/// ]
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Per <c>MEMORY.md -&gt; feedback_no_migrations_until_release</c>, this converter
/// is forgiving of missing fields (writes the default), but strict on shape:
/// missing <c>"stat"</c> throws <see cref="JsonException"/> (and the repo
/// layer catches and falls back to <c>null</c> = use default order). Missing
/// <c>"emphasis"</c> defaults to <see cref="StatEmphasis.None"/> — the
/// renderer then falls back to the property's
/// <see cref="StatRowAttribute.DefaultEmphasis"/>. Missing <c>"hidden"</c>
/// defaults to <c>false</c> (visible).
/// </para>
/// <para>
/// Mirrors the structural pattern of <c>WidgetLayoutItemJsonConverter</c>
/// (plan 19) so a reader familiar with one can read the other.
/// </para>
/// </remarks>
public sealed class WidgetStatsConfigJsonConverter : JsonConverter<WidgetStatsConfig>
{
    private const string StatProp = "stat";
    private const string HiddenProp = "hidden";
    private const string EmphasisProp = "emphasis";

    /// <inheritdoc />
    public override WidgetStatsConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"Expected StartArray for WidgetStatsConfig, got {reader.TokenType}.");
        }

        var entries = new List<StatEntry>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException(
                    $"Expected StartObject for StatEntry, got {reader.TokenType}.");
            }

            string? stat = null;
            bool hidden = false;
            StatEmphasis emphasis = StatEmphasis.None;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException(
                        $"Expected PropertyName inside StatEntry, got {reader.TokenType}.");
                }
                var propName = reader.GetString();
                reader.Read();
                switch (propName)
                {
                    case StatProp:
                        stat = reader.GetString();
                        break;
                    case HiddenProp:
                        hidden = reader.GetBoolean();
                        break;
                    case EmphasisProp:
                        var emphasisStr = reader.GetString();
                        emphasis = ParseEmphasis(emphasisStr);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (stat is null)
            {
                throw new JsonException("StatEntry missing required 'stat' field.");
            }
            entries.Add(new StatEntry(stat, hidden, emphasis));
        }

        return new WidgetStatsConfig(entries);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WidgetStatsConfig value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartArray();
        foreach (var entry in value.Entries)
        {
            writer.WriteStartObject();
            writer.WriteString(StatProp, entry.StatKey);
            writer.WriteBoolean(HiddenProp, entry.Hidden);
            writer.WriteString(EmphasisProp, entry.Emphasis.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Parse an emphasis string (case-insensitive). Unknown / null / empty
    /// strings fall back to <see cref="StatEmphasis.None"/> — the renderer
    /// then defers to the property's <see cref="StatRowAttribute.DefaultEmphasis"/>.
    /// </summary>
    private static StatEmphasis ParseEmphasis(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StatEmphasis.None;
        return Enum.TryParse<StatEmphasis>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : StatEmphasis.None;
    }
}
