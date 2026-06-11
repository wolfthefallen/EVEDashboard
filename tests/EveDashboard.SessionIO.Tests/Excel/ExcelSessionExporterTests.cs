namespace EveDashboard.SessionIO.Tests.Excel;

using ClosedXML.Excel;
using EveDashboard.Persistence.Storage;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.SessionIO.Excel;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies <see cref="ExcelSessionExporter"/> writes the expected sheet names,
/// summary cells, and per-pilot tabs.
/// </summary>
public sealed class ExcelSessionExporterTests : IAsyncLifetime, IDisposable
{
    private const string InMemory = "Data Source=:memory:";
    private SqliteDatabase _db = null!;
    private string _tempDir = null!;

    public async Task InitializeAsync()
    {
        _db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(_db);
        _tempDir = Path.Combine(Path.GetTempPath(), "EveDashboard.SessionIO.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ExportAsync_writes_summary_table_and_per_pilot_tabs()
    {
        var sid = await SeedSampleSessionAsync();

        var reader = new SessionTableReader(_db, NullLogger<SessionTableReader>.Instance);
        var queries = new StubReportingQueries
        {
            Timeline = new SessionTimeline(
                SessionStart:   new DateTimeOffset(2026, 5, 7, 10, 0, 0, TimeSpan.Zero),
                ActiveDuration: TimeSpan.FromHours(1),
                PauseWindows:   Array.Empty<PauseWindowRow>()),
            Pilots = new[]
            {
                new PilotSummaryRow(90_000_001L, "Asgar", "Miner"),
                new PilotSummaryRow(90_000_002L, "Bjorn", "Miner"),
            },
            SessionOre = new[]
            {
                new OreBreakdownRow(1230, "Veldspar", 10.0, 7_500_000.0),
                new OreBreakdownRow(1228, "Scordite", 30.0, 12_000_000.0),
            },
            PerPilotOre = new Dictionary<long, IReadOnlyList<OreBreakdownRow>>
            {
                [90_000_001L] = new[] { new OreBreakdownRow(1230, "Veldspar", 10.0, 7_500_000.0) },
                [90_000_002L] = new[] { new OreBreakdownRow(1228, "Scordite", 30.0, 12_000_000.0) },
            },
        };

        var exporter = new ExcelSessionExporter(reader, queries, NullLogger<ExcelSessionExporter>.Instance);
        var path = Path.Combine(_tempDir, "session.xlsx");

        await exporter.ExportAsync(sid, path);

        File.Exists(path).Should().BeTrue();

        using var workbook = new XLWorkbook(path);

        // Summary tab is first.
        workbook.Worksheets.First().Name.Should().Be("Summary");

        // Required table tabs.
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToHashSet();
        sheetNames.Should().Contain(new[]
        {
            "Summary", "session", "session_time", "pilot", "session_pilot",
            "log", "market_stations", "market_data",
        });

        // Per-pilot tabs.
        sheetNames.Should().Contain("Pilot - Asgar");
        sheetNames.Should().Contain("Pilot - Bjorn");

        // Summary cells.
        var summary = workbook.Worksheet("Summary");
        summary.Cell(1, 1).GetString().Should().Be("Name");
        summary.Cell(1, 2).GetString().Should().Be("Sample");
        summary.Cell(7, 1).GetString().Should().Be("Pilot count");
        summary.Cell(7, 2).GetDouble().Should().Be(2);
        summary.Cell(9, 1).GetString().Should().Be("Total ISK");
        summary.Cell(9, 2).GetDouble().Should().BeApproximately(19_500_000.0, precision: 0.01);

        // log tab data row count.
        var logSheet = workbook.Worksheet("log");
        var logHeader = logSheet.Cell(1, 1).GetString();
        logHeader.Should().Be("log_id");
        // Row 2 onwards are data; we seeded 2 log rows.
        logSheet.Cell(2, 5).GetString().Should().Be("Tick");
        logSheet.Cell(3, 5).GetString().Should().Be("Tick");
    }

    private async Task<string> SeedSampleSessionAsync()
    {
        const string sid = "sess-excel-sample";
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'Sample', 'sample notes')",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, '2026-05-07T10:00:00Z', '2026-05-07T11:00:00Z', 0)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await _db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (90000001, 'Asgar')");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (90000002, 'Bjorn')");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, 90000001)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, 90000002)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await _db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, 90000001, '2026-05-07T10:01:00Z', 'Tick', 1230, 100, 10.0)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, 90000002, '2026-05-07T10:02:00Z', 'Tick', 1228, 200, 30.0)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        return sid;
    }

    /// <summary>Hand-rolled IReportingQueries stub returning canned data.</summary>
    private sealed class StubReportingQueries : IReportingQueries
    {
        public SessionTimeline                              Timeline    { get; init; } = null!;
        public IReadOnlyList<PilotSummaryRow>               Pilots      { get; init; } = Array.Empty<PilotSummaryRow>();
        public IReadOnlyList<OreBreakdownRow>               SessionOre  { get; init; } = Array.Empty<OreBreakdownRow>();
        public IReadOnlyDictionary<long, IReadOnlyList<OreBreakdownRow>> PerPilotOre { get; init; } =
            new Dictionary<long, IReadOnlyList<OreBreakdownRow>>();

        public Task<IReadOnlyList<SessionSummaryRow>> GetAllSessionSummariesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummaryRow>>(Array.Empty<SessionSummaryRow>());

        public Task<IReadOnlyList<PilotSummaryRow>> GetPilotsForSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(Pilots);

        public Task<IReadOnlyList<OreBreakdownRow>> GetOreBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default)
        {
            if (pilotId is null) return Task.FromResult(SessionOre);
            return Task.FromResult(PerPilotOre.TryGetValue(pilotId.Value, out var v) ? v : Array.Empty<OreBreakdownRow>());
        }

        public Task<IReadOnlyList<RefineMatBreakdownRow>> GetRefinedMatBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RefineMatBreakdownRow>>(Array.Empty<RefineMatBreakdownRow>());

        public Task<SessionTimeline> GetSessionTimelineAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(Timeline);
    }
}
