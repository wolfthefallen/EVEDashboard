namespace EveDashboard.SessionIO.Json;

using System.Text.Json.Serialization;
using EveDashboard.SessionIO.Tables;

/// <summary>
/// On-disk JSON envelope for a single-session export. Top-level shape:
/// a <c>_manifest</c> block plus one array per database table mirroring
/// <see cref="SessionExportData"/>.
/// </summary>
/// <remarks>
/// JSON property names are lowercased / underscored to match the SQLite column
/// and table names — opening the file in a text editor and opening the DB in
/// DB Browser for SQLite show the same identifiers.
/// </remarks>
public sealed record SessionExportEnvelope(
    [property: JsonPropertyName("_manifest")]       SessionExportManifest    Manifest,
    [property: JsonPropertyName("session")]         IReadOnlyList<SessionRow>        Session,
    [property: JsonPropertyName("session_time")]    IReadOnlyList<SessionTimeRow>    SessionTime,
    [property: JsonPropertyName("pilot")]           IReadOnlyList<PilotRow>          Pilot,
    [property: JsonPropertyName("session_pilot")]   IReadOnlyList<SessionPilotRow>   SessionPilot,
    [property: JsonPropertyName("log")]             IReadOnlyList<LogRow>            Log,
    [property: JsonPropertyName("market_stations")] IReadOnlyList<MarketStationRow>  MarketStations,
    [property: JsonPropertyName("market_data")]     IReadOnlyList<MarketDataRow>     MarketData);
