using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Persistence.Tests.Storage;

/// <summary>
/// Verifies that <see cref="SqliteDatabase.RowChanged"/> forwards
/// <c>sqlite3_update_hook</c> notifications for INSERT / UPDATE / DELETE
/// with the correct table name, rowid, and change kind.
/// </summary>
public class SqliteDatabaseRowChangedTests
{
    private const string InMemoryConnectionString = "Data Source=:memory:";

    [Theory]
    [InlineData(SqliteRowChangeKind.Insert, "INSERT INTO t (rowid, v) VALUES (1, 'x')")]
    [InlineData(SqliteRowChangeKind.Update, "UPDATE t SET v = 'y' WHERE rowid = 1")]
    [InlineData(SqliteRowChangeKind.Delete, "DELETE FROM t WHERE rowid = 1")]
    public async Task RowChanged_fires_with_correct_args(SqliteRowChangeKind expectedKind, string mutatingSql)
    {
        await using var db = new SqliteDatabase(InMemoryConnectionString, NullLogger<SqliteDatabase>.Instance);

        await db.ExecuteScriptAsync("CREATE TABLE t (v TEXT);");
        if (expectedKind != SqliteRowChangeKind.Insert)
        {
            // UPDATE / DELETE need an existing row; seed it BEFORE subscribing
            // so the seed INSERT does not pollute the captured event list.
            await db.ExecuteNonQueryAsync("INSERT INTO t (rowid, v) VALUES (1, 'seed')");
        }

        var captured = new List<SqliteRowChangedEventArgs>();
        db.RowChanged += (_, e) => captured.Add(e);

        await db.ExecuteNonQueryAsync(mutatingSql);

        captured.Should().HaveCount(1);
        captured[0].Table.Should().Be("t");
        captured[0].RowId.Should().Be(1);
        captured[0].ChangeKind.Should().Be(expectedKind);
    }
}
