using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Schema;

public class SchemaInitializerTests
{
    private const string InMemory = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    [Fact]
    public async Task EnsureSchemaAsync_throws_on_null_target()
    {
        Func<Task> act = async () => await SchemaInitializer.EnsureSchemaAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EnsureSchemaAsync_creates_all_eight_tables()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var tables = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
            r => r.GetString(0));

        // The eight declared tables plus sqlite_sequence (auto-created for AUTOINCREMENT tables).
        tables.Should().BeEquivalentTo(new[]
        {
            "log",
            "market_data",
            "market_stations",
            "pilot",
            "session",
            "session_pilot",
            "session_time",
            "setting",
            "sqlite_sequence",
        });
    }

    [Fact]
    public async Task EnsureSchemaAsync_creates_all_five_indexes()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var indexes = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            r => r.GetString(0));

        indexes.Should().BeEquivalentTo(new[]
        {
            "idx_log_session_pilot",
            "idx_log_session_typeid",
            "idx_market_data_lookup",
            "idx_session_pilot_pilot",  // plan 35: supports default-to-last-role lookup
            "idx_session_time_session_id",
        });
    }

    [Fact]
    public async Task EnsureSchemaAsync_is_idempotent()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await SchemaInitializer.EnsureSchemaAsync(db);   // second call: no throw

        var tableCount = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
        // Eight declared tables + sqlite_sequence = 9.
        tableCount.Should().Be(9);
    }

    [Fact]
    public async Task EnsureSchemaAsync_sets_journal_mode_to_wal_on_file_database()
    {
        // WAL mode is reported as "memory" on :memory: connections — Microsoft.Data.Sqlite
        // ignores PRAGMA journal_mode=WAL for in-memory DBs. Use a temp file to verify
        // the production code path actually enables WAL.
        var path = Path.Combine(Path.GetTempPath(), $"sqlite-wal-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new SqliteDatabase($"Data Source={path}", NullLogger<SqliteDatabase>.Instance))
            {
                await SchemaInitializer.EnsureSchemaAsync(db);
                var mode = await db.QueryScalarAsync<string>("PRAGMA journal_mode");
                mode.Should().Be("wal");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            var walSide = path + "-wal";
            var shmSide = path + "-shm";
            if (File.Exists(walSide)) File.Delete(walSide);
            if (File.Exists(shmSide)) File.Delete(shmSide);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_sets_foreign_keys_on()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var fk = await db.QueryScalarAsync<long>("PRAGMA foreign_keys");
        fk.Should().Be(1);
    }

    [Fact]
    public async Task EnsureSchemaAsync_seeds_schema_version_to_1()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("1");
    }

    [Fact]
    public async Task EnsureSchemaAsync_idempotent_schema_version_stays_1()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await SchemaInitializer.EnsureSchemaAsync(db);

        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("1");
    }

    [Fact]
    public async Task Foreign_key_constraint_rejects_log_with_bogus_session_id()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        // Both session_id and pilot_id reference empty parent tables — FK should fail.
        Func<Task> act = async () => await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind) " +
            "VALUES (999, 999, '2026-05-01T00:00:00.000Z', 'Tick')");

        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task Setting_table_round_trips_a_json_value()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        const string markets = "[{\"system\":\"Jita\",\"station_id\":60003760}]";
        await db.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ($k, $v)",
            new Dictionary<string, object?> { ["$k"] = "markets", ["$v"] = markets });

        var roundTrip = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'markets'");
        roundTrip.Should().Be(markets);
    }
}
