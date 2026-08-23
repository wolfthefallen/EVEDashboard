namespace EveDashboard.SessionIO.Tables;

using System.Text.Json.Serialization;
using EveDashboard.SessionIO.Json;

/// <summary>
/// In-memory representation of one mining session and the rows from related
/// tables that ride along with it. Loaded by <c>SessionTableReader</c> from a
/// session SQLite database; written by <c>SessionTableWriter</c> into a
/// (possibly different) session SQLite database. Round-trippable via
/// <c>JsonSessionExporter</c> / <c>JsonSessionImporter</c>.
/// </summary>
/// <param name="Session">Single-element list — the <c>session</c> row being exported.</param>
/// <param name="SessionTimes">Every <c>session_time</c> row whose <c>session_id</c> matches the exported session.</param>
/// <param name="Pilots">Every <c>pilot</c> row referenced (via <c>session_pilot</c>) by the exported session.</param>
/// <param name="SessionPilots">Every <c>session_pilot</c> row whose <c>session_id</c> matches the exported session.</param>
/// <param name="Logs">Every <c>log</c> row whose <c>session_id</c> matches the exported session.</param>
/// <param name="MarketStations">Every <c>market_stations</c> row referenced (by <c>station_id</c>) by the exported <see cref="MarketData"/> rows.</param>
/// <param name="MarketData">The exported session's own <c>market_data</c> rows (every row whose <c>session_id</c> matches); prices are session-bound, so no time-window scoping applies.</param>
/// <remarks>
/// Locally-autoincrement PK columns (<c>session_id</c>, <c>session_time_id</c>,
/// <c>log_id</c>, <c>marketdata_id</c>) are serialised verbatim by the exporter
/// for diagnostic readability but are deliberately ignored by the importer:
/// they are regenerated on insert. EVE-side stable IDs (<c>pilot_id</c>,
/// <c>station_id</c>) are preserved on import (with collision policy: skip
/// existing row, log Warning if name differs).
/// </remarks>
public sealed record SessionExportData(
    IReadOnlyList<SessionRow>        Session,
    IReadOnlyList<SessionTimeRow>    SessionTimes,
    IReadOnlyList<PilotRow>          Pilots,
    IReadOnlyList<SessionPilotRow>   SessionPilots,
    IReadOnlyList<LogRow>            Logs,
    IReadOnlyList<MarketStationRow>  MarketStations,
    IReadOnlyList<MarketDataRow>     MarketData);

/// <summary>One row from the <c>session</c> table.</summary>
/// <param name="SessionId">Auto-generated PK in the source DB. Ignored on import (regenerated).</param>
/// <param name="Name">Template-expanded session name as stored in <c>session.name</c>.</param>
/// <param name="Notes">Free-form notes; nullable.</param>
/// <param name="Archived">Mirror of <c>session.archived</c>. Absent from files written before the column existed — defaults to <c>false</c>. An imported archived session stays archived (hidden).</param>
/// <param name="MergedInto">Mirror of <c>session.merged_into</c> — the combined session this one was folded into, or <c>null</c>. Carried verbatim; it may name a session the target store does not have (informational only, never dereferenced).</param>
public sealed record SessionRow(
    [property: JsonConverter(typeof(FlexibleSessionIdJsonConverter))] string SessionId,
    string  Name,
    string? Notes,
    bool    Archived = false,
    string? MergedInto = null);

/// <summary>One row from the <c>session_time</c> table.</summary>
/// <param name="SessionTimeId">Auto-generated PK in the source DB. Ignored on import (regenerated).</param>
/// <param name="SessionId">FK to the parent session. Rewritten on import to the new session id. Legacy export files carry an integer here; <see cref="FlexibleSessionIdJsonConverter"/> reads it as its decimal string so legacy exports still deserialise.</param>
/// <param name="StartTime">ISO-8601 UTC string. When this active interval (or pause) began.</param>
/// <param name="StopTime">ISO-8601 UTC string. Null if this row is the still-open tail of a running session — saved sessions never have a null stop_time.</param>
/// <param name="IsPause">0 = active interval, 1 = pause window. SQLite CHECK constrains to {0, 1}.</param>
public sealed record SessionTimeRow(
    long    SessionTimeId,
    [property: JsonConverter(typeof(FlexibleSessionIdJsonConverter))] string SessionId,
    string  StartTime,
    string? StopTime,
    int     IsPause);

