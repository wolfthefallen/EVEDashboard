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

/// <summary>
/// Plan 35: <see cref="SqliteReportingQueries"/> applies the same role-exclusion
/// CTE as the live stats repository. Verify both <c>OreBreakdown</c> queries
/// and that <c>GetPilotsForSessionAsync</c> propagates <c>sp.role</c> into
/// <see cref="PilotSummaryRow.Role"/>.
/// </summary>
public class SqliteReportingQueriesRoleTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long Pilot1Miner   = 90_000_001L;
    private const long Pilot2Cyno    = 90_000_002L;
    private const long Pilot3Booster = 90_000_003L;
    private const long VeldsparTypeId = 1230L;
    private const long ScorditeTypeId = 1228L;

    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    private static SqliteDatabase OpenInMemory() => new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static SqliteReportingQueries NewQueries(SqliteDatabase db) =>
        new(
            db,
            new StubStatsRepo(),
            new StubSdeLookup(),
            new StubIskCalculator(),
            new StubSettings(),
            NullLogger<SqliteReportingQueries>.Instance);

    private static async Task<string> InsertSessionAsync(SqliteDatabase db, string name)
    {
        // Plan 38: content-addressed sha256 hex session id; distinct names hash distinct.
        var sessionId = EveDashboard.Core.Sessions.SessionId.Compute(name, ToIso(T0));
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sessionId, ["$n"] = name });
        return sessionId;
    }

    private static async Task InsertSessionPilotAsync(SqliteDatabase db, string sessionId, long pilotId, string name, string role)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
            new Dictionary<string, object?> { ["$pid"] = pilotId, ["$n"] = name });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $r)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sessionId, ["$pid"] = pilotId, ["$r"] = role,
            });
    }

    private static Task InsertSessionTimeAsync(SqliteDatabase db, string sessionId, DateTimeOffset start, DateTimeOffset stop) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $start, $stop, 0)",
            new Dictionary<string, object?>
            {
                ["$sid"]   = sessionId,
                ["$start"] = ToIso(start),
                ["$stop"]  = ToIso(stop),
            });

    private static Task InsertLogAsync(SqliteDatabase db, string sessionId, long pilotId, long typeId, double m3, string role, long units = 100) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES ($sid, $pid, $ts, 'Tick', $tid, $u, $v, $r)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sessionId, ["$pid"] = pilotId, ["$ts"] = ToIso(T0.AddSeconds(pilotId % 100)),
                ["$tid"] = typeId, ["$u"] = units, ["$v"] = m3, ["$r"] = role,
            });

    private static string ToIso(DateTimeOffset v) => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [Fact]
    public async Task GetPilotsForSessionAsync_returns_role_per_pilot()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "Role Sample");
        await InsertSessionPilotAsync(db, sid, Pilot1Miner,   "Alice",   "Miner");
        await InsertSessionPilotAsync(db, sid, Pilot2Cyno,    "Bob",     "Cyno");
        await InsertSessionPilotAsync(db, sid, Pilot3Booster, "Charlie", "Booster");
        var queries = NewQueries(db);

        var rows = await queries.GetPilotsForSessionAsync(sid);

        rows.Should().HaveCount(3);
        rows.Single(r => r.PilotId == Pilot1Miner).Role.Should().Be("Miner");
        rows.Single(r => r.PilotId == Pilot2Cyno).Role.Should().Be("Cyno");
        rows.Single(r => r.PilotId == Pilot3Booster).Role.Should().Be("Booster");
    }

    [Fact]
    public async Task GetOreBreakdownAsync_session_excludes_Cyno_rows()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "Mix");
        await InsertSessionPilotAsync(db, sid, Pilot1Miner, "Alice", "Miner");
        await InsertSessionPilotAsync(db, sid, Pilot2Cyno,  "Bob",   "Cyno");
        await InsertSessionTimeAsync(db, sid, T0, T0.AddMinutes(5));

        await InsertLogAsync(db, sid, Pilot1Miner, VeldsparTypeId, 100.0, "Miner");
        await InsertLogAsync(db, sid, Pilot2Cyno,  VeldsparTypeId, 100.0, "Cyno");

        var queries = NewQueries(db);
        var rows = await queries.GetOreBreakdownAsync(sid, pilotId: null);

        rows.Should().ContainSingle();
        rows[0].VolumeM3.Should().Be(100.0, "the Cyno row is filtered out by the eligibility CTE");
    }

    [Fact]
    public async Task GetOreBreakdownAsync_session_excludes_Combat_rows()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "Mix");
        await InsertSessionPilotAsync(db, sid, Pilot1Miner, "Alice", "Miner");
        await InsertSessionTimeAsync(db, sid, T0, T0.AddMinutes(5));

        await InsertLogAsync(db, sid, Pilot1Miner, VeldsparTypeId, 100.0, "Miner");
        // Same pilot but Combat-tagged row — would happen if they had been Combat earlier.
        await InsertLogAsync(db, sid, Pilot1Miner, VeldsparTypeId, 200.0, "Combat");

        var queries = NewQueries(db);
        var rows = await queries.GetOreBreakdownAsync(sid, pilotId: null);

        rows.Should().ContainSingle();
        rows[0].VolumeM3.Should().Be(100.0);
    }

    [Fact]
    public async Task GetOreBreakdownAsync_session_excludes_Booster_with_no_m3()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "BoosterGate");
        await InsertSessionPilotAsync(db, sid, Pilot1Miner,   "Alice",   "Miner");
        await InsertSessionPilotAsync(db, sid, Pilot3Booster, "Charlie", "Booster");
        await InsertSessionTimeAsync(db, sid, T0, T0.AddMinutes(5));

        await InsertLogAsync(db, sid, Pilot1Miner,   VeldsparTypeId, 100.0, "Miner");
        // Booster pilot has only Residue-shape (NULL m^3) row.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, role) VALUES ($sid, $pid, $ts, 'Residue', 'Booster')",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = Pilot3Booster, ["$ts"] = ToIso(T0) });

        var queries = NewQueries(db);
        var rows = await queries.GetOreBreakdownAsync(sid, pilotId: null);

        rows.Should().ContainSingle();
        rows[0].VolumeM3.Should().Be(100.0);
    }

    [Fact]
    public async Task GetOreBreakdownAsync_session_includes_Booster_with_positive_m3()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "BoosterFlipped");
        await InsertSessionPilotAsync(db, sid, Pilot1Miner,   "Alice",   "Miner");
        await InsertSessionPilotAsync(db, sid, Pilot3Booster, "Charlie", "Booster");
        await InsertSessionTimeAsync(db, sid, T0, T0.AddMinutes(5));

        await InsertLogAsync(db, sid, Pilot1Miner,   VeldsparTypeId, 100.0, "Miner");
        await InsertLogAsync(db, sid, Pilot3Booster, VeldsparTypeId, 75.0,  "Booster");

        var queries = NewQueries(db);
        var rows = await queries.GetOreBreakdownAsync(sid, pilotId: null);

        rows.Should().ContainSingle();
        rows[0].VolumeM3.Should().Be(175.0,
            "once any Booster row has m^3 > 0, all that pilot's Booster rows count");
    }

    [Fact]
    public async Task GetOreBreakdownAsync_per_pilot_filters_role_too()
    {
        await using var db = await NewSchemaAsync();
        var sid = await InsertSessionAsync(db, "PerPilot");
        await InsertSessionPilotAsync(db, sid, Pilot2Cyno, "Bob", "Cyno");
        await InsertSessionTimeAsync(db, sid, T0, T0.AddMinutes(5));
        await InsertLogAsync(db, sid, Pilot2Cyno, VeldsparTypeId, 500.0, "Cyno");

        var queries = NewQueries(db);
        var rows = await queries.GetOreBreakdownAsync(sid, pilotId: Pilot2Cyno);

        rows.Should().BeEmpty("Cyno pilot's per-pilot breakdown is also empty (filter applies)");
    }

    // ---- Stubs to make NewQueries happy (we only use methods that hit the real DB) ----

    private sealed class StubStatsRepo : ISessionStatsRepository
    {
        public Task<SessionStats> GetSessionStatsAsync(string sessionId, DateTimeOffset asOf, bool allowLazyFetch, CancellationToken ct = default) =>
            throw new NotImplementedException("Reporting role tests don't call the stats repo.");
    }

    private sealed class StubSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string name) => null;
        public MiningProduct? TryGetProduct(long typeId) => new(typeId, "Stub", VolumePerUnitM3: 0.1, PortionSize: 100);
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubIskCalculator : IIskCalculator
    {
        public Task<IskCalculationResult> ComputeAsync(
            IReadOnlyDictionary<long, long> typeIdToUnitsMined,
            long stationId,
            string sessionId,
            bool allowLazyFetch,
            double refiningYieldPercent,
            PriceMode priceMode,
            CancellationToken ct = default) =>
            Task.FromResult(new IskCalculationResult(0.0, 0.0, new Dictionary<long, double>()));

        public Task<IskCalculationResult> ComputeAsync(
            IReadOnlyDictionary<long, long> typeIdToUnitsMined,
            IReadOnlyDictionary<long, long> critTypeIdToUnitsMined,
            long stationId,
            string sessionId,
            bool allowLazyFetch,
            double refiningYieldPercent,
            PriceMode priceMode,
            CancellationToken ct = default) =>
            Task.FromResult(new IskCalculationResult(0.0, 0.0, new Dictionary<long, double>()));
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
        public event EventHandler<SettingChangedEventArgs>? SettingChanged { add { } remove { } }
    }
}
