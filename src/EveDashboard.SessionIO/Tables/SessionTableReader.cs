namespace EveDashboard.SessionIO.Tables;

using System.Data.Common;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Loads a single session's table-row payload from a session SQLite database
/// into a <see cref="SessionExportData"/>. Pure read; no writes, no schema
/// mutations.
/// </summary>
/// <remarks>
/// Used by both <c>JsonSessionExporter</c> and <c>ExcelSessionExporter</c> so
/// the SQL definition of "what rides along with a session export" is stated
/// exactly once in this file. Filtering decisions (which rows belong to the
/// session) follow plan 29 alignment 3.1 = B for market_data: per
/// <c>(type_id, station_id)</c> pair, the latest row where
/// <c>fetched_at &lt;= session_end</c>.
/// </remarks>
public sealed class SessionTableReader
{
    private readonly ISqliteDatabase _db;
    private readonly ILogger<SessionTableReader> _log;

    /// <summary>Construct the reader over a session SQLite wrapper.</summary>
    /// <param name="db">Session SQLite wrapper to read from.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public SessionTableReader(ISqliteDatabase db, ILogger<SessionTableReader> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Read every row that belongs to the given session: the session itself,
    /// its session_time intervals, the pilots associated through session_pilot,
    /// every log row, the market_data rows in scope (per the plan-29 rule),
    /// and the market_stations referenced by those market_data rows.
    /// </summary>
    /// <param name="sessionId">Session id to scope the read to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="SessionExportData"/>. The <c>Session</c> list is empty if no row matches the id.</returns>
    public async Task<SessionExportData> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        var session       = await ReadSessionAsync(sessionId, ct).ConfigureAwait(false);
        var sessionTimes  = await ReadSessionTimesAsync(sessionId, ct).ConfigureAwait(false);
        var sessionPilots = await ReadSessionPilotsAsync(sessionId, ct).ConfigureAwait(false);
        var pilots        = await ReadPilotsForSessionAsync(sessionId, ct).ConfigureAwait(false);
        var logs          = await ReadLogsAsync(sessionId, ct).ConfigureAwait(false);
        var marketData    = await ReadMarketDataAsync(sessionId, ct).ConfigureAwait(false);
        var stations      = await ReadStationsForMarketDataAsync(marketData, ct).ConfigureAwait(false);

        _log.LogDebug(
            "Read session {SessionId}: {Times} session_time / {Pilots} pilot / {Logs} log / {Stations} market_stations / {MarketData} market_data rows.",
            sessionId, sessionTimes.Count, pilots.Count, logs.Count, stations.Count, marketData.Count);