/// <summary>One row from the <c>pilot</c> table.</summary>
/// <param name="PilotId">EVE character id. Stable across machines — preserved on import.</param>
/// <param name="Name">Display name from the gamelog Listener header.</param>
public sealed record PilotRow(
    long   PilotId,
    string Name);

/// <summary>One row from the <c>session_pilot</c> association table.</summary>
/// <param name="SessionId">FK to <c>session</c>. Rewritten on import to the new session id.</param>
/// <param name="PilotId">FK to <c>pilot</c>. Preserved on import (stable EVE id).</param>
/// <param name="Role">Current fleet role string (Miner / Booster / Cyno / Combat). On import from a legacy file that lacks the field, the custom <c>JsonConverter</c> defaults missing values to <c>"Miner"</c>.</param>
public sealed record SessionPilotRow(
    string SessionId,
    long   PilotId,
    string Role);

/// <summary>One row from the <c>log</c> table.</summary>
/// <param name="LogId">Auto-generated PK in the source DB. Ignored on import (regenerated).</param>
/// <param name="SessionId">FK to <c>session</c>. Rewritten on import to the new session id.</param>
/// <param name="PilotId">FK to <c>pilot</c>. Preserved on import (stable EVE id).</param>
/// <param name="EveTimestamp">ISO-8601 UTC string from EVE's <c>[YYYY.MM.DD HH:MM:SS]</c> log line prefix.</param>
/// <param name="EventKind">One of <c>Tick</c> / <c>Crit</c> / <c>Residue</c> / <c>Depleted</c> per the <c>log.event_kind</c> CHECK constraint.</param>
/// <param name="TypeId">Source ore/material type id. Null for <c>Residue</c> and <c>Depleted</c> rows; non-null for <c>Tick</c> and <c>Crit</c>.</param>
/// <param name="Units">Raw count from the log line. Null for <c>Depleted</c>.</param>
/// <param name="VolumeM3">Denormalised <c>units * volume_per_unit</c> at write time. Null for <c>Residue</c> (no type_id) and <c>Depleted</c>.</param>
/// <param name="Role">Event-time fleet role (Miner / Booster / Cyno / Combat). On import from a legacy file that lacks the field, the custom <c>JsonConverter</c> defaults missing values to <c>"Miner"</c>.</param>
public sealed record LogRow(
    long    LogId,
    string  SessionId,
    long    PilotId,
    string  EveTimestamp,
    string  EventKind,
    long?   TypeId,
    long?   Units,
    double? VolumeM3,
    string  Role);

/// <summary>One row from the <c>market_stations</c> table.</summary>
/// <param name="StationId">EVE station id. Stable across machines — preserved on import.</param>
/// <param name="SystemName">Solar system name (e.g. <c>Jita</c>).</param>
/// <param name="StationName">Station name without the system prefix (e.g. <c>IV - Moon 4 - Caldari Navy Assembly Plant</c>).</param>
/// <param name="RegionId">Region id; nullable for user-added stations whose region is unknown.</param>
/// <param name="RegionName">Region name; nullable for the same reason.</param>
public sealed record MarketStationRow(
    long    StationId,
    string  SystemName,
    string  StationName,
    long?   RegionId,
    string? RegionName);

/// <summary>One row from the <c>market_data</c> table.</summary>
/// <param name="MarketDataId">Auto-generated PK in the source DB. Ignored on import (regenerated).</param>
/// <param name="TypeId">EVE inventory type id. Informational FK to the SDE; not enforced (cross-DB).</param>
/// <param name="StationId">FK to <c>market_stations.station_id</c>. Preserved on import (stable EVE id).</param>
/// <param name="SessionId">Owning session's sha256 id. On export it is the session being exported; on import it is rewritten to the (declared or computed) session id.</param>
/// <param name="FetchedAt">ISO-8601 UTC string. When the price was fetched from GoonMetrics.</param>
/// <param name="IskBuy">Highest buy order. Null when GoonMetrics did not return a buy side.</param>
/// <param name="IskSell">Lowest sell order. Null when GoonMetrics did not return a sell side.</param>
public sealed record MarketDataRow(
    long    MarketDataId,
    long    TypeId,
    long    StationId,
    string  SessionId,
    string  FetchedAt,
    double? IskBuy,
    double? IskSell);
