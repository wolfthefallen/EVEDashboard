using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Schema;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Schema;

public class SchemaInitializerTests
{
    private const string InMemory = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    [Fact]
    public async Task EnsureSchemaAsync_creates_all_five_tables()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var tables = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
            r => r.GetString(0));

        tables.Should().BeEquivalentTo(new[]
        {
            "categories", "groups", "meta", "type_materials", "types"
        });
    }

    [Fact]
    public async Task EnsureSchemaAsync_creates_all_indexes()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var indexes = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            r => r.GetString(0));

        indexes.Should().Contain(new[]
        {
            "idx_groups_category_id",
            "idx_types_group_id",
            "idx_types_is_ore",
            "idx_types_name",
            "idx_type_materials_material_type_id",
        });
    }

    [Fact]
    public async Task EnsureSchemaAsync_enables_foreign_keys()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var fk = await db.QueryScalarAsync<long>("PRAGMA foreign_keys");
        fk.Should().Be(1);
    }

    [Fact]
    public async Task EnsureSchemaAsync_enables_WAL_mode_on_file_database()
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
            // WAL leaves -wal and -shm sidecar files; clean both up.
            var walSide = path + "-wal";
            var shmSide = path + "-shm";
            if (File.Exists(walSide)) File.Delete(walSide);
            if (File.Exists(shmSide)) File.Delete(shmSide);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_is_idempotent()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        await SchemaInitializer.EnsureSchemaAsync(db);   // second call: no throw

        var tableCount = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
        tableCount.Should().Be(5);
    }

    [Fact]
    public async Task EnsureSchemaAsync_FK_violation_is_rejected()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        // Insert a group whose category_id doesn't exist — should fail FK.
        Func<Task> act = () => db.ExecuteNonQueryAsync(
            "INSERT INTO groups (group_id, category_id, name) VALUES (999, 99999, 'orphan')");

        await act.Should().ThrowAsync<SqliteException>()
            .Where(ex => ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureSchemaAsync_throws_on_null_target()
    {
        Func<Task> act = () => SchemaInitializer.EnsureSchemaAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("target");
    }

    [Fact]
    public async Task EnsureSchemaAsync_creates_meta_table_with_last_modified_and_etag_columns()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var columns = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('meta') ORDER BY name",
            r => r.GetString(0));

        columns.Should().Contain(new[] { "last_modified", "etag" });
        columns.Should().Contain(new[]
        {
            "id", "build_time", "esi_sha256", "sde_sha256", "hoboleaks_sha256",
        });
    }

    [Fact]
    public async Task EnsureSchemaAsync_on_already_migrated_db_logs_no_errors()
    {
        // Regression guard: EnsureSchemaAsync now runs on EVERY app launch (wired into
        // the SDE-storage bootstrap). On an already-migrated DB the column-add must be
        // a silent no-op — a blind ALTER would throw "duplicate column", which
        // SqliteDatabase logs at Error (with a stack trace) before rethrowing, spamming
        // the log for every existing user on every launch. TryAddColumnAsync pre-checks
        // the column list to avoid the throw entirely.
        var log = new CapturingLogger<SqliteDatabase>();
        await using var db = new SqliteDatabase(InMemory, log);

        await SchemaInitializer.EnsureSchemaAsync(db);   // creates the full schema incl. columns
        await SchemaInitializer.EnsureSchemaAsync(db);   // columns present -> must not ALTER/throw/log

        log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task EnsureSchemaAsync_migrates_v1_meta_table_by_adding_last_modified_and_etag()
    {
        // Build a v1-shape meta table (no last_modified, no etag) to simulate a
        // database created before the migration columns existed — this is the
        // shape of the shipped fallback before plan 07.
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync(
            """
            CREATE TABLE meta (
                id               INTEGER PRIMARY KEY CHECK (id = 1),
                build_time       TEXT NOT NULL,
                esi_sha256       TEXT,
                sde_sha256       TEXT,
                hoboleaks_sha256 TEXT
            );
            INSERT INTO meta (id, build_time) VALUES (1, '2026-04-30T00:00:00Z');
            """);

        var preColumns = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('meta')",
            r => r.GetString(0));
        preColumns.Should().NotContain("last_modified");
        preColumns.Should().NotContain("etag");

        // Run schema initialiser — should add the missing columns idempotently.
        await SchemaInitializer.EnsureSchemaAsync(db);

        var postColumns = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('meta')",
            r => r.GetString(0));
        postColumns.Should().Contain(new[] { "last_modified", "etag" });

        // The pre-existing row must still be there with original data intact,
        // and the new columns must be NULL on it (no default backfill expected).
        var row = await db.QueryAsync(
            "SELECT build_time, last_modified, etag FROM meta WHERE id = 1",
            r => (
                BuildTime:    r.GetString(0),
                LastModified: r.IsDBNull(1) ? null : r.GetString(1),
                Etag:         r.IsDBNull(2) ? null : r.GetString(2)));
        row.Should().ContainSingle();
        row[0].BuildTime.Should().Be("2026-04-30T00:00:00Z");
        row[0].LastModified.Should().BeNull();
        row[0].Etag.Should().BeNull();

        // Idempotent: a second call must not throw and must leave the schema unchanged.
        await SchemaInitializer.EnsureSchemaAsync(db);
        var finalColumns = await db.QueryAsync(
            "SELECT name FROM pragma_table_info('meta')",
            r => r.GetString(0));
        finalColumns.Should().BeEquivalentTo(postColumns);
    }
}
