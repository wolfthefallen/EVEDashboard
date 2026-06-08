namespace EveDashboard.SessionIO.Tests.Tables;

using EveDashboard.Persistence.Storage;
using EveDashboard.SessionIO.Import;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies <see cref="SessionTableWriter"/> regenerates auto-PKs, preserves
/// EVE-stable ids, applies the "skip + Warning" pilot/station policy, and
/// hard-fails on schema_version mismatch.
/// </summary>
public sealed class SessionTableWriterTests : IAsyncLifetime
{
    private const string InMemory = "Data Source=:memory:";
    private SqliteDatabase _db = null!;
    private SessionTableWriter _writer = null!;

    public async Task InitializeAsync()
    {
        _db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(_db);
        _writer = new SessionTableWriter(_db, NullLogger<SessionTableWriter>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WriteAsync_inserts_session_with_fresh_id_and_rewrites_fks()
    {
        var data = SampleData(sourceSessionId: "42", pilotId: 90_000_001L);

        var result = await _writer.WriteAsync(data, fileSchemaVersion: 1);

        // Plan 38: a non-sha256 source id ("42") is regenerated to a content-addressed id.
        result.NewSessionId.Should().NotBe("42", "the source id is regenerated, not preserved");
        result.SessionName.Should().Be("Sample");
        result.Warnings.Should().BeEmpty();
        result.RowCounts.Pilots.Should().Be(1);
        result.RowCounts.SessionTimes.Should().Be(1);
        result.RowCounts.Logs.Should().Be(1);

        // Verify FKs in the destination DB point at NewSessionId, not "42".
        var logs = await _db.QueryAsync(
            "SELECT session_id FROM log",
            static r => r.GetString(0));
        logs.Should().ContainSingle().Which.Should().Be(result.NewSessionId);

        var times = await _db.QueryAsync(
            "SELECT session_id FROM session_time",
            static r => r.GetString(0));
        times.Should().ContainSingle().Which.Should().Be(result.NewSessionId);
    }

    [Fact]
    public async Task WriteAsync_keeps_existing_pilot_when_id_collides_and_name_matches()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES ($pid, $n)",
            new Dictionary<string, object?> { ["$pid"] = 90_000_001L, ["$n"] = "Asgar" });

        var data = SampleData(sourceSessionId: "42", pilotId: 90_000_001L, pilotName: "Asgar");

        var result = await _writer.WriteAsync(data, fileSchemaVersion: 1);

        result.Warnings.Should().BeEmpty();
        result.RowCounts.Pilots.Should().Be(0, "existing pilot row was kept; nothing new was inserted");

        var pilotCount = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM pilot");
        pilotCount.Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_warns_when_existing_pilot_has_different_name()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES ($pid, $n)",
            new Dictionary<string, object?> { ["$pid"] = 90_000_001L, ["$n"] = "Asgar" });

        var data = SampleData(sourceSessionId: "42", pilotId: 90_000_001L, pilotName: "AsgarRenamed");

        var result = await _writer.WriteAsync(data, fileSchemaVersion: 1);

        result.Warnings.Should().ContainSingle();
        var w = result.Warnings[0];
        w.Kind.Should().Be(NameMismatchKind.Pilot);
        w.Id.Should().Be(90_000_001L);
        w.ExistingName.Should().Be("Asgar");
        w.ImportedName.Should().Be("AsgarRenamed");

