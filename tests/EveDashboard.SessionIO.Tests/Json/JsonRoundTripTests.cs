namespace EveDashboard.SessionIO.Tests.Json;

using EveDashboard.Persistence.Storage;
using EveDashboard.SessionIO.Import;
using EveDashboard.SessionIO.Json;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies a session round-trips through JSON export -> import on a fresh DB
/// and produces logically equivalent rows (autoincrement ids regenerated;
/// payload fields preserved).
/// </summary>
public sealed class JsonRoundTripTests : IDisposable
{
    private readonly string _tempDir;

    public JsonRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EveDashboard.SessionIO.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Export_then_import_into_fresh_db_preserves_session_payload()
    {
        // ----- source DB: seed a small session -----
        await using var sourceDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sourceDb);
        var sourceSid = await SeedSampleSessionAsync(sourceDb);

        var reader = new SessionTableReader(sourceDb, NullLogger<SessionTableReader>.Instance);
        var exporter = new JsonSessionExporter(reader, NullLogger<JsonSessionExporter>.Instance);
        var path = Path.Combine(_tempDir, "session.json");
        await exporter.ExportAsync(sourceSid, path);

        File.Exists(path).Should().BeTrue();

        // ----- destination DB: fresh schema, run the import -----
        await using var destDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(destDb);
        var writer = new SessionTableWriter(destDb, NullLogger<SessionTableWriter>.Instance);
        var importer = new JsonSessionImporter(writer, NullLogger<JsonSessionImporter>.Instance);

        var result = await importer.ImportAsync(path);

        // Plan 38: the new id is the content-addressed sha256 hex string. The
        // source id already looks like a sha256, so the importer preserves it.
        EveDashboard.Core.Sessions.SessionId.LooksLikeSha256(result.NewSessionId).Should().BeTrue();
        result.SessionName.Should().Be("RoundTrip");
        result.Warnings.Should().BeEmpty();
        result.RowCounts.Logs.Should().Be(2);
        result.RowCounts.SessionTimes.Should().Be(1);
        result.RowCounts.Pilots.Should().Be(2);

        // Payload checks against the destination DB.
        var pilotNames = await destDb.QueryAsync(
            "SELECT name FROM pilot ORDER BY pilot_id",
            static r => r.GetString(0));
        pilotNames.Should().BeEquivalentTo(new[] { "Asgar", "Bjorn" });

