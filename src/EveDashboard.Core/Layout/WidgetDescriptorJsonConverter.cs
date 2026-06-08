namespace EveDashboard.Core.Layout;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="System.Text.Json"/> polymorphic converter for
/// <see cref="WidgetDescriptor"/> (plan 19; plan 24 expanded the union; the
/// post-plan-25 follow-up moved the SessionConfig widget to a modal dialog,
/// so the union is back to two kinds). Reads the <c>kind</c> string
/// discriminator and constructs the matching nested record
/// (<see cref="WidgetDescriptor.SessionStats"/> or
/// <see cref="WidgetDescriptor.Pilot"/>).
/// </summary>
/// <remarks>
/// <para>
/// Wire format:
/// <code>
/// { "kind": "session_stats" }
/// { "kind": "pilot", "pilot_id": 90863271 }
/// </code>
/// </para>
/// <para>
/// Unknown <c>kind</c> values throw <see cref="JsonException"/>; the
/// repository layer catches at the top of <c>GetWidgetLayoutAsync</c>, logs
/// a Warning, and returns null so the app falls back to the default layout.
/// Per <c>MEMORY.md -&gt; feedback_no_migrations_until_release</c>, stale
/// JSON with the now-removed <c>"session_config"</c> kind falls through to
/// the unknown-kind branch and the user lands on the default layout.
/// </para>
/// </remarks>
public sealed class WidgetDescriptorJsonConverter : JsonConverter<WidgetDescriptor>
{
    private const string KindProp = "kind";
    private const string PilotIdProp = "pilot_id";
    private const string KindSessionStats = "session_stats";
    private const string KindPilot = "pilot";

    /// <inheritdoc />
    public override WidgetDescriptor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for WidgetDescriptor, got {reader.TokenType}.");
        }

        string? kind = null;
        long? pilotId = null;

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
                default:
                    reader.Skip();
                    break;
            }
        }

        return kind switch
        {
            KindSessionStats => new WidgetDescriptor.SessionStats(),
            KindPilot => pilotId is { } id
                ? new WidgetDescriptor.Pilot(id)
                : throw new JsonException("WidgetDescriptor 'pilot' missing required 'pilot_id'."),
            null => throw new JsonException("WidgetDescriptor missing required 'kind' discriminator."),
            _ => throw new JsonException($"WidgetDescriptor has unknown 'kind' value '{kind}'."),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WidgetDescriptor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        switch (value)
        {
            case WidgetDescriptor.SessionStats:
                writer.WriteString(KindProp, KindSessionStats);
                break;
            case WidgetDescriptor.Pilot p:
                writer.WriteString(KindProp, KindPilot);
                writer.WriteNumber(PilotIdProp, p.PilotId);
                break;
            default:
                throw new JsonException($"Unknown WidgetDescriptor subtype {value.GetType().Name}.");
        }
        writer.WriteEndObject();
    }
}
