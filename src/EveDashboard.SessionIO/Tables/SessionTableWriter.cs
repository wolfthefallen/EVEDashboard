namespace EveDashboard.SessionIO.Tables;

using System.Globalization;
using System.Linq;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using EveDashboard.SessionIO.Import;
using EveDashboard.SessionIO.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes a <see cref="SessionExportData"/> payload into a session SQLite
/// database as a brand-new session, atomically. Implements the "skip + Warning"
/// pilot/station collision policy (an existing row wins; a differing name is
/// reported, never overwritten) and an export-FORMAT version check decoupled
/// from the DB schema_version — a file from a newer export format than this app
/// supports is rejected; an equal-or-older format imports.
/// </summary>
/// <remarks>
/// Locally-autoincremented PKs (<c>session_id</c>, <c>session_time_id</c>,
/// <c>log_id</c>, <c>marketdata_id</c>) are regenerated on insert; FKs that
/// pointed at the source <c>session_id</c> are rewritten to the new id. EVE-
/// side stable ids (<c>pilot_id</c>, <c>station_id</c>) are preserved.
/// Atomic via raw <c>BEGIN IMMEDIATE</c> / <c>COMMIT</c> / <c>ROLLBACK</c> SQL
/// — <see cref="ISqliteDatabase"/> does not expose a transaction API.
/// </remarks>
public sealed class SessionTableWriter
{
    private readonly ISqliteDatabase _db;
    private readonly ILogger<SessionTableWriter> _log;

    /// <summary>Construct the writer over a session SQLite wrapper.</summary>
    /// <param name="db">Session SQLite wrapper to write into.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public SessionTableWriter(ISqliteDatabase db, ILogger<SessionTableWriter> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Insert <paramref name="data"/> as a new session in the destination
    /// database. Any failure rolls back to the pre-import state.
    /// </summary>
    /// <param name="data">Session payload to write.</param>
    /// <param name="fileSchemaVersion">Export-format version from the import file's manifest (its <c>schema_version</c> wire field — see <see cref="SessionExportFormat"/>). Decoupled from the DB schema_version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ImportResult"/> with the new session id, name, collision warnings, and per-table row counts.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="data"/> contains zero or more than one <c>session</c> row (single-session export contract).</exception>
    /// <exception cref="SchemaVersionMismatchException">Thrown if <paramref name="fileSchemaVersion"/> is newer than this app's supported export format (<see cref="SessionExportFormat.CurrentVersion"/>).</exception>
    public async Task<ImportResult> WriteAsync(
        SessionExportData data,
        int               fileSchemaVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Session.Count != 1)
        {
            throw new ArgumentException(
                $"Single-session imports only — file contained {data.Session.Count} session rows.",
                nameof(data));
        }

        // The manifest's version is the export-FORMAT
        // version (SessionExportFormat.CurrentVersion), decoupled from the DB
        // schema_version. Reject only a file written by a NEWER export format
        // than this app understands; an equal-or-older format is importable (an
        // additive DB migration never changes the export shape, so an older
        // file's rows still fit). No DB schema_version read here.
        if (fileSchemaVersion > SessionExportFormat.CurrentVersion)
        {
            throw new SchemaVersionMismatchException(fileSchemaVersion, SessionExportFormat.CurrentVersion);
        }

        // Resolve the content-addressed session id — the file's id when
        // it already looks like a sha256 (new exports), else recompute from name
        // + first start_time (legacy integer-id files).
        var sessionId = ResolveSessionId(data);

