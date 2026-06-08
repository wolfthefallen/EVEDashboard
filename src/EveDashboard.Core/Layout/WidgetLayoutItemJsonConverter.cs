namespace EveDashboard.Core.Layout;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="System.Text.Json"/> converter for <see cref="WidgetLayoutItem"/>
/// (plan 19; plan 24 expanded the descriptor union; the post-plan-25 follow-
/// up moved the SessionConfig widget to a modal dialog so the union is back
/// to two kinds). Flattens <see cref="WidgetLayoutItem.Descriptor"/> and
/// <see cref="WidgetLayoutItem.Hidden"/> into a single JSON object so the
/// persisted shape matches the documented wire format:
/// <code>
/// { "kind": "session_stats", "hidden": false }
/// { "kind": "pilot", "pilot_id": 90863271, "hidden": false }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A pure POCO round-trip would emit
/// <c>{ "Descriptor": { "kind": ... }, "Hidden": false }</c>, which is more
/// verbose and bakes a nested layer into every persisted item. This converter
/// reads/writes the flat shape directly; <see cref="WidgetLayout"/> serializes
/// as <c>{ "items": [ ... ] }</c> via the default record serializer, picking
/// up this converter through <see cref="JsonSerializerOptions.Converters"/>.
/// </para>
/// <para>
/// Unknown <c>kind</c> or missing required fields throw
/// <see cref="JsonException"/>; the repository layer catches at the top of
/// <c>GetWidgetLayoutAsync</c>, logs a Warning, and returns null so the app
/// falls back to the default layout. Per
/// <c>MEMORY.md -&gt; feedback_no_migrations_until_release</c>, stale JSON
/// with the now-removed <c>"session_config"</c> kind falls through and the
/// user lands on the default layout.
/// </para>
/// </remarks>
public sealed class WidgetLayoutItemJsonConverter : JsonConverter<WidgetLayoutItem>
{
    private const string KindProp = "kind";
    private const string PilotIdProp = "pilot_id";
    private const string HiddenProp = "hidden";
    private const string KindSessionStats = "session_stats";
    private const string KindPilot = "pilot";

    /// <inheritdoc />
    public override WidgetLayoutItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for WidgetLayoutItem, got {reader.TokenType}.");
        }

        string? kind = null;
        long? pilotId = null;
        bool hidden = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}.");
            }
            var propName = reader.GetString();
            reader.Read();
            switch (propName)
            {
                case KindProp:
                    kind = reader.GetString();
                    break;
                case PilotIdProp:
                    pilotId = reader.GetInt64();
                    break;
                case HiddenProp:
                    hidden = reader.GetBoolean();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        WidgetDescriptor descriptor = kind switch
        {
            KindSessionStats => new WidgetDescriptor.SessionStats(),
            KindPilot => pilotId is { } id
                ? new WidgetDescriptor.Pilot(id)
                : throw new JsonException("WidgetLayoutItem 'pilot' missing required 'pilot_id'."),
            null => throw new JsonException("WidgetLayoutItem missing required 'kind' discriminator."),
            _ => throw new JsonException($"WidgetLayoutItem has unknown 'kind' value '{kind}'."),
        };

        return new WidgetLayoutItem(descriptor, hidden);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WidgetLayoutItem value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        switch (value.Descriptor)
        {
            case WidgetDescriptor.SessionStats:
                writer.WriteString(KindProp, KindSessionStats);
                break;
            case WidgetDescriptor.Pilot p:
                writer.WriteString(KindProp, KindPilot);
                writer.WriteNumber(PilotIdProp, p.PilotId);
                break;
            default:
                throw new JsonException($"Unknown WidgetDescriptor subtype {value.Descriptor.GetType().Name}.");
        }
        writer.WriteBoolean(HiddenProp, value.Hidden);
        writer.WriteEndObject();
    }
}