        // Existing name was kept.
        var name = await _db.QueryScalarAsync<string>(
            "SELECT name FROM pilot WHERE pilot_id = $pid",
            new Dictionary<string, object?> { ["$pid"] = 90_000_001L });
        name.Should().Be("Asgar");
    }

    [Fact]
    public async Task WriteAsync_warns_when_existing_station_has_different_name()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name) VALUES ($sid, $sys, $st)",
            new Dictionary<string, object?>
            {
                ["$sid"] = 60003760L,
                ["$sys"] = "Jita",
                ["$st"]  = "IV - Moon 4 - Caldari Navy Assembly Plant",
            });

        // Plan 38: this import carries no session_time rows, so the writer cannot
        // recompute an id from name+start — supply a real sha256 id it preserves.
        var sha = EveDashboard.Core.Sessions.SessionId.Compute("Test", "2026-05-07T10:00:00.000Z");
        var data = new SessionExportData(
            Session:        new[] { new SessionRow(sha, "Test", null) },
            SessionTimes:   Array.Empty<SessionTimeRow>(),
            Pilots:         Array.Empty<PilotRow>(),
            SessionPilots:  Array.Empty<SessionPilotRow>(),
            Logs:           Array.Empty<LogRow>(),
            MarketStations: new[]
            {
                new MarketStationRow(60003760L, "Jita", "IV - Moon 4 - Renamed", null, null),
            },
            MarketData:     Array.Empty<MarketDataRow>());

        var result = await _writer.WriteAsync(data, fileSchemaVersion: 1);

        result.Warnings.Should().ContainSingle();
        var w = result.Warnings[0];
        w.Kind.Should().Be(NameMismatchKind.Station);
        w.Id.Should().Be(60003760L);
    }

    [Fact]
    public async Task WriteAsync_throws_SchemaVersionMismatch_when_versions_differ()
    {
        var data = SampleData(sourceSessionId: "42", pilotId: 90_000_001L);

        var act = () => _writer.WriteAsync(data, fileSchemaVersion: 99);

        await act.Should().ThrowAsync<SchemaVersionMismatchException>()
            .Where(ex => ex.FileSchemaVersion == 99 && ex.CurrentSchemaVersion == 1);

        // No partial writes leaked into the DB.
        var sessionCount = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session");
        sessionCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_rolls_back_on_mid_import_failure()
    {
        // Force a failure by giving a log row a pilot_id that the import payload does NOT include
        // and that does not pre-exist in the destination DB. The FK check (PRAGMA foreign_keys = ON
        // by SchemaInitializer) trips on the log INSERT, the catch issues ROLLBACK, and no rows
        // should remain in any table.
        var goodPilot = new PilotRow(90_000_001L, "Asgar");
        var data = new SessionExportData(
            Session:        new[] { new SessionRow("42", "Atomic test", null) },
            SessionTimes:   new[] { new SessionTimeRow(100, "42", "2026-05-07T10:00:00Z", "2026-05-07T11:00:00Z", 0) },
            Pilots:         new[] { goodPilot },
            SessionPilots:  new[] { new SessionPilotRow("42", goodPilot.PilotId, "Miner") },
            Logs:           new[]
            {
                // log row references an unknown pilot_id 90_000_999 which is NOT in Pilots — FK violation.
                new LogRow(555, "42", 90_000_999L, "2026-05-07T10:01:00Z", "Tick", 1230, 100, 10.0, "Miner"),
            },
            MarketStations: Array.Empty<MarketStationRow>(),
            MarketData:     Array.Empty<MarketDataRow>());

        var act = () => _writer.WriteAsync(data, fileSchemaVersion: 1);
        await act.Should().ThrowAsync<Exception>();

        // Rollback verification — nothing landed.
        var sessionCount      = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session");
        var sessionTimeCount  = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session_time");
        var sessionPilotCount = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session_pilot");
        var pilotCount        = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM pilot");
        var logCount          = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");

        sessionCount.Should().Be(0);
        sessionTimeCount.Should().Be(0);
        sessionPilotCount.Should().Be(0);
        pilotCount.Should().Be(0);
        logCount.Should().Be(0);
    }

    private static SessionExportData SampleData(
        string sourceSessionId,
        long pilotId,
        string pilotName = "Asgar")
    {
        return new SessionExportData(
            Session:        new[] { new SessionRow(sourceSessionId, "Sample", "notes") },
            SessionTimes:   new[]
            {
                new SessionTimeRow(100, sourceSessionId, "2026-05-07T10:00:00Z", "2026-05-07T11:00:00Z", 0),
            },
            Pilots:         new[] { new PilotRow(pilotId, pilotName) },
            SessionPilots:  new[] { new SessionPilotRow(sourceSessionId, pilotId, "Miner") },
            Logs:           new[]
            {
                new LogRow(555, sourceSessionId, pilotId, "2026-05-07T10:01:00Z", "Tick", 1230, 100, 10.0, "Miner"),
            },
            MarketStations: Array.Empty<MarketStationRow>(),
            MarketData:     Array.Empty<MarketDataRow>());
    }
}
