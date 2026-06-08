using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Events;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Writer;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Writer;

/// <summary>
/// Plan 37 auto-resume tests for <see cref="SessionEventWriter"/>. Asserts
/// that the new inline callback path (1) flips state through a registered
/// callback, (2) preserves the triggering Tick/Crit into the <c>log</c>
/// table once state is Running, (3) drops non-trigger paused events
/// silently, (4) single-flights concurrent invocations, and (5) clears the
/// callback on Stop.
/// </summary>
public class SessionEventWriterAutoResumeTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long PilotId = 2123642033;
    private const string SessionId = "sess-writer-autoresume-1";
    private const long VeldsparTypeId = 1230;
    private const double VeldsparVolumePerUnit = 0.1;

    private static async Task<TestRig> SetupAsync(SessionRunState initialState = SessionRunState.Paused)
    {
        var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES ($pid, 'TestPilot')",
            new Dictionary<string, object?> { ["$pid"] = PilotId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test', NULL)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = PilotId });
        // Note: tests start in Paused — open session_time row reflects that.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, '2026-05-01T00:00:00.000Z', NULL, 1)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });

        var ctx = new ActiveSessionContext();
        ctx.Set(SessionId, initialState);

        var sde = new FakeSdeLookup();
        var capturing = new CapturingLogger<SessionEventWriter>();
        var writer = new SessionEventWriter(db, sde, ctx, capturing);

        return new TestRig(db, ctx, writer, capturing);
    }

    private static MiningEvent ResolvableEvent(MiningEventKind kind, long amount = 100) =>
        new(
            Kind: kind,
            EveTimestamp: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CharacterId: PilotId,
            ProductName: "Veldspar",
            TypeId: VeldsparTypeId,
            Amount: amount);

    private static MiningEvent NonTriggerEvent(MiningEventKind kind) =>
        new(
            Kind: kind,
            EveTimestamp: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CharacterId: PilotId,
            ProductName: kind == MiningEventKind.Residue ? string.Empty : "Veldspar",
            TypeId: kind == MiningEventKind.Residue ? null : VeldsparTypeId,
            Amount: kind == MiningEventKind.Depleted ? 0 : 7);

    [Theory]
    [InlineData(MiningEventKind.Tick)]
    [InlineData(MiningEventKind.Crit)]
    public async Task WriteAsync_paused_Tick_or_Crit_invokes_callback_and_INSERTs_the_triggering_event(MiningEventKind kind)
    {
        await using var rig = await SetupAsync();
        var captured = new List<SessionEventAddedEventArgs>();
        rig.Writer.SessionEventAdded += (_, e) => captured.Add(e);

        var callbackCallCount = 0;
        rig.Writer.SetAutoResumeCallback(_ =>
        {
            callbackCallCount++;
            // Real callback flips ActiveSession to Running before returning.
            rig.ActiveSession.Set(SessionId, SessionRunState.Running);
            return Task.CompletedTask;
        });

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(kind, amount: 50), CancellationToken.None);
        await rig.Writer.StopAsync();

        callbackCallCount.Should().Be(1, "the paused Tick/Crit must trigger exactly one auto-resume");

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(1, "the triggering event must land in log AFTER the callback flips state");

        captured.Should().HaveCount(1);
        captured[0].Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData(MiningEventKind.Residue)]
    [InlineData(MiningEventKind.Depleted)]
    public async Task WriteAsync_paused_non_trigger_event_drops_silently_without_invoking_callback(MiningEventKind kind)
    {
        await using var rig = await SetupAsync();
        var callbackCallCount = 0;
        rig.Writer.SetAutoResumeCallback(_ =>
        {
            callbackCallCount++;
            rig.ActiveSession.Set(SessionId, SessionRunState.Running);
            return Task.CompletedTask;
        });

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(NonTriggerEvent(kind), CancellationToken.None);
        await rig.Writer.StopAsync();

        callbackCallCount.Should().Be(0, "non-trigger paused events must drop without consulting the callback");
        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_paused_Tick_drops_silently_when_no_callback_registered()
    {
        await using var rig = await SetupAsync();
        // No SetAutoResumeCallback call — covers the regression case for the
        // existing "drops_event_when_State_is_Paused" behaviour.

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_when_callback_throws_drops_triggering_event_and_logs_warning()
    {
        await using var rig = await SetupAsync();
        rig.Writer.SetAutoResumeCallback(_ =>
            throw new InvalidOperationException("simulated resume failure"));

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
        rig.Logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Auto-resume callback threw"));
    }

    [Fact]
    public async Task WriteAsync_callback_that_leaves_state_Paused_drops_triggering_event()
    {
        await using var rig = await SetupAsync();
        // Callback intentionally doesn't flip state.
        rig.Writer.SetAutoResumeCallback(_ => Task.CompletedTask);

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_two_concurrent_paused_Ticks_share_a_single_callback_invocation_and_both_INSERT()
    {
        await using var rig = await SetupAsync();
        var callbackCallCount = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Writer.SetAutoResumeCallback(async _ =>
        {
            Interlocked.Increment(ref callbackCallCount);
            // Hold the callback open so the second WriteAsync arrives mid-flight.
            await gate.Task;
            rig.ActiveSession.Set(SessionId, SessionRunState.Running);
        });

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        var t1 = rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick, amount: 1), CancellationToken.None);
        // Tiny wait to ensure t1 entered the callback before t2 arrives.
        await Task.Delay(20);
        var t2 = rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick, amount: 2), CancellationToken.None);

        // Release the gate so both WriteAsync calls complete.
        gate.SetResult(true);
        await Task.WhenAll(t1, t2);
        await rig.Writer.StopAsync();

        callbackCallCount.Should().Be(1, "the single-flight cache must collapse concurrent paused Tick callers");
        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(2, "BOTH triggering events should land in log");
    }

    [Fact]
    public async Task SetAutoResumeCallback_null_clears_prior_registration()
    {
        await using var rig = await SetupAsync();
        var callbackCallCount = 0;
        rig.Writer.SetAutoResumeCallback(_ =>
        {
            callbackCallCount++;
            rig.ActiveSession.Set(SessionId, SessionRunState.Running);
            return Task.CompletedTask;
        });
        rig.Writer.SetAutoResumeCallback(null); // wipe

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        callbackCallCount.Should().Be(0);
        var count = await rig.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log");
        count.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_clears_callback_so_next_StartAsync_session_does_not_inherit()
    {
        await using var rig = await SetupAsync();
        var firstCallCount = 0;
        rig.Writer.SetAutoResumeCallback(_ =>
        {
            firstCallCount++;
            rig.ActiveSession.Set(SessionId, SessionRunState.Running);
            return Task.CompletedTask;
        });

        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.StopAsync();

        // After Stop, no callback is registered.
        rig.ActiveSession.Set(SessionId, SessionRunState.Paused);
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(ResolvableEvent(MiningEventKind.Tick), CancellationToken.None);
        await rig.Writer.StopAsync();

        firstCallCount.Should().Be(0, "Stop must clear the callback so a stale resume hook never fires on the next session's events");
    }

    /// <summary>Fixture mirroring the SessionEventWriterTests TestRig shape.</summary>
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

    private sealed class FakeSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string productName) =>
            string.Equals(productName, "Veldspar", StringComparison.OrdinalIgnoreCase) ? VeldsparTypeId : null;

        public MiningProduct? TryGetProduct(long typeId) =>
            typeId == VeldsparTypeId
                ? new MiningProduct(VeldsparTypeId, "Veldspar", VeldsparVolumePerUnit, PortionSize: 100)
                : null;

        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
