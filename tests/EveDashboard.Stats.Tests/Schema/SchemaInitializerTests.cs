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
    public async Task EnsureSchemaAsync_creates_all_ten_tables()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var tables = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
            r => r.GetString(0));

        // The ten declared tables plus sqlite_sequence (auto-created for
        // AUTOINCREMENT tables).
        tables.Should().BeEquivalentTo(new[]
        {
            "fleet",
            "fleet_pilot",
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
    public async Task EnsureSchemaAsync_creates_all_seven_indexes()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var indexes = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            r => r.GetString(0));

        indexes.Should().BeEquivalentTo(new[]
        {
            "idx_fleet_name",         // unique fleet display name
            "idx_fleet_pilot_fleet",  // per-fleet member seek
            "idx_log_session_pilot",
            "idx_log_session_typeid",
            "idx_market_data_lookup",
            "idx_session_pilot_pilot",
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
        // Ten declared tables + sqlite_sequence = 11.
        tableCount.Should().Be(11);
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
    public async Task EnsureSchemaAsync_seeds_schema_version_to_latest_on_fresh_db()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        // The canonical SchemaDdl already emits the latest shape, so a
        // fresh DB is stamped straight at the latest version (3), NOT 1.
        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("3");
    }

    [Fact]
    public async Task EnsureSchemaAsync_idempotent_schema_version_stays_3()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await SchemaInitializer.EnsureSchemaAsync(db);

        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("3");
    }

    [Fact]
    public async Task EnsureSchemaAsync_fresh_db_has_session_archive_columns_with_defaults()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ('abc', 'Run', NULL)");

        var archived = await db.QueryScalarAsync<long>("SELECT archived FROM session WHERE session_id = 'abc'");
        var mergedInto = await db.QueryScalarAsync<string>("SELECT merged_into FROM session WHERE session_id = 'abc'");
        archived.Should().Be(0, "a freshly created session is never archived");
        mergedInto.Should().BeNull();

        // The CHECK constraint rejects anything but 0 / 1.
        var act = async () => await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes, archived) VALUES ('bad', 'Run', NULL, 2)");
        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task EnsureSchemaAsync_upgrades_a_v2_store_to_v3_adding_session_archive_columns_and_preserving_data()
    {
        // Simulate a live v2 store: the full schema minus the v3 session
        // columns, with a session row and schema_version stamped at 2. The
        // next EnsureSchemaAsync (an app upgrade) must ADD the two columns,
        // bump the version to 3, and leave the existing row untouched with
        // the column defaults (archived = 0, merged_into = NULL).
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ('abc', 'Old Run', 'kept')");

        // Roll the store back to the v2 shape: drop the v3 columns, stamp version 2.
        await db.ExecuteScriptAsync(
            "ALTER TABLE session DROP COLUMN merged_into; ALTER TABLE session DROP COLUMN archived;");
        await db.ExecuteNonQueryAsync(
            "UPDATE setting SET value = '2' WHERE key = 'schema_version'");
        var before = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('session')", r => r.GetString(0));
        before.Should().BeEquivalentTo(new[] { "session_id", "name", "notes" }, "the rollback must produce the real v2 shape");

        // Upgrade.
        await SchemaInitializer.EnsureSchemaAsync(db);

        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("3");

        var after = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('session')", r => r.GetString(0));
        after.Should().BeEquivalentTo(new[] { "session_id", "name", "notes", "archived", "merged_into" });

        var row = await db.QueryAsync(
            "SELECT name, notes, archived, merged_into FROM session WHERE session_id = 'abc'",
            r => (Name: r.GetString(0), Notes: r.GetString(1), Archived: r.GetInt64(2), MergedInto: r.IsDBNull(3) ? null : r.GetString(3)));
        row.Should().ContainSingle();
        row[0].Name.Should().Be("Old Run", "the additive migration must not touch existing rows");
        row[0].Notes.Should().Be("kept");
        row[0].Archived.Should().Be(0, "pre-existing sessions are not archived by the upgrade");
        row[0].MergedInto.Should().BeNull();

        // Re-running is a no-op (no duplicate-column throw, version unchanged).
        await SchemaInitializer.EnsureSchemaAsync(db);
        (await db.QueryScalarAsync<string>("SELECT value FROM setting WHERE key = 'schema_version'")).Should().Be("3");
    }

    [Fact]
    public async Task EnsureSchemaAsync_upgrades_a_v1_store_through_the_whole_chain_creating_fleet_tables_and_preserving_data()
    {
        // Simulate a live v1 store: the full schema minus the v2 fleet tables,
        // with a session + pilot row and schema_version stamped at 1. The next
        // EnsureSchemaAsync (an app upgrade) must run the whole chain: add the
        // fleet tables (v2), then the session archive columns (v3 - already
        // present here because the DDL emitted them, so that step is a no-op),
        // stamp the latest version, and leave the existing rows untouched.
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        // Seed user data.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ('abc', 'Old Run', NULL)");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (42, 'Veteran')");

        // Roll the store back to a v1 shape: drop the v2 tables, stamp version 1.
        await db.ExecuteScriptAsync("DROP TABLE fleet_pilot; DROP TABLE fleet;");
        await db.ExecuteNonQueryAsync(
            "UPDATE setting SET value = '1' WHERE key = 'schema_version'");

        // Upgrade.
        await SchemaInitializer.EnsureSchemaAsync(db);

        var version = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'");
        version.Should().Be("3", "the chain runs every outstanding migration and stamps the latest version");

        var fleetTables = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('fleet','fleet_pilot') ORDER BY name",
            r => r.GetString(0));
        fleetTables.Should().BeEquivalentTo(new[] { "fleet", "fleet_pilot" });

        var sessionName = await db.QueryScalarAsync<string>(
            "SELECT name FROM session WHERE session_id = 'abc'");
        sessionName.Should().Be("Old Run", "the additive migration must not touch existing rows");

        var pilotName = await db.QueryScalarAsync<string>(
            "SELECT name FROM pilot WHERE pilot_id = 42");
        pilotName.Should().Be("Veteran");
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
