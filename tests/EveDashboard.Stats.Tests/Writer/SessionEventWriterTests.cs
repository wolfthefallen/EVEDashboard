using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Events;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Writer;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Writer;

/// <summary>
/// Plan 12 writer tests: 9 tests covering the contract of
/// <see cref="SessionEventWriter"/> end-to-end against an in-memory SQLite
/// with the canonical session schema applied.
/// </summary>
public class SessionEventWriterTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long PilotId = 2123642033;
    private const string SessionId = "sess-writer-1";
    private const long VeldsparTypeId = 1230;
    private const double VeldsparVolumePerUnit = 0.1;

    /// <summary>
    /// Spin up a writer + ready-to-INSERT SQLite database. The disposable
    /// returned from this method tears down the writer (drains pending events)
    /// and disposes the database in the right order.
    /// </summary>
    private static async Task<TestRig> SetupAsync(SessionRunState initialState = SessionRunState.Running)
    {
        var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);

        // Pre-seed pilot + session + an open is_pause=0 session_time row so
        // FK constraints accept INSERTs to log.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES ($pid, 'TestPilot')",
            new Dictionary<string, object?> { ["$pid"] = PilotId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test', NULL)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = PilotId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, '2026-05-01T00:00:00.000Z', NULL, 0)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });

        var ctx = new ActiveSessionContext();
        ctx.Set(SessionId, initialState);

        var sde = new FakeSdeLookup();
        var capturing = new CapturingLogger<SessionEventWriter>();
        var writer = new SessionEventWriter(db, sde, ctx, capturing);

        return new TestRig(db, ctx, writer, capturing);
    }

    /// <summary>Build a Tick or Crit event for a known (resolvable) ore.</summary>
    private static MiningEvent ResolvableEvent(MiningEventKind kind, long amount = 100) =>
        new(
            Kind: kind,
            EveTimestamp: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CharacterId: PilotId,
            ProductName: "Veldspar",
            TypeId: VeldsparTypeId,
            Amount: amount);

    [Theory]
    [InlineData(MiningEventKind.Tick, 100L)]
    [InlineData(MiningEventKind.Crit, 25L)]
    public async Task WriteAsync_inserts_log_row_for_resolvable_Tick_or_Crit(MiningEventKind kind, long amount)
    {
        await using var rig = await SetupAsync();
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(ResolvableEvent(kind, amount), CancellationToken.None);
        await rig.Writer.StopAsync();

        var rows = await rig.Db.QueryAsync(
            "SELECT session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3 FROM log",
            r => new
            {
                Sid = r.GetString(0),
                Pid = r.GetInt64(1),
                Ts = r.GetString(2),
                Kind = r.GetString(3),
                Type = r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                Units = r.IsDBNull(5) ? (long?)null : r.GetInt64(5),
                Vol = r.IsDBNull(6) ? (double?)null : r.GetDouble(6),
            });

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Sid.Should().Be(SessionId);
        row.Pid.Should().Be(PilotId);
        row.Ts.Should().Be("2026-05-01T00:00:00.000Z");
        row.Kind.Should().Be(kind.ToString());
        row.Type.Should().Be(VeldsparTypeId);
        row.Units.Should().Be(amount);
        row.Vol.Should().BeApproximately(amount * VeldsparVolumePerUnit, 1e-9);

        captured.Should().HaveCount(1);
        captured[0].SessionId.Should().Be(SessionId);
        captured[0].LogId.Should().Be(1);
        captured[0].Kind.Should().Be(kind);
    }

    [Fact]
    public async Task WriteAsync_inserts_Residue_with_null_type_id_and_null_volume_m3()
    {
        await using var rig = await SetupAsync();
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        var residue = new MiningEvent(
            Kind: MiningEventKind.Residue,
            EveTimestamp: new DateTimeOffset(2026, 5, 1, 0, 0, 1, TimeSpan.Zero),
            CharacterId: PilotId,
            ProductName: string.Empty,
            TypeId: null,
            Amount: 7);

        await rig.Writer.WriteAsync(residue, CancellationToken.None);
        await rig.Writer.StopAsync();

        var rows = await rig.Db.QueryAsync(
            "SELECT event_kind, type_id, units, volume_m3 FROM log",
            r => new
            {
                Kind = r.GetString(0),
                Type = r.IsDBNull(1) ? (long?)null : r.GetInt64(1),
                Units = r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                Vol = r.IsDBNull(3) ? (double?)null : r.GetDouble(3),
            });

        rows.Should().HaveCount(1);
        rows[0].Kind.Should().Be("Residue");
        rows[0].Type.Should().BeNull();
        rows[0].Units.Should().Be(7);
        rows[0].Vol.Should().BeNull();

        captured.Should().HaveCount(1);
        captured[0].Kind.Should().Be(MiningEventKind.Residue);
    }

    [Theory]
    [InlineData(MiningEventKind.Tick)]
    [InlineData(MiningEventKind.Crit)]
    public async Task WriteAsync_drops_Tick_or_Crit_with_null_TypeId(MiningEventKind kind)
    {
        await using var rig = await SetupAsync();
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        var unresolved = new MiningEvent(
            Kind: kind,
            EveTimestamp: DateTimeOffset.UtcNow,
            CharacterId: PilotId,
            ProductName: "MysteriousNewOreXYZ",
            TypeId: null,
            Amount: 50);

        await rig.Writer.WriteAsync(unresolved, CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_drops_event_when_State_is_Paused()
    {
        await using var rig = await SetupAsync(SessionRunState.Paused);
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_drops_event_when_no_active_session()
    {
        await using var rig = await SetupAsync();
        rig.ActiveSession.Set(null, SessionRunState.None);
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Notifier_continues_after_subscriber_throws()
    {
        await using var rig = await SetupAsync();
        var goodCaptured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, _) => throw new InvalidOperationException("boom");
        rig.Writer.SessionEventAdded += (_, e) => goodCaptured.Add(e);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(1);
        goodCaptured.Should().HaveCount(1);
        rig.Logger.Entries.Should()
            .Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                       && e.Exception is InvalidOperationException
                       && e.Exception.Message == "boom");
    }

    [Fact]
    public async Task StartAsync_then_StopAsync_drains_queued_events()
    {
        await using var rig = await SetupAsync();
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        const int n = 50;
        for (var i = 0; i < n; i++)
        {
            await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick, amount: i + 1), CancellationToken.None);
        }
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(n);
    }

    [Fact]
    public async Task StartAsync_called_twice_throws()
    {
        await using var rig = await SetupAsync();
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        Func<Task> act = () => rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_throws_on_null_argument()
    {
        var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        var sde = new FakeSdeLookup();
        var ctx = new ActiveSessionContext();
        var log = NullLogger<SessionEventWriter>.Instance;

        ((Action)(() => _ = new SessionEventWriter(null!, sde, ctx, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SessionEventWriter(db, null!, ctx, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SessionEventWriter(db, sde, null!, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SessionEventWriter(db, sde, ctx, null!))).Should().Throw<ArgumentNullException>();

        db.Dispose();
    }

    /// <summary>Fixture wiring a writer + database + context together for a single test.</summary>
    private sealed class TestRig : IAsyncDisposable
    {
        public SqliteDatabase Db { get; }
        public ActiveSessionContext ActiveSession { get; }
        public SessionEventWriter Writer { get; }
        public CapturingLogger<SessionEventWriter> Logger { get; }

        public TestRig(SqliteDatabase db, ActiveSessionContext ctx, SessionEventWriter writer, CapturingLogger<SessionEventWriter> logger)
        {
            Db = db;
            ActiveSession = ctx;
            Writer = writer;
            Logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            await Writer.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    /// <summary>
    /// Minimal <see cref="ISdeLookup"/> for writer tests. Returns a Veldspar-shaped
    /// product for <see cref="VeldsparTypeId"/>, null otherwise. Plan 09 tests its
    /// real implementation; the writer's responsibility is to multiply by
    /// <see cref="MiningProduct.VolumePerUnitM3"/> and we test that here.
    /// </summary>
    private sealed class FakeSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string name) =>
            string.Equals(name, "Veldspar", StringComparison.OrdinalIgnoreCase) ? VeldsparTypeId : null;

        public MiningProduct? TryGetProduct(long typeId) =>
            typeId == VeldsparTypeId
                ? new MiningProduct(VeldsparTypeId, "Veldspar", VeldsparVolumePerUnit, PortionSize: 100)
                : null;

        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