        // Re-import dedup. If this session already
        // exists, write nothing and report AlreadyPresent.
        var existing = await _db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM session WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = sessionId },
            ct).ConfigureAwait(false);
        if (existing > 0)
        {
            _log.LogWarning(
                "Import skipped: session {SessionId} ('{Name}') already present in the destination DB.",
                sessionId, data.Session[0].Name);
            return new ImportResult(
                NewSessionId:   sessionId,
                SessionName:    data.Session[0].Name,
                Warnings:       Array.Empty<NameMismatchWarning>(),
                RowCounts:      new ImportRowCounts(0, 0, 0, 0, 0, 0),
                AlreadyPresent: true);
        }

        // Atomic via WithTransactionAsync (BEGIN IMMEDIATE..COMMIT, ROLLBACK on
        // throw) — holds the connection gate for the whole import so it cannot
        // interleave with a live session's writes. The Write* helpers call back
        // into _db; the re-entrant un-gated path keeps that from self-deadlocking.
        var result = await _db.WithTransactionAsync(async (_, _) =>
        {
            var warnings = new List<NameMismatchWarning>();

            var pilotsInserted   = await WritePilotsAsync(data.Pilots, warnings, ct).ConfigureAwait(false);
            var stationsInserted = await WriteStationsAsync(data.MarketStations, warnings, ct).ConfigureAwait(false);
            await WriteSessionAsync(data.Session[0], sessionId, ct).ConfigureAwait(false);
            var timesInserted    = await WriteSessionTimesAsync(data.SessionTimes, sessionId, ct).ConfigureAwait(false);
            var sessionPilots    = await WriteSessionPilotsAsync(data.SessionPilots, sessionId, ct).ConfigureAwait(false);
            var logsInserted     = await WriteLogsAsync(data.Logs, sessionId, ct).ConfigureAwait(false);
            var marketInserted   = await WriteMarketDataAsync(data.MarketData, sessionId, ct).ConfigureAwait(false);

            return new ImportResult(
                NewSessionId: sessionId,
                SessionName:  data.Session[0].Name,
                Warnings:     warnings,
                RowCounts:    new ImportRowCounts(
                    Pilots:         pilotsInserted,
                    MarketStations: stationsInserted,
                    SessionTimes:   timesInserted,
                    SessionPilots:  sessionPilots,
                    Logs:           logsInserted,
                    MarketData:     marketInserted));
        }, immediate: true, ct).ConfigureAwait(false);

        _log.LogInformation(
            "Imported session {NewSessionId} ('{Name}'): {Logs} log + {SessionTimes} session_time + {Pilots} new pilot + {Stations} new station + {MarketData} market_data; {Warnings} name warning(s).",
            result.NewSessionId, result.SessionName, result.RowCounts.Logs, result.RowCounts.SessionTimes,
            result.RowCounts.Pilots, result.RowCounts.MarketStations, result.RowCounts.MarketData, result.Warnings.Count);

        return result;
    }

    private async Task<int> WritePilotsAsync(
        IReadOnlyList<PilotRow>     pilots,
        List<NameMismatchWarning>   warnings,
        CancellationToken           ct)
    {
        var inserted = 0;
        foreach (var p in pilots)
        {
            var rowsAffected = await _db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
                new Dictionary<string, object?> { ["$pid"] = p.PilotId, ["$n"] = p.Name },
                ct).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                // Row already existed — keep the existing name; warn if it differs.
                var existing = await _db.QueryScalarAsync<string>(
                    "SELECT name FROM pilot WHERE pilot_id = $pid",
                    new Dictionary<string, object?> { ["$pid"] = p.PilotId },
                    ct).ConfigureAwait(false);
                if (existing is not null && !string.Equals(existing, p.Name, StringComparison.Ordinal))
                {
                    _log.LogWarning(
                        "Pilot {PilotId} import name '{Imported}' differs from existing '{Existing}'; keeping existing.",
                        p.PilotId, p.Name, existing);
                    warnings.Add(new NameMismatchWarning(NameMismatchKind.Pilot, p.PilotId, existing, p.Name));
                }
            }
            else
            {
                inserted++;
            }
        }
        return inserted;
    }

    private async Task<int> WriteStationsAsync(
        IReadOnlyList<MarketStationRow> stations,
        List<NameMismatchWarning>       warnings,
        CancellationToken               ct)
    {
        var inserted = 0;
        foreach (var s in stations)
        {
            var rowsAffected = await _db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
                "VALUES ($sid, $sys, $st, $rid, $rn)",
                new Dictionary<string, object?>
                {
                    ["$sid"] = s.StationId,
                    ["$sys"] = s.SystemName,
                    ["$st"]  = s.StationName,
                    ["$rid"] = s.RegionId,
                    ["$rn"]  = s.RegionName,
                },
                ct).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                // Existing row preserved. Surface mismatch on the composite "system - station" string.
                var existing = await _db.QueryAsync(
                    "SELECT system_name, station_name FROM market_stations WHERE station_id = $sid",
                    static r => new { System = r.GetString(0), Station = r.GetString(1) },
                    new Dictionary<string, object?> { ["$sid"] = s.StationId },
                    ct).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    var existingComposite = existing[0].System + " " + existing[0].Station;
                    var importedComposite = s.SystemName + " " + s.StationName;
                    if (!string.Equals(existingComposite, importedComposite, StringComparison.Ordinal))
                    {
                        _log.LogWarning(
                            "Station {StationId} import name '{Imported}' differs from existing '{Existing}'; keeping existing.",
                            s.StationId, importedComposite, existingComposite);
                        warnings.Add(new NameMismatchWarning(
                            NameMismatchKind.Station, s.StationId, existingComposite, importedComposite));
                    }
                }
            }
            else
            {
                inserted++;
            }
        }
        return inserted;
    }

    private Task WriteSessionAsync(SessionRow session, string sessionId, CancellationToken ct) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes, archived, merged_into) VALUES ($sid, $n, $notes, $archived, $merged)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sessionId, ["$n"] = session.Name, ["$notes"] = session.Notes,
                ["$archived"] = session.Archived ? 1 : 0, ["$merged"] = session.MergedInto,
            },
            ct);

    /// <summary>
    /// Resolve the session id to write. Returns the file's id verbatim
    /// when it already looks like a sha256 (new exports); otherwise recomputes
    /// from the session name and the earliest <c>session_time.start_time</c>
    /// (the first inserted row — matches the live + migration recipe).
    /// </summary>
    private static string ResolveSessionId(SessionExportData data)
    {
        var declared = data.Session[0].SessionId;
        if (SessionId.LooksLikeSha256(declared))
        {
            return declared;
        }
        if (data.SessionTimes.Count == 0)
        {
            throw new ArgumentException(
                "Cannot compute a session id: the import lacks a sha256 session_id and has no session_time rows.",
                nameof(data));
        }
        var firstStart = data.SessionTimes
            .OrderBy(t => t.SessionTimeId)
            .First()
            .StartTime;
        return SessionId.Compute(data.Session[0].Name, firstStart);
    }

    private async Task<int> WriteSessionTimesAsync(
        IReadOnlyList<SessionTimeRow> rows, string newSessionId, CancellationToken ct)
    {
        var inserted = 0;
        foreach (var r in rows)
        {
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
                "VALUES ($sid, $start, $stop, $is_pause)",
                new Dictionary<string, object?>
                {
                    ["$sid"]      = newSessionId,
                    ["$start"]    = r.StartTime,
                    ["$stop"]     = r.StopTime,
                    ["$is_pause"] = r.IsPause,
                },
                ct).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }

    private async Task<int> WriteSessionPilotsAsync(
        IReadOnlyList<SessionPilotRow> rows, string newSessionId, CancellationToken ct)
    {
        var inserted = 0;
        foreach (var r in rows)
        {
            // The role column is always written: the JsonConverter already defaulted
            // missing values to "Miner" at deserialise time, so r.Role is never null.
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $role)",
                new Dictionary<string, object?>
                {
                    ["$sid"]  = newSessionId,
                    ["$pid"]  = r.PilotId,
                    ["$role"] = r.Role,
                },
                ct).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }

    private async Task<int> WriteLogsAsync(
        IReadOnlyList<LogRow> rows, string newSessionId, CancellationToken ct)
    {
        var inserted = 0;
        foreach (var r in rows)
        {
            // The role column is always written: the JsonConverter already defaulted
            // missing values to "Miner" at deserialise time, so r.Role is never null.
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
                "VALUES ($sid, $pid, $ts, $kind, $tid, $u, $v, $role)",
                new Dictionary<string, object?>
                {
                    ["$sid"]  = newSessionId,
                    ["$pid"]  = r.PilotId,
                    ["$ts"]   = r.EveTimestamp,
                    ["$kind"] = r.EventKind,
                    ["$tid"]  = r.TypeId,
                    ["$u"]    = r.Units,
                    ["$v"]    = r.VolumeM3,
                    ["$role"] = r.Role,
                },
                ct).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }

    private async Task<int> WriteMarketDataAsync(
        IReadOnlyList<MarketDataRow> rows, string sessionId, CancellationToken ct)
    {
        // Plain INSERT (no OR IGNORE): market_data has no unique constraint
        // and the design accumulates rows forever.
        // Every imported price row is stamped with the (declared or
        // computed) session id, keeping overlapping imports isolated.
        var inserted = 0;
        foreach (var r in rows)
        {
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) " +
                "VALUES ($tid, $sid, $session, $f, $b, $s)",
                new Dictionary<string, object?>
                {
                    ["$tid"]     = r.TypeId,
                    ["$sid"]     = r.StationId,
                    ["$session"] = sessionId,
                    ["$f"]       = r.FetchedAt,
                    ["$b"]       = r.IskBuy,
                    ["$s"]       = r.IskSell,
                },
                ct).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }
}
