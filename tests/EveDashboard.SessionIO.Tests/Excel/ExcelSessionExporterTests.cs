namespace EveDashboard.SessionIO.Tests.Excel;

using ClosedXML.Excel;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.SessionIO.Excel;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Queries;
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

        var statsRepo = new StubStatsRepository { Stats = MakeStats() };
        var exporter = new ExcelSessionExporter(reader, queries, statsRepo, NullLogger<ExcelSessionExporter>.Instance);
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

    [Fact]
    public async Task ExportAsync_writes_refined_output_tables()
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
            },
            PerPilotOre = new Dictionary<long, IReadOnlyList<OreBreakdownRow>>
            {
                [90_000_001L] = new[] { new OreBreakdownRow(1230, "Veldspar", 10.0, 7_500_000.0) },
                [90_000_002L] = new[] { new OreBreakdownRow(1228, "Scordite", 30.0, 12_000_000.0) },
            },
        };
        var statsRepo = new StubStatsRepository { Stats = MakeStats() };
        var exporter = new ExcelSessionExporter(reader, queries, statsRepo, NullLogger<ExcelSessionExporter>.Instance);
        var path = Path.Combine(_tempDir, "refined.xlsx");

        await exporter.ExportAsync(sid, path);

        using var workbook = new XLWorkbook(path);

        // Summary tab: session-wide "Total Refined Output" block (Material | Quantity | Total ISK).
        var summary = workbook.Worksheet("Summary");
        var label = summary.Column(1).CellsUsed()
            .FirstOrDefault(c => c.GetString() == "Total Refined Output (session)");
        label.Should().NotBeNull("the Summary tab should carry a session-wide refined block");
        var lr = label!.Address.RowNumber;
        summary.Cell(lr + 1, 1).GetString().Should().Be("Material");
        summary.Cell(lr + 1, 2).GetString().Should().Be("Quantity");
        summary.Cell(lr + 1, 3).GetString().Should().Be("Total ISK");
        summary.Cell(lr + 2, 1).GetString().Should().Be("Tritanium");
        summary.Cell(lr + 2, 2).GetDouble().Should().Be(1000);
        summary.Cell(lr + 2, 3).GetDouble().Should().BeApproximately(5_000_000.0, precision: 0.01);

        // Per-pilot tab: "Refined output" table (type_id | type_name | quantity | isk).
        var asgar = workbook.Worksheet("Pilot - Asgar");
        var refLabel = asgar.Column(1).CellsUsed()
            .FirstOrDefault(c => c.GetString() == "Refined output");
        refLabel.Should().NotBeNull("each pilot tab should carry a refined-output table");
        var rr = refLabel!.Address.RowNumber;
        asgar.Cell(rr + 1, 1).GetString().Should().Be("type_id");
        asgar.Cell(rr + 1, 2).GetString().Should().Be("type_name");
        asgar.Cell(rr + 1, 3).GetString().Should().Be("quantity");
        asgar.Cell(rr + 1, 4).GetString().Should().Be("isk");
        asgar.Cell(rr + 2, 1).GetDouble().Should().Be(34);
        asgar.Cell(rr + 2, 2).GetString().Should().Be("Tritanium");
        asgar.Cell(rr + 2, 3).GetDouble().Should().Be(1000);
        asgar.Cell(rr + 2, 4).GetDouble().Should().BeApproximately(5_000_000.0, precision: 0.01);
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

        public Task<SessionsReport> GetAllSessionSummariesAsync(bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(new SessionsReport(Array.Empty<SessionSummaryRow>(), LifetimeSummary.Empty));

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

    /// <summary>
    /// Canned <see cref="SessionStats"/> snapshot matching the seeded sample
    /// session: session-wide refined = Tritanium + Pyerite; Asgar refined =
    /// Tritanium; Bjorn refined = Pyerite. Non-refined fields are placeholders
    /// (the exporter only reads <c>RefinedOutput</c> / <c>ByCharacter[..].RefinedOutput</c>).
    /// </summary>
    private static SessionStats MakeStats()
    {
        var asgarRefined = new[] { new RefinedMaterialAmount(34, "Tritanium", 1000) { Isk = 5_000_000.0 } };
        var bjornRefined = new[] { new RefinedMaterialAmount(35, "Pyerite", 400) { Isk = 3_200_000.0 } };
        var sessionRefined = new[]
        {
            new RefinedMaterialAmount(34, "Tritanium", 1000) { Isk = 5_000_000.0 },
            new RefinedMaterialAmount(35, "Pyerite",   400)  { Isk = 3_200_000.0 },
        };
        var byChar = new Dictionary<long, CharacterStats>
        {
            [90_000_001L] = new CharacterStats(
                90_000_001L, null, 10.0, 0, 7_500_000.0, 0, 0, 0, 0, 0, 0, PilotRole.Miner)
            { RefinedOutput = asgarRefined },
            [90_000_002L] = new CharacterStats(
                90_000_002L, null, 30.0, 0, 12_000_000.0, 0, 0, 0, 0, 0, 0, PilotRole.Miner)
            { RefinedOutput = bjornRefined },
        };
        return new SessionStats(
            "sess-excel-sample", TimeSpan.FromHours(1), default, 40.0, 0,
            19_500_000.0, 0, 0, 0, 0, 0, 0, byChar)
        { RefinedOutput = sessionRefined };
    }

    /// <summary>Hand-rolled <see cref="ISessionStatsRepository"/> stub returning a canned snapshot.</summary>
    private sealed class StubStatsRepository : ISessionStatsRepository
    {
        public SessionStats Stats { get; init; } = null!;

        public Task<SessionStats> GetSessionStatsAsync(
            string sessionId, DateTimeOffset asOf, bool allowLazyFetch, CancellationToken ct = default) =>
            Task.FromResult(Stats);
    }
}
