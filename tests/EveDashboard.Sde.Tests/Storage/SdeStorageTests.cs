using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Storage;

public class SdeStorageTests
{
    /// <summary>Allocate a unique temporary path that does not yet exist on disk.</summary>
    private static string NewTempPath(string? subdir = null)
    {
        var leaf = $"sde-storage-test-{Guid.NewGuid():N}.sqlite";
        return subdir is null
            ? Path.Combine(Path.GetTempPath(), leaf)
            : Path.Combine(Path.GetTempPath(), subdir, leaf);
    }

    /// <summary>Best-effort cleanup — tests must never leave files behind.</summary>
    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) &&
            dir != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        Action act = () => _ = new SdeStorage(null!, "ignored");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DatabasePath_returns_explicit_path_when_provided()
    {
        var explicitPath = NewTempPath();
        var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, explicitPath);
        storage.DatabasePath.Should().Be(explicitPath);
    }

    [Fact]
    public void DefaultConstructor_uses_AppData_root()
    {
        var storage = new SdeStorage(NullLogger<SdeStorage>.Instance);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var expected = Path.Combine(appData, "EveDashboard", "sde", "eve.sqlite");

        storage.DatabasePath.Should().Be(expected);
        storage.DatabasePath.Should().StartWith(appData);
        storage.DatabasePath.Should().EndWith(Path.Combine("EveDashboard", "sde", "eve.sqlite"));
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_extracts_fallback_when_file_missing()
    {
        var path = NewTempPath();
        try
        {
            File.Exists(path).Should().BeFalse("precondition: temp path must not exist");
            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, path);

            var copied = await storage.EnsureDatabaseExistsAsync();

            copied.Should().BeTrue();
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(1_000_000,
                "the embedded fallback is ~4.7 MB");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_is_idempotent_on_existing_file()
    {
        var path = NewTempPath();
        try
        {
            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, path);
            (await storage.EnsureDatabaseExistsAsync()).Should().BeTrue();
            var firstSize = new FileInfo(path).Length;

            var copied = await storage.EnsureDatabaseExistsAsync();

            copied.Should().BeFalse("file already exists; no bootstrap should occur");
            new FileInfo(path).Length.Should().Be(firstSize, "file must not have been touched");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_creates_parent_directory_if_missing()
    {
        var subdir = $"sde-storage-test-parent-{Guid.NewGuid():N}";
        var path = NewTempPath(subdir);
        try
        {
            Directory.Exists(Path.GetDirectoryName(path)!).Should().BeFalse(
                "precondition: parent directory must not exist");

            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, path);
            await storage.EnsureDatabaseExistsAsync();

            Directory.Exists(Path.GetDirectoryName(path)!).Should().BeTrue();
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_extracted_file_is_a_valid_sqlite_database()
    {
        var path = NewTempPath();
        try
        {
            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, path);
            await storage.EnsureDatabaseExistsAsync();

            await using var db = new SqliteDatabase(
                $"Data Source={path}", NullLogger<SqliteDatabase>.Instance);

            var buildTime = await db.QueryScalarAsync<string>(
                "SELECT build_time FROM meta WHERE id = 1");
            buildTime.Should().NotBeNullOrEmpty(
                "the shipped fallback must carry a populated build_time");

            // The migration columns were added by SchemaInitializer at fallback
            // creation OR by the first open of an older fallback. Either way they
            // exist on the schema by now (verified by the SchemaInitializer tests).
            var columns = await db.QueryAsync(
                "SELECT name FROM pragma_table_info('meta')",
                r => r.GetString(0));
            columns.Should().Contain(new[] { "build_time", "esi_sha256", "sde_sha256", "hoboleaks_sha256" });
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_extracted_file_has_expected_table_set()
    {
        var path = NewTempPath();
        try
        {
            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, path);
            await storage.EnsureDatabaseExistsAsync();

            await using var db = new SqliteDatabase(
                $"Data Source={path}", NullLogger<SqliteDatabase>.Instance);

            var tables = await db.QueryAsync(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name",
                r => r.GetString(0));

            tables.Should().BeEquivalentTo(new[]
            {
                "categories", "groups", "meta", "type_materials", "types",
            });
        }
        finally
        {
            Cleanup(path);
        }
    }
}
