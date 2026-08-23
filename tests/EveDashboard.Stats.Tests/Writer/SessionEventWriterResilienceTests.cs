namespace EveDashboard.Stats.Tests.Writer;

using System.Data.Common;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Writer;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Belt-and-suspenders for the connection-concurrency defect. The writer's background consumer
/// loop must survive a NON-<c>DbException</c> thrown by a single insert and keep
/// draining; pre-fix it caught only <c>DbException</c>, so the fault killed the
/// consumer Task and silently lost every later event.
/// </summary>
public class SessionEventWriterResilienceTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long PilotId = 2123642033;
    private const string SessionId = "sess-resilience-1";
    private const long VeldsparTypeId = 1230;

    [Fact]
    public async Task ConsumeLoop_survives_a_non_DbException_insert_and_keeps_draining()
    {
        await using var inner = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(inner);
        await inner.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES ($pid, 'TestPilot')",
            new Dictionary<string, object?> { ["$pid"] = PilotId });
        await inner.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test', NULL)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });
        await inner.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = PilotId });
        await inner.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, '2026-05-01T00:00:00.000Z', NULL, 0)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });

        var failOnce = new FailOnceLogInsertDb(inner);
        var ctx = new ActiveSessionContext();
        ctx.Set(SessionId, SessionRunState.Running);

        await using var writer = new SessionEventWriter(failOnce, new NullSde(), ctx, NullLogger<SessionEventWriter>.Instance);
        await writer.StartAsync(SessionId, CancellationToken.None);

        // Three valid Ticks. The FIRST log insert throws ArgumentOutOfRangeException
        // (a non-DbException, modelling the shared-connection driver fault). The consumer must
        // log-and-continue so the remaining two still land.
        await writer.WriteAsync(Tick(10), CancellationToken.None);
        await writer.WriteAsync(Tick(20), CancellationToken.None);
        await writer.WriteAsync(Tick(30), CancellationToken.None);
        await writer.StopAsync();

        // All three were attempted — the consumer did NOT die after the first throw.
        failOnce.LogInsertAttempts.Should().Be(3);
        // The two that did not throw actually persisted.
        (await inner.QueryScalarAsync<long>("SELECT COUNT(*) FROM log")).Should().Be(2);
    }

    private static MiningEvent Tick(long amount) =>
        new(
            Kind: MiningEventKind.Tick,
            EveTimestamp: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CharacterId: PilotId,
            ProductName: "Veldspar",
            TypeId: VeldsparTypeId,
            Amount: amount);

    /// <summary>Minimal <see cref="ISdeLookup"/> stub — null/empty for everything (volume_m3 stays null, which the log table allows).</summary>
    private sealed class NullSde : ISdeLookup
    {
        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Decorates a real <see cref="ISqliteDatabase"/> and throws an
    /// <see cref="ArgumentOutOfRangeException"/> (NOT a <c>DbException</c>) on the
    /// FIRST <c>INSERT INTO log</c>, delegating everything else. Re-raises the
    /// inner <see cref="RowChanged"/> so the writer's notifier still works.
    /// </summary>
    private sealed class FailOnceLogInsertDb : ISqliteDatabase
    {
        private readonly ISqliteDatabase _inner;
        private int _logInserts;

        public FailOnceLogInsertDb(ISqliteDatabase inner)
        {
            _inner = inner;
            _inner.RowChanged += (_, e) => RowChanged?.Invoke(this, e);
        }

        public int LogInsertAttempts => _logInserts;

        public event EventHandler<SqliteRowChangedEventArgs>? RowChanged;

        public Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        {
            if (sql.Contains("INSERT INTO log", StringComparison.Ordinal))
            {
                var n = Interlocked.Increment(ref _logInserts);
                if (n == 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(sql), "simulated driver concurrency fault (NOT a DbException)");
                }
            }

            return _inner.ExecuteNonQueryAsync(sql, parameters, ct);
        }

        public Task ExecuteScriptAsync(string sqlText, CancellationToken ct = default) => _inner.ExecuteScriptAsync(sqlText, ct);

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> mapper, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
            => _inner.QueryAsync(sql, mapper, parameters, ct);

        public Task<T?> QueryScalarAsync<T>(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
            => _inner.QueryScalarAsync<T>(sql, parameters, ct);

        public Task WithTransactionAsync(Func<ISqliteTransaction, CancellationToken, Task> body, bool immediate = false, CancellationToken ct = default)
            => _inner.WithTransactionAsync(body, immediate, ct);

        public Task<T> WithTransactionAsync<T>(Func<ISqliteTransaction, CancellationToken, Task<T>> body, bool immediate = false, CancellationToken ct = default)
            => _inner.WithTransactionAsync(body, immediate, ct);

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
