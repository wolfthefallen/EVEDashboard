using System.Data.Common;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Persistence.Tests.Storage;

/// <summary>
/// Verifies the shared <see cref="SqliteSchemaExtensions.TryAddColumnAsync"/>
/// helper both schema initialisers use: it adds a missing column exactly once,
/// is a silent no-op when the column already exists, and surfaces a real SQL
/// error (unknown table) instead of swallowing it.
/// </summary>
public class SqliteSchemaExtensionsTests
{
    private const string InMemoryConnectionString = "Data Source=:memory:";

    private static async Task<IReadOnlyList<string>> ColumnsOfAsync(ISqliteDatabase db, string table) =>
        await db.QueryAsync(
            "SELECT name FROM pragma_table_info($t)",
            static r => r.GetString(0),
            new Dictionary<string, object?> { ["$t"] = table });

    [Fact]
    public async Task TryAddColumnAsync_adds_a_missing_column_and_returns_true()
    {
        await using var db = new SqliteDatabase(InMemoryConnectionString, NullLogger<SqliteDatabase>.Instance);
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY);");

        var added = await db.TryAddColumnAsync("t", "flag", "INTEGER NOT NULL DEFAULT 0");

        added.Should().BeTrue();
        (await ColumnsOfAsync(db, "t")).Should().BeEquivalentTo(new[] { "id", "flag" });
    }

    [Fact]
    public async Task TryAddColumnAsync_is_a_no_op_when_the_column_exists_and_returns_false()
    {
        await using var db = new SqliteDatabase(InMemoryConnectionString, NullLogger<SqliteDatabase>.Instance);
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, flag INTEGER NOT NULL DEFAULT 0);");
        await db.ExecuteNonQueryAsync("INSERT INTO t (id, flag) VALUES (1, 7)");

        var added = await db.TryAddColumnAsync("t", "flag", "INTEGER NOT NULL DEFAULT 0");

        added.Should().BeFalse();
        (await ColumnsOfAsync(db, "t")).Should().BeEquivalentTo(new[] { "id", "flag" });
        // Existing data is untouched by the no-op.
        (await db.QueryScalarAsync<long>("SELECT flag FROM t WHERE id = 1")).Should().Be(7);
    }

    [Fact]
    public async Task TryAddColumnAsync_applies_the_default_to_existing_rows()
    {
        await using var db = new SqliteDatabase(InMemoryConnectionString, NullLogger<SqliteDatabase>.Instance);
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY);");
        await db.ExecuteNonQueryAsync("INSERT INTO t (id) VALUES (1)");

        await db.TryAddColumnAsync("t", "flag", "INTEGER NOT NULL DEFAULT 0");
        await db.TryAddColumnAsync("t", "note", "TEXT");

        (await db.QueryScalarAsync<long>("SELECT flag FROM t WHERE id = 1")).Should().Be(0);
        (await db.QueryScalarAsync<string>("SELECT note FROM t WHERE id = 1")).Should().BeNull();
    }

    [Fact]
    public async Task TryAddColumnAsync_rethrows_when_the_table_does_not_exist()
    {
        await using var db = new SqliteDatabase(InMemoryConnectionString, NullLogger<SqliteDatabase>.Instance);

        // pragma_table_info on an unknown table yields no rows, so the helper
        // proceeds to the ALTER, which must fail loudly — nothing is swallowed
        // except the specific duplicate-column race.
        var act = async () => await db.TryAddColumnAsync("missing", "flag", "INTEGER");

        await act.Should().ThrowAsync<DbException>();
    }

    [Fact]
    public async Task TryAddColumnAsync_throws_on_null_database()
    {
        var act = async () => await SqliteSchemaExtensions.TryAddColumnAsync(null!, "t", "c", "TEXT");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
