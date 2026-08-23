namespace EveDashboard.Persistence.Tests.Storage;

using System.Collections.Generic;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for the connection gate + <see cref="ISqliteDatabase.WithTransactionAsync{T}"/>.
/// These reproduce the original concurrency defect (concurrent access to the
/// single non-thread-safe connection corrupted the driver and threw a
/// non-<c>DbException</c>) and verify the transaction wrapper's atomicity and the
/// re-entrant un-gated path.
/// </summary>
public class SqliteDatabaseConcurrencyTests
{
    private static SqliteDatabase NewDb() =>
        new("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

    private static Dictionary<string, object?> P(string name, object? value) => new() { [name] = value };

    [Fact]
    public async Task Concurrent_reads_and_writes_do_not_corrupt_the_shared_connection()
    {
        // Pre-fix, interleaving inserts (background) with COUNT(*) reads (UI) on
        // one connection threw inside Microsoft.Data.Sqlite. The gate must make
        // this safe and keep the row count exact.
        await using var db = NewDb();
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, v INTEGER NOT NULL);");

        const int writers = 8;
        const int readers = 4;
        const int perTask = 200;

        var tasks = new List<Task>();
        for (var w = 0; w < writers; w++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var i = 0; i < perTask; i++)
                {
                    await db.ExecuteNonQueryAsync("INSERT INTO t (v) VALUES ($v)", P("$v", i));
                }
            }));
        }

        for (var r = 0; r < readers; r++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var i = 0; i < perTask; i++)
                {
                    _ = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM t");
                }
            }));
        }

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        var count = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM t");
        count.Should().Be(writers * perTask);
    }

    [Fact]
    public async Task WithTransactionAsync_commits_and_returns_the_body_result()
    {
        await using var db = NewDb();
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL);");

        var seenInside = await db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (1, 10)");
            await tx.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (2, 20)");
            return await tx.QueryScalarAsync<long>("SELECT COUNT(*) FROM t");
        });

        seenInside.Should().Be(2);
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM t")).Should().Be(2);
    }

    [Fact]
    public async Task WithTransactionAsync_rolls_back_on_body_exception_leaving_no_partial_rows()
    {
        await using var db = NewDb();
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL);");

        Func<Task> act = () => db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (1, 100)");
            throw new InvalidOperationException("boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM t")).Should().Be(0);
    }

    [Fact]
    public async Task WithTransactionAsync_body_calling_the_outer_db_does_not_deadlock()
    {
        // The SessionTableWriter pattern: the transaction body calls back into the
        // OUTER db (its helpers use _db). The AsyncLocal re-entrant path must run
        // those un-gated rather than self-deadlock on the held gate.
        await using var db = NewDb();
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL);");

        Func<Task> act = () => db.WithTransactionAsync(async (_, _) =>
        {
            await db.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (1, 1)");
            await db.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (2, 2)");
        });

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM t")).Should().Be(2);
    }

    [Fact]
    public async Task WithTransactionAsync_nested_call_throws_rather_than_deadlocking()
    {
        await using var db = NewDb();

        Func<Task> act = () => db.WithTransactionAsync(async (_, _) =>
        {
            await db.WithTransactionAsync((_, _) => Task.CompletedTask);
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RowChanged_handler_that_does_not_reenter_the_db_fires_under_the_gate()
    {
        await using var db = NewDb();
        await db.ExecuteScriptAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER NOT NULL);");

        var fired = 0;
        db.RowChanged += (_, e) => { if (e.Table == "t" && e.ChangeKind == SqliteRowChangeKind.Insert) fired++; };

        await db.ExecuteNonQueryAsync("INSERT INTO t (id, v) VALUES (1, 1)");

        fired.Should().Be(1);
    }
}
