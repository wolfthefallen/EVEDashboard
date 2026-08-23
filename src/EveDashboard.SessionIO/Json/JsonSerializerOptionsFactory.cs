namespace EveDashboard.SessionIO.Json;

using System.Reflection;
using System.Text.Json;

/// <summary>
/// Single source of truth for the <see cref="JsonSerializerOptions"/> shared by
/// the exporter and the importer. Configures the snake_case naming policy so
/// row-record properties (<c>SessionId</c>, <c>StartTime</c>, etc.) round-trip
/// against the column-named JSON shape (<c>session_id</c>, <c>start_time</c>).
/// </summary>
internal static class JsonSerializerOptionsFactory
{
    /// <summary>The shared options instance.</summary>
    /// <remarks>
    /// Registers <see cref="LogRowJsonConverter"/> and
    /// <see cref="SessionPilotRowJsonConverter"/> so older JSON files
    /// (which lack the <c>role</c> field on log + session_pilot rows) import
    /// cleanly with missing values defaulted to <c>"Miner"</c>. Files that
    /// carry the field round-trip lossless.
    /// </remarks>
    public static readonly JsonSerializerOptions Default = CreateDefault();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented        = true,
        };
        options.Converters.Add(new LogRowJsonConverter());
        options.Converters.Add(new SessionPilotRowJsonConverter());
        return options;
    }

    /// <summary>
    /// Resolve the running app's build version, e.g. <c>"0.29.0.0"</c>. Falls
    /// back to <c>"unknown"</c> when no entry assembly is available (test host
    /// scenarios) or no version attribute is present.
    /// </summary>
    public static string ResolveAppVersion()
    {
        var asm     = Assembly.GetEntryAssembly() ?? typeof(JsonSerializerOptionsFactory).Assembly;
        var version = asm.GetName().Version?.ToString();
        return version ?? "unknown";
    }
}
