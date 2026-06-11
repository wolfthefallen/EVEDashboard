using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Writer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Writer;

/// <summary>
/// Plan 35: <see cref="SessionEventWriter.InsertLogRowAsync"/> stamps the
/// pilot's CURRENT role (read from <c>ActiveSessionContext</c>'s in-memory
/// role map) onto every <c>log</c> INSERT.
/// </summary>
public class SessionEventWriterRoleTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long PilotId = 2123642033;
    private const string SessionId = "sess-writer-role-1";
    private const long VeldsparTypeId = 1230;

    private sealed class Rig : IAsyncDisposable
    {
        public SqliteDatabase Db { get; }
        public ActiveSessionContext Ctx { get; }
        public SessionEventWriter Writer { get; }

        public Rig(SqliteDatabase db, ActiveSessionContext ctx, SessionEventWriter writer)
        {
            Db = db;
            Ctx = ctx;
            Writer = writer;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Writer.DisposeAsync(); } catch { /* test cleanup */ }
            try { Db.Dispose(); } catch { /* test cleanup */ }
        }
    }

    private static async Task<Rig> SetupAsync()
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
            "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, 'Miner')",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = PilotId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, '2026-05-01T00:00:00.000Z', NULL, 0)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });

        var ctx = new ActiveSessionContext();
        ctx.Set(SessionId, SessionRunState.Running);

        var sde = new FakeSdeLookup();
        var writer = new SessionEventWriter(db, sde, ctx, NullLogger<SessionEventWriter>.Instance);
        return new Rig(db, ctx, writer);
    }

    private static MiningEvent TickEvent(DateTimeOffset ts) =>
        new(
            Kind: MiningEventKind.Tick,
            EveTimestamp: ts,
            CharacterId: PilotId,
            ProductName: "Veldspar",
            TypeId: VeldsparTypeId,
            Amount: 100);

    [Theory]
    [InlineData(PilotRole.Miner)]
    [InlineData(PilotRole.Booster)]
    [InlineData(PilotRole.Cyno)]
    [InlineData(PilotRole.Combat)]
    public async Task INSERT_stamps_role_from_ActiveSessionContext(PilotRole role)
    {
        await using var rig = await SetupAsync();
        rig.Ctx.SeedRoles(new Dictionary<long, PilotRole> { [PilotId] = role });
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(TickEvent(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        await rig.Writer.StopAsync();

        var rows = await rig.Db.QueryAsync(
            "SELECT role FROM log",
            static r => r.GetString(0));
        rows.Should().ContainSingle();
        rows[0].Should().Be(PilotRoleExtensions.ToCanonicalString(role));
    }

    [Fact]
    public async Task Role_change_mid_session_only_affects_subsequent_events()
    {
        // Per the plan's Risks section, role is stamped at INSERT (consumer) time,
        // not at WriteAsync (producer) time. So this test must drain event 1 BEFORE
        // calling SetRole; otherwise the consumer might process event 1 after the
        // SetRole landed and stamp it Booster. The drain is via StopAsync + Start.
        await using var rig = await SetupAsync();
        rig.Ctx.SeedRoles(new Dictionary<long, PilotRole> { [PilotId] = PilotRole.Miner });
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        // Event 1: Miner. Drain before any role change.
        await rig.Writer.WriteAsync(
            TickEvent(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await rig.Writer.StopAsync();

        // Now flip role to Booster.
        rig.Ctx.SetRole(PilotId, PilotRole.Booster);

        // Event 2: should be Booster. Restart the writer for the second batch.
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);
        await rig.Writer.WriteAsync(
            TickEvent(new DateTimeOffset(2026, 5, 1, 0, 0, 1, TimeSpan.Zero)),
            CancellationToken.None);
        await rig.Writer.StopAsync();

        var rows = await rig.Db.QueryAsync(
            "SELECT eve_timestamp, role FROM log ORDER BY log_id",
            static r => (Ts: r.GetString(0), Role: r.GetString(1)));
        rows.Should().HaveCount(2);
        rows[0].Role.Should().Be("Miner",   "the first event was stamped before the role change");
        rows[1].Role.Should().Be("Booster", "the second event was stamped after the role change");
    }

    [Fact]
    public async Task Unmapped_pilot_defaults_to_Miner_role()
    {
        // Defensive default: GetRole returns Miner for any pilot id not in the map.
        // This shouldn't happen in practice (the seed includes every session pilot)
        // but the writer must not crash on the defensive path.
        await using var rig = await SetupAsync();
        // Do NOT SeedRoles — the map is empty.
        await rig.Writer.StartAsync(SessionId, CancellationToken.None);

        await rig.Writer.WriteAsync(
            TickEvent(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await rig.Writer.StopAsync();

        var role = await rig.Db.QueryScalarAsync<string>("SELECT role FROM log LIMIT 1");
        role.Should().Be("Miner");
    }

    /// <summary>
    /// Local fake matching the shape used by <see cref="SessionEventWriterTests"/>
    /// (same product / volume mapping for Veldspar so volume_m3 denormalisation
    /// works during INSERT).
    /// </summary>
    private sealed class FakeSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string name) =>
            string.Equals(name, "Veldspar", StringComparison.OrdinalIgnoreCase) ? VeldsparTypeId : null;

        public MiningProduct? TryGetProduct(long typeId) =>
            typeId == VeldsparTypeId
                ? new MiningProduct(VeldsparTypeId, "Veldspar", VolumePerUnitM3: 0.1, PortionSize: 100)
                : null;

        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