        return new SessionExportData(
            Session:        session,
            SessionTimes:   sessionTimes,
            Pilots:         pilots,
            SessionPilots:  sessionPilots,
            Logs:           logs,
            MarketStations: stations,
            MarketData:     marketData);
    }

    private Task<IReadOnlyList<SessionRow>> ReadSessionAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            "SELECT session_id, name, notes FROM session WHERE session_id = $sid",
            static r => new SessionRow(
                SessionId: r.GetString(0),
                Name:      r.GetString(1),
                Notes:     r.IsDBNull(2) ? null : r.GetString(2)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    private Task<IReadOnlyList<SessionTimeRow>> ReadSessionTimesAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            "SELECT session_time_id, session_id, start_time, stop_time, is_pause " +
            "FROM session_time WHERE session_id = $sid ORDER BY session_time_id",
            static r => new SessionTimeRow(
                SessionTimeId: r.GetInt64(0),
                SessionId:     r.GetString(1),
                StartTime:     r.GetString(2),
                StopTime:      r.IsDBNull(3) ? null : r.GetString(3),
                IsPause:       r.GetInt32(4)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    private Task<IReadOnlyList<SessionPilotRow>> ReadSessionPilotsAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            // Plan 35: include role column.
            "SELECT session_id, pilot_id, role FROM session_pilot WHERE session_id = $sid ORDER BY pilot_id",
            static r => new SessionPilotRow(
                SessionId: r.GetString(0),
                PilotId:   r.GetInt64(1),
                Role:      r.GetString(2)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    private Task<IReadOnlyList<PilotRow>> ReadPilotsForSessionAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            "SELECT p.pilot_id, p.name FROM pilot p " +
            "INNER JOIN session_pilot sp ON sp.pilot_id = p.pilot_id " +
            "WHERE sp.session_id = $sid ORDER BY p.pilot_id",
            static r => new PilotRow(
                PilotId: r.GetInt64(0),
                Name:    r.GetString(1)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    private Task<IReadOnlyList<LogRow>> ReadLogsAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            // Plan 35: include role column.
            "SELECT log_id, session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role " +
            "FROM log WHERE session_id = $sid ORDER BY log_id",
            static r => new LogRow(
                LogId:        r.GetInt64(0),
                SessionId:    r.GetString(1),
                PilotId:      r.GetInt64(2),
                EveTimestamp: r.GetString(3),
                EventKind:    r.GetString(4),
                TypeId:       r.IsDBNull(5) ? null : r.GetInt64(5),
                Units:        r.IsDBNull(6) ? null : r.GetInt64(6),
                VolumeM3:     r.IsDBNull(7) ? null : r.GetDouble(7),
                Role:         r.GetString(8)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    /// <summary>
    /// Plan 38: prices are session-bound, so the export carries exactly the
    /// session's own <c>market_data</c> rows — a plain <c>WHERE session_id =
    /// $sid</c>. (Replaces the plan-29 time-window scoping.) Zero-sentinel rows
    /// belong to no session and are never selected.
    /// </summary>
    private Task<IReadOnlyList<MarketDataRow>> ReadMarketDataAsync(string sessionId, CancellationToken ct) =>
        _db.QueryAsync(
            """
            SELECT marketdata_id, type_id, station_id, session_id, fetched_at, isk_buy, isk_sell
            FROM market_data
            WHERE session_id = $sid
            ORDER BY type_id, station_id
            """,
            static r => new MarketDataRow(
                MarketDataId: r.GetInt64(0),
                TypeId:       r.GetInt64(1),
                StationId:    r.GetInt64(2),
                SessionId:    r.GetString(3),
                FetchedAt:    r.GetString(4),
                IskBuy:       r.IsDBNull(5) ? null : r.GetDouble(5),
                IskSell:      r.IsDBNull(6) ? null : r.GetDouble(6)),
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct);

    /// <summary>
    /// Read only the <c>market_stations</c> rows referenced by the supplied
    /// <c>market_data</c> rows. Avoids dragging in unrelated catalog entries.
    /// </summary>
    private async Task<IReadOnlyList<MarketStationRow>> ReadStationsForMarketDataAsync(
        IReadOnlyList<MarketDataRow> marketData, CancellationToken ct)
    {
        if (marketData.Count == 0) return Array.Empty<MarketStationRow>();

        // Distinct station ids referenced by the in-scope market_data rows.
        var ids = new HashSet<long>(marketData.Count);
        foreach (var md in marketData) ids.Add(md.StationId);

        // Build an "IN (...)" clause with one parameter per id. Bounded by the
        // size of `market_stations` (~tens of rows even in the worst case).
        var paramNames = new List<string>(ids.Count);
        var parameters = new Dictionary<string, object?>(ids.Count);
        var i = 0;
        foreach (var id in ids)
        {
            var name = $"$st{i}";
            paramNames.Add(name);
            parameters[name] = id;
            i++;
        }

        var sql =
            "SELECT station_id, system_name, station_name, region_id, region_name " +
            "FROM market_stations WHERE station_id IN (" + string.Join(", ", paramNames) + ") " +
            "ORDER BY station_id";

        return await _db.QueryAsync(
            sql,
            static r => new MarketStationRow(
                StationId:    r.GetInt64(0),
                SystemName:   r.GetString(1),
                StationName:  r.GetString(2),
                RegionId:     r.IsDBNull(3) ? null : r.GetInt64(3),
                RegionName:   r.IsDBNull(4) ? null : r.GetString(4)),
            parameters,
            ct).ConfigureAwait(false);
    }
}
