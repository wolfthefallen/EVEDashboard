namespace EveDashboard.Reporting.Tests.Queries;

using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Persistence.Storage;
using EveDashboard.Reporting.Queries;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class SqliteReportingQueriesTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long Pilot1 = 90_000_001L;
    private const long Pilot2 = 90_000_002L;
    private const long VeldsparTypeId = 1230L;
    private const long ScorditeTypeId = 1228L;

    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static SqliteReportingQueries NewQueries(
        SqliteDatabase db,
        ISessionStatsRepository? statsRepo = null,
        ISdeLookup? sdeLookup = null,
        IIskCalculator? iskCalculator = null,
        ISettingsRepository? settings = null) =>
        new(
            db,
            statsRepo     ?? new StubStatsRepo(),
            sdeLookup     ?? new StubSdeLookup(),
            iskCalculator ?? new StubIskCalculator(),
            settings      ?? new StubSettings(),
            NullLogger<SqliteReportingQueries>.Instance);

    private static async Task<string> InsertSessionAsync(SqliteDatabase db, string name)
    {
        // Plan 38: session ids are content-addressed sha256 hex strings. Each
        // distinct name within a test's DB hashes to a distinct id; that is
        // enough to keep FKs and per-session assertions unambiguous here.
        var sessionId = EveDashboard.Core.Sessions.SessionId.Compute(name, ToIso(T0));
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sessionId, ["$n"] = name });
        return sessionId;
    }

    private static async Task InsertSessionPilotAsync(SqliteDatabase db, string sessionId, long pilotId, string pilotName)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
            new Dictionary<string, object?> { ["$pid"] = pilotId, ["$n"] = pilotName });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
            new Dictionary<string, object?> { ["$sid"] = sessionId, ["$pid"] = pilotId });
    }

    private static async Task InsertSessionTimeAsync(
        SqliteDatabase db,
        string sessionId,
        DateTimeOffset start,
        DateTimeOffset? stop,
        bool isPause = false)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, $start, $stop, $is_pause)",
            new Dictionary<string, object?>
            {
                ["$sid"]      = sessionId,
                ["$start"]    = ToIso(start),
                ["$stop"]     = stop is null ? null : ToIso(stop.Value),
                ["$is_pause"] = isPause ? 1 : 0,
            });
    }

    private static async Task InsertLogAsync(
        SqliteDatabase db,
        string sessionId,
        long pilotId,
        DateTimeOffset ts,
        string kind,
        long? typeId,
        long? units,
        double? volumeM3)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, $pid, $ts, $kind, $type, $units, $vol)",
            new Dictionary<string, object?>
            {
                ["$sid"]   = sessionId,
                ["$pid"]   = pilotId,
                ["$ts"]    = ToIso(ts),
                ["$kind"]  = kind,
                ["$type"]  = typeId,
                ["$units"] = units,
                ["$vol"]   = volumeM3,
            });
    }

    private static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // ---- GetAllSessionSummariesAsync ----------------------------------------------

    [Fact]
    public async Task GetAllSessionSummariesAsync_returns_empty_list_for_empty_database()
    {
        await using var db = await NewSchemaAsync();
        var queries = NewQueries(db);

        var result = await queries.GetAllSessionSummariesAsync();

        result.Sessions.Should().BeEmpty();
        result.Lifetime.Should().Be(LifetimeSummary.Empty);
    }

    [Fact]
    public async Task GetAllSessionSummariesAsync_returns_one_row_per_saved_session_with_metadata()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "First Run");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(30));
        var s2 = await InsertSessionAsync(db, "Second Run");
        await InsertSessionPilotAsync(db, s2, Pilot1, "Alice");
        await InsertSessionPilotAsync(db, s2, Pilot2, "Bob");
        await InsertSessionTimeAsync(db, s2, T0.AddDays(1), T0.AddDays(1).AddMinutes(45));

        var statsRepo = new StubStatsRepo
        {
            ByIdSnapshots =
            {
                [s1] = MakeSnapshot(s1, TimeSpan.FromMinutes(30), totalIsk: 100_000.0),
                [s2] = MakeSnapshot(s2, TimeSpan.FromMinutes(45), totalIsk: 250_000.0),
            },
        };
        var queries = NewQueries(db, statsRepo);

        var result = await queries.GetAllSessionSummariesAsync();

        result.Sessions.Should().HaveCount(2);
        // Plan 38: ordered by start_time DESC -> newest first (sha256 ids are not chronological).
        result.Sessions[0].SessionId.Should().Be(s2);
        result.Sessions[0].Name.Should().Be("Second Run");
        result.Sessions[0].PilotCount.Should().Be(2);
        result.Sessions[0].ActiveDuration.Should().Be(TimeSpan.FromMinutes(45));
        result.Sessions[0].TotalIskMined.Should().Be(250_000.0);
        result.Sessions[0].StartTime.Should().BeCloseTo(T0.AddDays(1), TimeSpan.FromSeconds(1));
        result.Sessions[1].SessionId.Should().Be(s1);
        result.Sessions[1].PilotCount.Should().Be(1);
        result.Sessions[1].TotalIskMined.Should().Be(100_000.0);
    }

    [Fact]
    public async Task GetAllSessionSummariesAsync_active_duration_excludes_pause_windows()
    {
        // Active duration is supplied by ISessionStatsRepository (which itself
        // excludes paused intervals). This test asserts the reporting query
        // forwards that value verbatim.
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Paused Session");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(30));

        var statsRepo = new StubStatsRepo
        {
            ByIdSnapshots = { [s1] = MakeSnapshot(s1, TimeSpan.FromMinutes(15), totalIsk: 50_000.0) },
        };
        var queries = NewQueries(db, statsRepo);

        var result = await queries.GetAllSessionSummariesAsync();

        result.Sessions.Single().ActiveDuration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task GetAllSessionSummariesAsync_pilot_count_matches_session_pilot_table()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Big Fleet");
        await InsertSessionPilotAsync(db, s1, 1L, "P1");
        await InsertSessionPilotAsync(db, s1, 2L, "P2");
        await InsertSessionPilotAsync(db, s1, 3L, "P3");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(10));
        var queries = NewQueries(db, new StubStatsRepo
        {
            ByIdSnapshots = { [s1] = MakeSnapshot(s1, TimeSpan.FromMinutes(10), 0.0) },
        });

        var result = await queries.GetAllSessionSummariesAsync();

        result.Sessions.Single().PilotCount.Should().Be(3);
    }

    [Fact]
    public async Task GetAllSessionSummariesAsync_total_isk_matches_GetSessionStatsAsync_TotalIskMined()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "ISK Session");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(5));
        var statsRepo = new StubStatsRepo
        {
            ByIdSnapshots = { [s1] = MakeSnapshot(s1, TimeSpan.FromMinutes(5), totalIsk: 1_234_567.89) },
        };
        var queries = NewQueries(db, statsRepo);

        var result = await queries.GetAllSessionSummariesAsync();

        result.Sessions.Single().TotalIskMined.Should().Be(1_234_567.89);
    }

    [Fact]
    public async Task GetAllSessionSummariesAsync_lifetime_sums_time_isk_and_refined_across_sessions()
    {
        // Plan 46: the lifetime rollup sums each session's active duration, total
        // ISK, and per-material refined output (qty + ISK), sorted by qty desc.
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "First Run");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(30));
        var s2 = await InsertSessionAsync(db, "Second Run");
        await InsertSessionTimeAsync(db, s2, T0.AddDays(1), T0.AddDays(1).AddMinutes(45));

        // MakeSnapshot does not set RefinedOutput (init-only), so attach it via
        // a record `with` expression per session.
        var statsRepo = new StubStatsRepo
        {
            ByIdSnapshots =
            {
                [s1] = MakeSnapshot(s1, TimeSpan.FromMinutes(30), totalIsk: 100_000.0) with
                {
                    RefinedOutput = new[]
                    {
                        new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 1000L) { Isk = 5000.0 },
                        new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",    500L) { Isk = 2000.0 },
                    },
                },
                [s2] = MakeSnapshot(s2, TimeSpan.FromMinutes(45), totalIsk: 250_000.0) with
                {
                    RefinedOutput = new[]
                    {
                        new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 200L) { Isk = 1000.0 },
                        new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   800L) { Isk = 8000.0 },
                    },
                },
            },
        };
        var queries = NewQueries(db, statsRepo);

        var result = await queries.GetAllSessionSummariesAsync();

        result.Lifetime.TotalTime.Should().Be(TimeSpan.FromMinutes(75));
        result.Lifetime.TotalIsk.Should().Be(350_000.0);
        // Tritanium: 1200 qty / 6000 ISK; Pyerite: 1300 qty / 10000 ISK. Sorted by qty desc.
        result.Lifetime.Refined.Should().HaveCount(2);
        result.Lifetime.Refined[0].Name.Should().Be("Pyerite");
        result.Lifetime.Refined[0].Quantity.Should().Be(1300L);
        result.Lifetime.Refined[0].Isk.Should().BeApproximately(10_000.0, 1e-6);
        result.Lifetime.Refined[1].Name.Should().Be("Tritanium");
        result.Lifetime.Refined[1].Quantity.Should().Be(1200L);
        result.Lifetime.Refined[1].Isk.Should().BeApproximately(6_000.0, 1e-6);
    }

    // ---- GetPilotsForSessionAsync -------------------------------------------------

    [Fact]
    public async Task GetPilotsForSessionAsync_returns_pilots_alphabetical()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Mixed");
        await InsertSessionPilotAsync(db, s1, 1L, "Charlie");
        await InsertSessionPilotAsync(db, s1, 2L, "alice");
        await InsertSessionPilotAsync(db, s1, 3L, "Bob");
        var queries = NewQueries(db);

        var result = await queries.GetPilotsForSessionAsync(s1);

        result.Select(r => r.PilotName).Should().ContainInOrder("alice", "Bob", "Charlie");
    }

    [Fact]
    public async Task GetPilotsForSessionAsync_returns_empty_for_unknown_session()
    {
        await using var db = await NewSchemaAsync();
        var queries = NewQueries(db);

        var result = await queries.GetPilotsForSessionAsync("sess-unknown");

        result.Should().BeEmpty();
    }

    // ---- GetOreBreakdownAsync -----------------------------------------------------

    [Fact]
    public async Task GetOreBreakdownAsync_session_level_aggregates_across_all_pilots()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Mining");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionPilotAsync(db, s1, Pilot2, "Bob");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(20));
        // Two pilots both mine Veldspar.
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(5),  "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        await InsertLogAsync(db, s1, Pilot2, T0.AddSeconds(10), "Tick", VeldsparTypeId, units: 200, volumeM3: 20.0);
        var sde = new StubSdeLookup
        {
            Products = { [VeldsparTypeId] = new MiningProduct(VeldsparTypeId, "Veldspar", 0.1, 100) },
        };
        var queries = NewQueries(db, sdeLookup: sde);

        var result = await queries.GetOreBreakdownAsync(s1, pilotId: null);

        result.Should().HaveCount(1);
        result[0].TypeId.Should().Be(VeldsparTypeId);
        result[0].VolumeM3.Should().Be(30.0);
        result[0].TypeName.Should().Be("Veldspar");
    }

    [Fact]
    public async Task GetOreBreakdownAsync_per_pilot_filters_by_pilot_id()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Mining");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionPilotAsync(db, s1, Pilot2, "Bob");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(20));
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(5),  "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        await InsertLogAsync(db, s1, Pilot2, T0.AddSeconds(10), "Tick", ScorditeTypeId, units: 200, volumeM3: 30.0);
        var sde = new StubSdeLookup
        {
            Products =
            {
                [VeldsparTypeId] = new MiningProduct(VeldsparTypeId, "Veldspar", 0.1, 100),
                [ScorditeTypeId] = new MiningProduct(ScorditeTypeId, "Scordite", 0.15, 100),
            },
        };
        var queries = NewQueries(db, sdeLookup: sde);

        var result = await queries.GetOreBreakdownAsync(s1, pilotId: Pilot1);

        result.Select(r => r.TypeId).Should().BeEquivalentTo(new long[] { VeldsparTypeId });
        result.Single().VolumeM3.Should().Be(10.0);
    }

    [Fact]
    public async Task GetOreBreakdownAsync_excludes_residue_rows_with_null_type_id()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Residue");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(5));
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(5), "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        // Residue row carries NULL type_id and NULL volume_m3 by contract.
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(6), "Residue", null, units: 5, volumeM3: null);
        var sde = new StubSdeLookup
        {
            Products = { [VeldsparTypeId] = new MiningProduct(VeldsparTypeId, "Veldspar", 0.1, 100) },
        };
        var queries = NewQueries(db, sdeLookup: sde);

        var result = await queries.GetOreBreakdownAsync(s1, pilotId: null);

        result.Should().HaveCount(1).And.OnlyContain(r => r.TypeId == VeldsparTypeId);
    }

    [Fact]
    public async Task GetOreBreakdownAsync_orders_by_volume_m3_descending()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Multi");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(10));
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(1), "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(2), "Tick", ScorditeTypeId, units: 200, volumeM3: 50.0);
        var sde = new StubSdeLookup
        {
            Products =
            {
                [VeldsparTypeId] = new MiningProduct(VeldsparTypeId, "Veldspar", 0.1, 100),
                [ScorditeTypeId] = new MiningProduct(ScorditeTypeId, "Scordite", 0.15, 100),
            },
        };
        var queries = NewQueries(db, sdeLookup: sde);

        var result = await queries.GetOreBreakdownAsync(s1, pilotId: null);

        result.Select(r => r.TypeId).Should().ContainInOrder(ScorditeTypeId, VeldsparTypeId);
    }

    [Fact]
    public async Task GetOreBreakdownAsync_returns_empty_list_for_unknown_session_id()
    {
        await using var db = await NewSchemaAsync();
        var queries = NewQueries(db);

        var result = await queries.GetOreBreakdownAsync(sessionId: "sess-unknown", pilotId: null);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOreBreakdownAsync_typename_falls_back_to_hash_typeid_when_sde_unknown()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Unknown");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(2));
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(1), "Tick", typeId: 99_999L, units: 1, volumeM3: 1.0);
        // Empty StubSdeLookup -> TryGetProduct returns null.
        var queries = NewQueries(db, sdeLookup: new StubSdeLookup());

        var result = await queries.GetOreBreakdownAsync(s1, pilotId: null);

        result.Single().TypeName.Should().Be("#99999");
    }

    // ---- GetRefinedMatBreakdownAsync (plan 43) ----------------------------------

    private const long TritaniumTypeId = 34L;
    private const long PyeriteTypeId = 35L;

    /// <summary>SDE stub that name-resolves Tritanium (34) and Pyerite (35).</summary>
    private static StubSdeLookup RefinedNamesSde() =>
        new()
        {
            Products =
            {
                [TritaniumTypeId] = new MiningProduct(TritaniumTypeId, "Tritanium", 0.01, 1),
                [PyeriteTypeId]   = new MiningProduct(PyeriteTypeId,   "Pyerite",   0.01, 1),
            },
        };

    [Fact]
    public async Task GetRefinedMatBreakdownAsync_session_level_rows_sorted_desc_with_resolved_names()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Refined Session");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionPilotAsync(db, s1, Pilot2, "Bob");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(20));
        // Two pilots mining (so the ore-breakdown SQL the query reuses returns rows).
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(5),  "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        await InsertLogAsync(db, s1, Pilot2, T0.AddSeconds(10), "Tick", VeldsparTypeId, units: 200, volumeM3: 20.0);

        var calc = new StubIskCalculator
        {
            RefinedQuantities = new Dictionary<long, long> { [TritaniumTypeId] = 2000L, [PyeriteTypeId] = 800L },
        };
        var queries = NewQueries(db, sdeLookup: RefinedNamesSde(), iskCalculator: calc);

        var result = await queries.GetRefinedMatBreakdownAsync(s1, pilotId: null);

        result.Should().Equal(
            new RefineMatBreakdownRow(TritaniumTypeId, "Tritanium", 2000L),
            new RefineMatBreakdownRow(PyeriteTypeId,   "Pyerite",   800L));
    }

    [Fact]
    public async Task GetRefinedMatBreakdownAsync_per_pilot_scope_returns_resolved_sorted_rows()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "Refined Per-Pilot");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionPilotAsync(db, s1, Pilot2, "Bob");
        await InsertSessionTimeAsync(db, s1, T0, T0.AddMinutes(20));
        await InsertLogAsync(db, s1, Pilot1, T0.AddSeconds(5),  "Tick", VeldsparTypeId, units: 100, volumeM3: 10.0);
        await InsertLogAsync(db, s1, Pilot2, T0.AddSeconds(10), "Tick", ScorditeTypeId, units: 200, volumeM3: 20.0);

        var calc = new StubIskCalculator
        {
            // Pyerite > Tritanium here, so the desc sort flips the row order.
            RefinedQuantities = new Dictionary<long, long> { [TritaniumTypeId] = 300L, [PyeriteTypeId] = 900L },
        };
        var queries = NewQueries(db, sdeLookup: RefinedNamesSde(), iskCalculator: calc);

        var result = await queries.GetRefinedMatBreakdownAsync(s1, pilotId: Pilot1);

        result.Should().Equal(
            new RefineMatBreakdownRow(PyeriteTypeId,   "Pyerite",   900L),
            new RefineMatBreakdownRow(TritaniumTypeId, "Tritanium", 300L));
    }

    // ---- GetSessionTimelineAsync ------------------------------------------------

    [Fact]
    public async Task GetSessionTimelineAsync_returns_earliest_active_interval_as_session_start()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "S1");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0,                 T0.AddMinutes(10));
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(15),  T0.AddMinutes(25));
        var queries = NewQueries(db);

        var result = await queries.GetSessionTimelineAsync(s1);

        result.SessionStart.Should().Be(T0);
    }

    [Fact]
    public async Task GetSessionTimelineAsync_sums_active_intervals_into_active_duration()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "S1");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        // Two 10-minute active intervals separated by a 5-minute pause.
        await InsertSessionTimeAsync(db, s1, T0,                 T0.AddMinutes(10));
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(10),  T0.AddMinutes(15), isPause: true);
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(15),  T0.AddMinutes(25));
        var queries = NewQueries(db);

        var result = await queries.GetSessionTimelineAsync(s1);

        result.ActiveDuration.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task GetSessionTimelineAsync_returns_pause_windows_ordered_by_start_with_both_timestamps()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "S1");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0,                 T0.AddMinutes(10));
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(10),  T0.AddMinutes(12), isPause: true);
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(12),  T0.AddMinutes(20));
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(20),  T0.AddMinutes(23), isPause: true);
        var queries = NewQueries(db);

        var result = await queries.GetSessionTimelineAsync(s1);

        result.PauseWindows.Should().HaveCount(2);
        result.PauseWindows[0].Start.Should().Be(T0.AddMinutes(10));
        result.PauseWindows[0].Stop.Should().Be(T0.AddMinutes(12));
        result.PauseWindows[1].Start.Should().Be(T0.AddMinutes(20));
        result.PauseWindows[1].Stop.Should().Be(T0.AddMinutes(23));
    }

    [Fact]
    public async Task GetSessionTimelineAsync_omits_open_pause_rows()
    {
        await using var db = await NewSchemaAsync();
        var s1 = await InsertSessionAsync(db, "S1");
        await InsertSessionPilotAsync(db, s1, Pilot1, "Alice");
        await InsertSessionTimeAsync(db, s1, T0,                 T0.AddMinutes(10));
        // Open pause — saved sessions don't have these but we test defensive behaviour.
        await InsertSessionTimeAsync(db, s1, T0.AddMinutes(10),  stop: null, isPause: true);
        var queries = NewQueries(db);

        var result = await queries.GetSessionTimelineAsync(s1);

        result.PauseWindows.Should().BeEmpty();
    }

    // ---- Stubs --------------------------------------------------------------------

    private static SessionStats MakeSnapshot(string sessionId, TimeSpan totalTime, double totalIsk) =>
        new(
            SessionId:          sessionId,
            TotalTimeMined:     totalTime,
            LastCalculated:     T0,
            TotalM3Mined:       0,
            M3PerSecond:        0,
            TotalIskMined:      totalIsk,
            IskPerHour:         0,
            TotalIskFromCrits:  0,
            CritIskPercent:     0,
            TotalM3FromCrits:   0,
            CritCount:          0,
            CritChance:         0,
            ByCharacter:        new Dictionary<long, CharacterStats>());

    private sealed class StubStatsRepo : ISessionStatsRepository
    {
        public Dictionary<string, SessionStats> ByIdSnapshots { get; } = new();

        public Task<SessionStats> GetSessionStatsAsync(string sessionId, DateTimeOffset asOf, bool allowLazyFetch, CancellationToken ct = default)
        {
            return Task.FromResult(
                ByIdSnapshots.TryGetValue(sessionId, out var snap)
                    ? snap
                    : MakeSnapshot(sessionId, TimeSpan.Zero, 0.0));
        }
    }

    private sealed class StubSdeLookup : ISdeLookup
    {
        public Dictionary<long, MiningProduct> Products { get; } = new();
        public Dictionary<long, IReadOnlyList<TypeMaterial>> Materials { get; } = new();

        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) =>
            Products.TryGetValue(typeId, out var p) ? p : null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) =>
            Materials.TryGetValue(typeId, out var m) ? m : Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() =>
            Materials.Values.SelectMany(l => l.Select(t => t.MaterialTypeId)).Distinct().ToArray();
        public string? TryGetRefinedMaterialName(long typeId) =>
            Products.TryGetValue(typeId, out var p) ? p.Name : null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubIskCalculator : IIskCalculator
    {
        /// <summary>
        /// Plan 43: optional refined-quantity map (refined-material type_id -> units)
        /// returned on every <c>ComputeAsync</c>. Empty by default so the m^3 / ISK
        /// tests above are unaffected; set it to drive the refined-breakdown test.
        /// </summary>
        public IReadOnlyDictionary<long, long> RefinedQuantities { get; set; } =
            new Dictionary<long, long>();

        public Task<IskCalculationResult> ComputeAsync(
            IReadOnlyDictionary<long, long> typeIdToUnitsMined,
            long stationId,
            string sessionId,
            bool allowLazyFetch,
            double refiningYieldPercent,
            PriceMode priceMode,
            CancellationToken ct = default)
        {
            // Stub returns 0 ISK with empty IskByInputType — the SQL/aggregation
            // tests above assert m^3 / type_id / pilot scoping; ISK plumbing is
            // covered by IskCalculatorTests.
            var perInput = new Dictionary<long, double>(typeIdToUnitsMined.Count);
            foreach (var k in typeIdToUnitsMined.Keys) perInput[k] = 0.0;
            return Task.FromResult(new IskCalculationResult(0.0, 0.0, perInput)
            {
                RefinedQuantitiesByMaterial = RefinedQuantities,
            });
        }

        public Task<IskCalculationResult> ComputeAsync(
            IReadOnlyDictionary<long, long> typeIdToUnitsMined,
            IReadOnlyDictionary<long, long> critTypeIdToUnitsMined,
            long stationId,
            string sessionId,
            bool allowLazyFetch,
            double refiningYieldPercent,
            PriceMode priceMode,
            CancellationToken ct = default)
        {
            var perInput = new Dictionary<long, double>(typeIdToUnitsMined.Count);
            foreach (var k in typeIdToUnitsMined.Keys) perInput[k] = 0.0;
            return Task.FromResult(new IskCalculationResult(0.0, 0.0, perInput)
            {
                RefinedQuantitiesByMaterial = RefinedQuantities,
            });
        }
    }

    private sealed class StubSettings : ISettingsRepository
    {
        public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default) => Task.FromResult(ThemeMode.Dark);
        public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default) => Task.FromResult(PriceMode.Buy);
        public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default) => Task.FromResult(90.6);
        public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default) => Task.FromResult(60003760L);
        public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
        public Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default) => Task.FromResult<WidgetLayout?>(null);
        public Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default) => Task.FromResult<WidgetStatsConfig?>(null);
        public Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default) => Task.FromResult<WidgetStatsConfig?>(null);
        public Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
        public Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
        public Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WindowGeometry?> GetLifetimeTotalsWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
        public Task SetLifetimeTotalsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EveDashboard.Core.Sessions.PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default) => Task.FromResult(EveDashboard.Core.Sessions.PilotSortOrder.LastActiveNewest);
        public Task SetPilotSortOrderAsync(EveDashboard.Core.Sessions.PilotSortOrder order, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetShowPilotRefinedOutputAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetShowPilotRefinedOutputAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<long, bool>>(new Dictionary<long, bool>());
        public Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
        public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default) => Task.FromResult(5);
        public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetAutoPauseSoundVolumeAsync(CancellationToken ct = default) => Task.FromResult(65);
        public Task SetAutoPauseSoundVolumeAsync(int volume, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<int, double>?> GetReportsColumnWidthsAsync(string gridKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<int, double>?>(null);
        public Task SetReportsColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, StatEmphasis>?> GetReportsTextSizeAsync(string widgetTypeKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(null);
        public Task SetReportsTextSizeAsync(string widgetTypeKey, IReadOnlyDictionary<string, StatEmphasis> textSize, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;
        private void _suppress() => SettingChanged?.Invoke(this, new SettingChangedEventArgs(""));
    }
}
