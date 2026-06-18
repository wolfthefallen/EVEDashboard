using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Queries;

/// <summary>
/// Plan 35: aggregate-filter behavior in <see cref="SqliteSessionStatsRepository"/>.
/// Tests verify the four exclusion rules (Cyno / Combat / Booster-with-no-m^3 /
/// Miner-counted) and that the Booster m^3 gate flips correctly.
/// </summary>
public class SqliteSessionStatsRepositoryRoleTests
{
    private const string InMemory = "Data Source=:memory:";
    private const string SessionId = "sess-role-1";
    private const long PilotMinerId   = 1001;
    private const long PilotBoosterId = 1002;
    private const long PilotCynoId    = 1003;
    private const long PilotCombatId  = 1004;
    private const long VeldsparTypeId = 1230;

    private static readonly DateTimeOffset T0 = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(10);

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteSessionStatsRepository NewRepo(SqliteDatabase db) =>
        new(
            db,
            new ZeroIskCalculator(),
            new InMemorySettingsRepository(),
            new StubSdeLookup(),
            NullLogger<SqliteSessionStatsRepository>.Instance);

    /// <summary>Plan 43: SDE stub — role tests do not exercise refined output (ZeroIskCalculator returns none).</summary>
    private sealed class StubSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Seed a session with the four named pilots in roles Miner / Booster / Cyno /
    /// Combat, an active session_time window from <see cref="T0"/> to
    /// <see cref="T1"/>, and zero log rows. Tests add rows.
    /// </summary>
    private static async Task<SqliteDatabase> SeedAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test', NULL)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $start, $stop, 0)",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$start"] = ToIso(T0), ["$stop"] = ToIso(T1) });

        foreach (var (pid, role) in new (long pid, string role)[]
                 {
                     (PilotMinerId,   "Miner"),
                     (PilotBoosterId, "Booster"),
                     (PilotCynoId,    "Cyno"),
                     (PilotCombatId,  "Combat"),
                 })
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                new Dictionary<string, object?> { ["$pid"] = pid, ["$name"] = $"Pilot{pid}" });
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $role)",
                new Dictionary<string, object?>
                {
                    ["$sid"]  = SessionId,
                    ["$pid"]  = pid,
                    ["$role"] = role,
                });
        }

        return db;
    }

    private static Task InsertTickAsync(
        SqliteDatabase db,
        long pilotId,
        string role,
        double volumeM3,
        long units = 100) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES ($sid, $pid, $ts, 'Tick', $tid, $u, $v, $role)",
            new Dictionary<string, object?>
            {
                ["$sid"]  = SessionId,
                ["$pid"]  = pilotId,
                ["$ts"]   = ToIso(T0.AddSeconds(pilotId % 1000)),
                ["$tid"]  = VeldsparTypeId,
                ["$u"]    = units,
                ["$v"]    = volumeM3,
                ["$role"] = role,
            });

    [Fact]
    public async Task Cyno_pilot_events_are_excluded_from_session_aggregates()
    {
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId, "Miner", 500.0);
        await InsertTickAsync(db, PilotCynoId,  "Cyno",  500.0);

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.TotalM3Mined.Should().Be(500.0,
            "Cyno events must be excluded from the session-wide total");
    }

    [Fact]
    public async Task Combat_pilot_events_are_excluded_from_session_aggregates()
    {
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId,  "Miner",  500.0);
        await InsertTickAsync(db, PilotCombatId, "Combat", 500.0);

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.TotalM3Mined.Should().Be(500.0);
    }

    [Fact]
    public async Task Booster_with_zero_m3_is_excluded()
    {
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId,   "Miner",   500.0);
        // Booster pilot has only a Residue-shape row (NULL volume_m3) — does not trip the gate.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES ($sid, $pid, $ts, 'Residue', NULL, NULL, NULL, 'Booster')",
            new Dictionary<string, object?>
            {
                ["$sid"] = SessionId,
                ["$pid"] = PilotBoosterId,
                ["$ts"]  = ToIso(T0),
            });

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.TotalM3Mined.Should().Be(500.0,
            "Booster with no positive m^3 stays excluded");
    }

    [Fact]
    public async Task Booster_with_positive_m3_flips_to_included()
    {
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId,   "Miner",   500.0);
        await InsertTickAsync(db, PilotBoosterId, "Booster", 300.0);   // m^3 gate flipped.

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.TotalM3Mined.Should().Be(800.0,
            "once a Booster pilot has any m^3 > 0, all their rows count");
    }

    [Fact]
    public async Task PerPilot_byCharacter_lists_Cyno_pilot_with_zeros()
    {
        // Decision 1.2 = A: per-pilot widgets remain visible for excluded roles.
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId, "Miner", 500.0);
        await InsertTickAsync(db, PilotCynoId,  "Cyno",  500.0);

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.ByCharacter.Should().ContainKey(PilotCynoId, "Cyno pilot still surfaces in byCharacter");
        var cyno = stats.ByCharacter[PilotCynoId];
        cyno.TotalM3Mined.Should().Be(0.0, "Cyno's events are filtered out");
        cyno.CritCount.Should().Be(0);
        cyno.Role.Should().Be(PilotRole.Cyno, "Role flows through to CharacterStats");

        var miner = stats.ByCharacter[PilotMinerId];
        miner.TotalM3Mined.Should().Be(500.0);
        miner.Role.Should().Be(PilotRole.Miner);
    }

    [Fact]
    public async Task PerPilot_byCharacter_propagates_Role_for_all_four_canonical_roles()
    {
        using var db = await SeedAsync();

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.ByCharacter[PilotMinerId].Role.Should().Be(PilotRole.Miner);
        stats.ByCharacter[PilotBoosterId].Role.Should().Be(PilotRole.Booster);
        stats.ByCharacter[PilotCynoId].Role.Should().Be(PilotRole.Cyno);
        stats.ByCharacter[PilotCombatId].Role.Should().Be(PilotRole.Combat);
    }

    [Fact]
    public async Task Mixed_role_pilot_keeps_old_role_events_filtered_at_that_role()
    {
        // Event-time semantics: a pilot whose log carries Miner rows AND Cyno rows
        // (e.g. someone who toggled role mid-session) has only the Miner rows count
        // toward session totals — the Cyno rows are excluded.
        using var db = await SeedAsync();
        await InsertTickAsync(db, PilotMinerId, "Miner", 200.0, units: 50);
        await InsertTickAsync(db, PilotMinerId, "Cyno",  200.0, units: 50);

        var stats = await NewRepo(db).GetSessionStatsAsync(SessionId, T1, true);

        stats.TotalM3Mined.Should().Be(200.0,
            "only the Miner-tagged half counts; the Cyno-tagged half is filtered out at event-time role");
    }

    /// <summary>Test-only ISK calculator that always returns zero.</summary>
    private sealed class ZeroIskCalculator : IIskCalculator
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

    /// <summary>Minimal settings repo with deterministic defaults — same shape as the sibling tests.</summary>
    private sealed class InMemorySettingsRepository : ISettingsRepository
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
