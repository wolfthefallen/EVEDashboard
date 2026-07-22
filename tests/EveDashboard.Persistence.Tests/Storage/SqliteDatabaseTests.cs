using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Persistence.Tests.Storage;

public class SqliteDatabaseTests
{
    private const string InMemoryConnectionString = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory(ILogger<SqliteDatabase>? log = null)
        => new(InMemoryConnectionString, log ?? NullLogger<SqliteDatabase>.Instance);

    [Fact]
    public void Constructor_throws_on_null_connection_string()
    {
        Action act = () => _ = new SqliteDatabase(null!, NullLogger<SqliteDatabase>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionString");
    }

    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        Action act = () => _ = new SqliteDatabase(InMemoryConnectionString, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    [Fact]
    public async Task OpenInMemory_succeeds_and_can_run_a_query()
    {
        await using var db = OpenInMemory();
        var result = await db.QueryScalarAsync<long>("SELECT 1");
        result.Should().Be(1);
    }

    [Fact]
    public async Task QueryScalarAsync_double_parses_text_invariantly_regardless_of_thread_culture()
    {
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync("CREATE TABLE t (v TEXT NOT NULL); INSERT INTO t (v) VALUES ('1.5');");

        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Under de-DE, '.' is a group separator; a culture-sensitive Convert.ChangeType
            // would read '1.5' as 15. The InvariantCulture fix must return 1.5 on any locale.
            // (CurrentCulture flows across the await via ExecutionContext.)
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var value = await db.QueryScalarAsync<double>("SELECT v FROM t");

            value.Should().Be(1.5);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void OpenFile_creates_file_if_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sqlite-test-{Guid.NewGuid():N}.db");
        try
        {
            File.Exists(path).Should().BeFalse("test path must not pre-exist");
            using (var db = new SqliteDatabase($"Data Source={path}", NullLogger<SqliteDatabase>.Instance))
            {
                File.Exists(path).Should().BeTrue("constructor opens the connection which materializes the file");
            }
            // Connection is now disposed; file handle released — safe to clean up below.
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteScriptAsync_runs_multi_statement_script()
    {
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync(
            """
            CREATE TABLE foo (id INTEGER);
            INSERT INTO foo VALUES (1);
            INSERT INTO foo VALUES (2);
            INSERT INTO foo VALUES (3);
            """);

        var count = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM foo");
        count.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteScriptAsync_throws_and_logs_on_invalid_sql()
    {
        var capturing = new CapturingLogger<SqliteDatabase>();
        await using var db = OpenInMemory(capturing);

        Func<Task> act = () => db.ExecuteScriptAsync("THIS IS NOT SQL");
        await act.Should().ThrowAsync<SqliteException>();

        capturing.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Exception is SqliteException);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_with_parameters_inserts_correctly()
    {
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync("CREATE TABLE products (name TEXT);");

        var rows = await db.ExecuteNonQueryAsync(
            "INSERT INTO products (name) VALUES ($name)",
            new Dictionary<string, object?> { ["$name"] = "Veldspar" });
        rows.Should().Be(1);

        var name = await db.QueryScalarAsync<string>("SELECT name FROM products LIMIT 1");
        name.Should().Be("Veldspar");
    }

    [Fact]
    public async Task QueryAsync_maps_rows_via_mapper()
    {
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync(
            """
            CREATE TABLE ores (typeId INTEGER, name TEXT);
            INSERT INTO ores VALUES (1230, 'Veldspar');
            INSERT INTO ores VALUES (1228, 'Scordite');
            INSERT INTO ores VALUES (18, 'Plagioclase');
            """);

        var rows = await db.QueryAsync(
            "SELECT typeId, name FROM ores ORDER BY typeId",
            r => new { TypeId = r.GetInt64(0), Name = r.GetString(1) });

        rows.Should().HaveCount(3);
        rows[0].TypeId.Should().Be(18);
        rows[0].Name.Should().Be("Plagioclase");
        rows[2].TypeId.Should().Be(1230);
        rows[2].Name.Should().Be("Veldspar");
    }

    [Fact]
    public async Task QueryScalarAsync_returns_default_on_empty_result()
    {
        await using var db = OpenInMemory();
        await db.ExecuteScriptAsync("CREATE TABLE empty (id INTEGER);");

        var result = await db.QueryScalarAsync<int>("SELECT id FROM empty");
        result.Should().Be(default);
    }

    [Fact]
    public async Task QueryScalarAsync_returns_default_on_null_value()
    {
        await using var db = OpenInMemory();
        var result = await db.QueryScalarAsync<int>("SELECT NULL");
        result.Should().Be(default);
    }

    [Fact]
    public async Task Dispose_closes_connection_and_subsequent_calls_throw_ObjectDisposedException()
    {
        var db = OpenInMemory();
        db.Dispose();

        Func<Task> act = () => db.ExecuteScriptAsync("SELECT 1");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_closes_connection_and_subsequent_calls_throw_ObjectDisposedException()
    {
        var db = OpenInMemory();
        await db.DisposeAsync();

        Func<Task> act = () => db.ExecuteScriptAsync("SELECT 1");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var db = OpenInMemory();
        db.Dispose();

        Action act = () => db.Dispose();
        act.Should().NotThrow();
    }
}
