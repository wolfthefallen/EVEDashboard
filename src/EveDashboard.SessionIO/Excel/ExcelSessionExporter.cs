namespace EveDashboard.SessionIO.Excel;

using System.Globalization;
using ClosedXML.Excel;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes one session to an .xlsx workbook with a Summary tab + one tab per
/// database table + one tab per pilot. Human-readable; not round-trippable.
/// </summary>
public sealed class ExcelSessionExporter
{
    private readonly SessionTableReader  _reader;
    private readonly IReportingQueries   _queries;
    private readonly ISessionStatsRepository _statsRepo;
    private readonly ILogger<ExcelSessionExporter> _log;

    /// <summary>Construct the exporter.</summary>
    /// <param name="reader">Reader that materialises raw table rows.</param>
    /// <param name="queries">Reporting queries — reused for Summary tab + per-pilot ore breakdowns (do not duplicate aggregation SQL).</param>
    /// <param name="statsRepo">Session-stats repository — reused for the refined-with-ISK output (<see cref="SessionStats.RefinedOutput"/> session-wide + per-pilot <see cref="CharacterStats.RefinedOutput"/>); the same path the on-screen Lifetime Totals + compare tables use, NOT a new query.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public ExcelSessionExporter(
        SessionTableReader  reader,
        IReportingQueries   queries,
        ISessionStatsRepository statsRepo,
        ILogger<ExcelSessionExporter> log)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(statsRepo);
        ArgumentNullException.ThrowIfNull(log);
        _reader = reader;
        _queries = queries;
        _statsRepo = statsRepo;
        _log = log;
    }

    /// <summary>Export the session at <paramref name="sessionId"/> to <paramref name="filePath"/>.</summary>
    /// <param name="sessionId">Session id to export.</param>
    /// <param name="filePath">Absolute path of the destination .xlsx file. Overwritten if it exists.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if no session row matches <paramref name="sessionId"/>.</exception>
    public async Task ExportAsync(string sessionId, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sessionId);

        var data = await _reader.ReadAsync(sessionId, ct).ConfigureAwait(false);
        if (data.Session.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot export session {sessionId}: no row in 'session' table.");
        }

        var timeline   = await _queries.GetSessionTimelineAsync(sessionId, ct).ConfigureAwait(false);
        var pilots     = await _queries.GetPilotsForSessionAsync(sessionId, ct).ConfigureAwait(false);
        var oreSession = await _queries.GetOreBreakdownAsync(sessionId, pilotId: null, ct).ConfigureAwait(false);

        // Per-pilot ore breakdowns — fetch up-front so we can populate tabs deterministically.
        var perPilotOre = new Dictionary<long, IReadOnlyList<OreBreakdownRow>>(pilots.Count);
        foreach (var p in pilots)
        {
            perPilotOre[p.PilotId] = await _queries.GetOreBreakdownAsync(sessionId, p.PilotId, ct).ConfigureAwait(false);
        }

        // Reuse the existing refined-with-ISK path: SessionStats
        // carries session-wide RefinedOutput and per-pilot ByCharacter[id].RefinedOutput,
        // both yield-applied + ISK-priced + sorted by quantity desc. This is the same data
        // the on-screen Lifetime Totals + compare tables consume — NOT a new query/aggregation.
        // allowLazyFetch:false = saved-session read (never re-price). asOf only bounds an OPEN
        // session_time row, of which a Stopped session has none.
        var stats = await _statsRepo
            .GetSessionStatsAsync(sessionId, DateTimeOffset.UtcNow, allowLazyFetch: false, ct)
            .ConfigureAwait(false);

        using var workbook = new XLWorkbook();

        AppendSummarySheet(workbook, data, timeline, pilots, oreSession, perPilotOre, stats.RefinedOutput);
        AppendSessionSheet(workbook, data.Session);
        AppendSessionTimeSheet(workbook, data.SessionTimes);
        AppendPilotSheet(workbook, data.Pilots);
        AppendSessionPilotSheet(workbook, data.SessionPilots);
        AppendLogSheet(workbook, data.Logs);
        AppendMarketStationsSheet(workbook, data.MarketStations);
        AppendMarketDataSheet(workbook, data.MarketData);
        foreach (var p in pilots)
        {
            var pilotRefined = stats.ByCharacter.TryGetValue(p.PilotId, out var cs)
                ? cs.RefinedOutput
                : Array.Empty<RefinedMaterialAmount>();
            AppendPerPilotSheet(workbook, p, perPilotOre[p.PilotId], pilotRefined);
        }

        workbook.SaveAs(filePath);

        _log.LogInformation(
            "Exported session #{SessionId} ('{Name}') to {Path}: {Sheets} sheet(s).",
            sessionId, data.Session[0].Name, filePath, workbook.Worksheets.Count);
    }

    // ----- Summary -----

    private static void AppendSummarySheet(
        XLWorkbook                                     workbook,
        SessionExportData                              data,
        SessionTimeline                                timeline,
        IReadOnlyList<PilotSummaryRow>                 pilots,
        IReadOnlyList<OreBreakdownRow>                 oreSession,
        IReadOnlyDictionary<long, IReadOnlyList<OreBreakdownRow>> perPilotOre,
        IReadOnlyList<RefinedMaterialAmount>           refinedSession)
    {
        var ws = workbook.Worksheets.Add("Summary");
        var session = data.Session[0];
        var pausedDuration = SumPauseDuration(timeline.PauseWindows);

        // Session block
        var sessionStart = data.SessionTimes.Count == 0 ? null : data.SessionTimes[0].StartTime;
        var sessionStop  = LastClosedStopTime(data.SessionTimes);
        var totalVolume  = data.Logs.Sum(l => l.VolumeM3 ?? 0.0);
        var totalIsk     = oreSession.Sum(o => o.Isk);

        var row = 1;
        ws.Cell(row, 1).Value = "Name";              ws.Cell(row, 2).Value = session.Name; row++;
        ws.Cell(row, 1).Value = "Notes";             ws.Cell(row, 2).Value = session.Notes ?? string.Empty; row++;
        ws.Cell(row, 1).Value = "Start time (UTC)"; ws.Cell(row, 2).Value = sessionStart ?? string.Empty; row++;
        ws.Cell(row, 1).Value = "Stop time (UTC)";  ws.Cell(row, 2).Value = sessionStop ?? string.Empty; row++;
        ws.Cell(row, 1).Value = "Active duration";  ws.Cell(row, 2).Value = FormatHms(timeline.ActiveDuration); row++;
        ws.Cell(row, 1).Value = "Paused duration";  ws.Cell(row, 2).Value = FormatHms(pausedDuration); row++;
        ws.Cell(row, 1).Value = "Pilot count";      ws.Cell(row, 2).Value = pilots.Count; row++;
        ws.Cell(row, 1).Value = "Total m^3";        ws.Cell(row, 2).Value = totalVolume; row++;
        ws.Cell(row, 1).Value = "Total ISK";        ws.Cell(row, 2).Value = totalIsk; row++;
        row++;

        // Per-pilot block (includes the pilot's fleet Role column).
        ws.Cell(row, 1).Value = "Per pilot"; row++;
        ws.Cell(row, 1).Value = "Pilot name";
        ws.Cell(row, 2).Value = "Role";
        ws.Cell(row, 3).Value = "m^3";
        ws.Cell(row, 4).Value = "ISK";
        row++;
        foreach (var p in pilots)
        {
            var pilotOre  = perPilotOre[p.PilotId];
            var pilotM3   = pilotOre.Sum(o => o.VolumeM3);
            var pilotIsk  = pilotOre.Sum(o => o.Isk);
            ws.Cell(row, 1).Value = p.PilotName;
            ws.Cell(row, 2).Value = p.Role;
            ws.Cell(row, 3).Value = pilotM3;
            ws.Cell(row, 4).Value = pilotIsk;
            row++;
        }
        row++;

        // Per-ore block (session-wide)
        ws.Cell(row, 1).Value = "Per ore (session)"; row++;
        ws.Cell(row, 1).Value = "Ore name";
        ws.Cell(row, 2).Value = "m^3";
        ws.Cell(row, 3).Value = "ISK";
        row++;
        foreach (var o in oreSession)
        {
            ws.Cell(row, 1).Value = o.TypeName;
            ws.Cell(row, 2).Value = o.VolumeM3;
            ws.Cell(row, 3).Value = o.Isk;
            row++;
        }
        row++;

        // Total Refined Output block (session-wide). Quantity + ISK, reusing
        // SessionStats.RefinedOutput — the same data the on-screen Lifetime Totals
        // table shows. Columns mirror the Summary "Per ore" block style (name +
        // values, no type_id). Empty list => label + header only (matches the ore block).
        ws.Cell(row, 1).Value = "Total Refined Output (session)"; row++;
        ws.Cell(row, 1).Value = "Material";
        ws.Cell(row, 2).Value = "Quantity";
        ws.Cell(row, 3).Value = "Total ISK";
        row++;
        foreach (var m in refinedSession)
        {
            ws.Cell(row, 1).Value = m.Name;
            ws.Cell(row, 2).Value = m.Quantity;
            ws.Cell(row, 3).Value = m.Isk;
            row++;
        }
    }

    private static TimeSpan SumPauseDuration(IReadOnlyList<PauseWindowRow> pauseWindows)
    {
        var total = TimeSpan.Zero;
        foreach (var w in pauseWindows) total += (w.Stop - w.Start);
        return total;
    }

    private static string? LastClosedStopTime(IReadOnlyList<SessionTimeRow> rows)
    {
        string? latest = null;
        foreach (var r in rows)
        {
            if (r.StopTime is null) continue;
            if (latest is null || string.CompareOrdinal(r.StopTime, latest) > 0) latest = r.StopTime;
        }
        return latest;
    }

    private static string FormatHms(TimeSpan ts) =>
        ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    // ----- Per-table tabs (mirror the session-DB SQL DDL column order of each table) -----

    private static void AppendSessionSheet(XLWorkbook wb, IReadOnlyList<SessionRow> rows)
    {
        var ws = wb.Worksheets.Add("session");
        WriteHeader(ws, "session_id", "name", "notes");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.SessionId;
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = r.Notes ?? string.Empty;
        }
    }

    private static void AppendSessionTimeSheet(XLWorkbook wb, IReadOnlyList<SessionTimeRow> rows)
    {
        var ws = wb.Worksheets.Add("session_time");
        WriteHeader(ws, "session_time_id", "session_id", "start_time", "stop_time", "is_pause");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.SessionTimeId;
            ws.Cell(row, 2).Value = r.SessionId;
            ws.Cell(row, 3).Value = r.StartTime;
            ws.Cell(row, 4).Value = r.StopTime ?? string.Empty;
            ws.Cell(row, 5).Value = r.IsPause;
        }
    }

    private static void AppendPilotSheet(XLWorkbook wb, IReadOnlyList<PilotRow> rows)
    {
        var ws = wb.Worksheets.Add("pilot");
        WriteHeader(ws, "pilot_id", "name");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.PilotId;
            ws.Cell(row, 2).Value = r.Name;
        }
    }

    private static void AppendSessionPilotSheet(XLWorkbook wb, IReadOnlyList<SessionPilotRow> rows)
    {
        var ws = wb.Worksheets.Add("session_pilot");
        // role column included.
        WriteHeader(ws, "session_id", "pilot_id", "role");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.SessionId;
            ws.Cell(row, 2).Value = r.PilotId;
            ws.Cell(row, 3).Value = r.Role;
        }
    }

    private static void AppendLogSheet(XLWorkbook wb, IReadOnlyList<LogRow> rows)
    {
        var ws = wb.Worksheets.Add("log");
        // role column included.
        WriteHeader(ws, "log_id", "session_id", "pilot_id", "eve_timestamp", "event_kind", "type_id", "units", "volume_m3", "role");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.LogId;
            ws.Cell(row, 2).Value = r.SessionId;
            ws.Cell(row, 3).Value = r.PilotId;
            ws.Cell(row, 4).Value = r.EveTimestamp;
            ws.Cell(row, 5).Value = r.EventKind;
            if (r.TypeId   is { } tid) ws.Cell(row, 6).Value = tid;
            if (r.Units    is { } u)   ws.Cell(row, 7).Value = u;
            if (r.VolumeM3 is { } v)   ws.Cell(row, 8).Value = v;
            ws.Cell(row, 9).Value = r.Role;
        }
    }

    private static void AppendMarketStationsSheet(XLWorkbook wb, IReadOnlyList<MarketStationRow> rows)
    {
        var ws = wb.Worksheets.Add("market_stations");
        WriteHeader(ws, "station_id", "system_name", "station_name", "region_id", "region_name");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.StationId;
            ws.Cell(row, 2).Value = r.SystemName;
            ws.Cell(row, 3).Value = r.StationName;
            if (r.RegionId   is { } rid)  ws.Cell(row, 4).Value = rid;
            if (r.RegionName is { } rn)   ws.Cell(row, 5).Value = rn;
        }
    }

    private static void AppendMarketDataSheet(XLWorkbook wb, IReadOnlyList<MarketDataRow> rows)
    {
        var ws = wb.Worksheets.Add("market_data");
        WriteHeader(ws, "marketdata_id", "type_id", "station_id", "fetched_at", "isk_buy", "isk_sell");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.MarketDataId;
            ws.Cell(row, 2).Value = r.TypeId;
            ws.Cell(row, 3).Value = r.StationId;
            ws.Cell(row, 4).Value = r.FetchedAt;
            if (r.IskBuy  is { } b) ws.Cell(row, 5).Value = b;
            if (r.IskSell is { } s) ws.Cell(row, 6).Value = s;
        }
    }

    // ----- Per-pilot tab -----

    private static void AppendPerPilotSheet(
        XLWorkbook                       wb,
        PilotSummaryRow                  pilot,
        IReadOnlyList<OreBreakdownRow>   ore,
        IReadOnlyList<RefinedMaterialAmount> refined)
    {
        // Excel sheet names cap at 31 chars and forbid : \ / ? * [ ]
        var safe = SafeSheetName($"Pilot - {pilot.PilotName}");
        var ws = wb.Worksheets.Add(safe);

        var totalM3  = ore.Sum(o => o.VolumeM3);
        var totalIsk = ore.Sum(o => o.Isk);

        ws.Cell(1, 1).Value = "Pilot name";  ws.Cell(1, 2).Value = pilot.PilotName;
        ws.Cell(2, 1).Value = "Pilot ID";    ws.Cell(2, 2).Value = pilot.PilotId;
        ws.Cell(3, 1).Value = "Total m^3";   ws.Cell(3, 2).Value = totalM3;
        ws.Cell(4, 1).Value = "Total ISK";   ws.Cell(4, 2).Value = totalIsk;

        // Breakdown table — leave row 5 blank, headers on row 6.
        WriteHeader(ws, headerRow: 6, "type_id", "type_name", "m^3", "isk");
        for (var i = 0; i < ore.Count; i++)
        {
            var o = ore[i]; var row = i + 7;
            ws.Cell(row, 1).Value = o.TypeId;
            ws.Cell(row, 2).Value = o.TypeName;
            ws.Cell(row, 3).Value = o.VolumeM3;
            ws.Cell(row, 4).Value = o.Isk;
        }

        // Refined output table — one blank row after the ore data (rows 7..6+ore.Count),
        // then a label, a 4-col header, and the refined rows. Columns match the sibling
        // ore table; data reused from CharacterStats.RefinedOutput
        // (quantity + ISK), the same path the on-screen tables use.
        var refinedLabelRow = 7 + ore.Count + 1;
        ws.Cell(refinedLabelRow, 1).Value = "Refined output";
        WriteHeader(ws, headerRow: refinedLabelRow + 1, "type_id", "type_name", "quantity", "isk");
        for (var i = 0; i < refined.Count; i++)
        {
            var m = refined[i]; var r = refinedLabelRow + 2 + i;
            ws.Cell(r, 1).Value = m.TypeId;
            ws.Cell(r, 2).Value = m.Name;
            ws.Cell(r, 3).Value = m.Quantity;
            ws.Cell(r, 4).Value = m.Isk;
        }
    }

    private static string SafeSheetName(string raw)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = string.Concat(raw.Where(c => !invalid.Contains(c)));
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    // ----- helpers -----

    private static void WriteHeader(IXLWorksheet ws, params string[] columns) =>
        WriteHeader(ws, headerRow: 1, columns);

    private static void WriteHeader(IXLWorksheet ws, int headerRow, params string[] columns)
    {
        for (var c = 0; c < columns.Length; c++)
        {
            ws.Cell(headerRow, c + 1).Value = columns[c];
            ws.Cell(headerRow, c + 1).Style.Font.Bold = true;
        }
    }
}