        var logCount = await destDb.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM log WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = result.NewSessionId });
        logCount.Should().Be(2);
    }

    [Fact]
    public async Task Import_legacy_integer_id_file_recomputes_sha256_and_stamps_market_data()
    {
        // Bug B regression (plan 38 amendment): pre-plan-38 export files carry
        // INTEGER session_ids on session / session_time / session_pilot / log
        // and a market_data block with NO session_id. This shape previously
        // threw at $.session_time[0].session_id because SessionTimeRow.SessionId
        // lacked a number-tolerant converter. It must now import cleanly: ids
        // recomputed to sha256, market_data stamped with the new id. Field shape
        // mirrors a real user export (verified against the failing file).
        const string LegacyJson = """
            {
              "_manifest": { "format": "EveDashboardSessionExport", "schema_version": 1, "app_version": "1.0.0.0", "exported_at": "2026-05-26T20:18:06.3099922+00:00", "session_id": 19, "session_name": "Legacy Import" },
              "session": [ { "session_id": 19, "name": "Legacy Import", "notes": null } ],
              "session_time": [ { "session_time_id": 45, "session_id": 19, "start_time": "2026-05-17T22:06:31.086Z", "stop_time": "2026-05-17T23:16:08.127Z", "is_pause": 0 } ],
              "pilot": [ { "pilot_id": 90793684, "name": "Wolf TheFallen" } ],
              "session_pilot": [ { "session_id": 19, "pilot_id": 90793684 } ],
              "log": [ { "log_id": 108538, "session_id": 19, "pilot_id": 90793684, "eve_timestamp": "2026-05-17T22:06:31.000Z", "event_kind": "Tick", "type_id": 46675, "units": 94, "volume_m3": 752 } ],
              "market_stations": [ { "station_id": 60003760, "system_name": "Jita", "station_name": "IV - Moon 4", "region_id": 10000002, "region_name": "The Forge" } ],
              "market_data": [ { "market_data_id": 934, "type_id": 34, "station_id": 60003760, "fetched_at": "2026-05-06T21:59:20.4417879+00:00", "isk_buy": 3.97, "isk_sell": 4.07 } ]
            }
            """;
        var path = Path.Combine(_tempDir, "legacy.json");
        await File.WriteAllTextAsync(path, LegacyJson);

        await using var destDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(destDb);
        var writer = new SessionTableWriter(destDb, NullLogger<SessionTableWriter>.Instance);
        var importer = new JsonSessionImporter(writer, NullLogger<JsonSessionImporter>.Instance);

        var result = await importer.ImportAsync(path);

        // 19 is not a sha256 -> importer recomputes from (name, first start_time).
        var expected = EveDashboard.Core.Sessions.SessionId.Compute("Legacy Import", "2026-05-17T22:06:31.086Z");
        result.NewSessionId.Should().Be(expected);
        result.SessionName.Should().Be("Legacy Import");
        result.AlreadyPresent.Should().BeFalse();
        result.RowCounts.SessionTimes.Should().Be(1);
        result.RowCounts.Logs.Should().Be(1);
        result.RowCounts.MarketData.Should().Be(1);

        // The market_data row carried NO session_id in the file; it must be
        // stamped with the recomputed session id on import.
        var stampedCount = await destDb.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM market_data WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = expected });
        stampedCount.Should().Be(1);
    }

    [Fact]
    public async Task Import_throws_PortableFileFormatException_for_non_envelope_json()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(path, "{\"some_other_format\": true}");

        await using var destDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(destDb);
        var writer = new SessionTableWriter(destDb, NullLogger<SessionTableWriter>.Instance);
        var importer = new JsonSessionImporter(writer, NullLogger<JsonSessionImporter>.Instance);

        var act = () => importer.ImportAsync(path);
        await act.Should().ThrowAsync<PortableFileFormatException>();
    }

    [Fact]
    public async Task Import_throws_SchemaVersionMismatch_when_manifest_version_differs()
    {
        // Export a real session, then mutate the manifest in the file.
        await using var sourceDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sourceDb);
        var sourceSid = await SeedSampleSessionAsync(sourceDb);

        var reader = new SessionTableReader(sourceDb, NullLogger<SessionTableReader>.Instance);
        var exporter = new JsonSessionExporter(reader, NullLogger<JsonSessionExporter>.Instance);
        var path = Path.Combine(_tempDir, "version-mutated.json");
        await exporter.ExportAsync(sourceSid, path);

        var json = await File.ReadAllTextAsync(path);
        json = json.Replace("\"schema_version\": 1", "\"schema_version\": 99", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        await using var destDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(destDb);
        var writer = new SessionTableWriter(destDb, NullLogger<SessionTableWriter>.Instance);
        var importer = new JsonSessionImporter(writer, NullLogger<JsonSessionImporter>.Instance);

        var act = () => importer.ImportAsync(path);
        var ex = await act.Should().ThrowAsync<SchemaVersionMismatchException>();
        ex.Which.FileSchemaVersion.Should().Be(99);
        ex.Which.CurrentSchemaVersion.Should().Be(1);
    }

    private static async Task<string> SeedSampleSessionAsync(SqliteDatabase db)
    {
        // Plan 38: a realistic sha256 session id (matches the live recipe so the
        // exporter writes a valid id and the importer dedup / recompute paths see
        // a well-formed source id).
        var sid = EveDashboard.Core.Sessions.SessionId.Compute("RoundTrip", "2026-05-07T10:00:00.000Z");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'RoundTrip', 'sample notes')",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, '2026-05-07T10:00:00Z', '2026-05-07T11:00:00Z', 0)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (90000001, 'Asgar')");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (90000002, 'Bjorn')");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, 90000001)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, 90000002)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, 90000001, '2026-05-07T10:01:00Z', 'Tick', 1230, 100, 10.0)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, 90000002, '2026-05-07T10:02:00Z', 'Tick', 1228, 200, 30.0)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        return sid;
    }
}
