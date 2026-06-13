using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Queries;

/// <summary>
/// Plan 43: <see cref="SqliteSessionStatsRepository.GetSessionStatsAsync"/> populates
/// the per-pilot and session-wide <c>RefinedOutput</c> from each pilot's
/// <c>IskCalculationResult.RefinedQuantitiesByMaterial</c>, name-resolving via the
/// SDE and summing element-wise across pilots for the session total.
/// </summary>
public class SqliteSessionStatsRepositoryRefinedTests
{
    private const string InMemory = "Data Source=:memory:";
    private const string SessionId = "sess-refined-1";
    private const long Pilot1 = 100L;
    private const long Pilot2 = 200L;
    private const long Ore1TypeId = 1230L; // Veldspar-shaped ore mined by pilot 1
    private const long Ore2TypeId = 1228L; // Scordite-shaped ore mined by pilot 2
    private const long TritaniumTypeId = 34L;
    private const long PyeriteTypeId = 35L;

    private static readonly DateTimeOffset T0 = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// SDE stub resolving refined-material names from <see cref="RefinedNames"/>;
    /// everything else is inert (refined output is the only surface under test).
    /// </summary>
    private sealed class StubSdeLookup : ISdeLookup
    {
        public Dictionary<long, string> RefinedNames { get; } = new();
        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) =>
            RefinedNames.TryGetValue(typeId, out var n) ? n : null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// ISK calculator stub. The two-dict overload (the only path the repository
    /// calls) returns a <c>RefinedQuantitiesByMaterial</c> derived from the
    /// pilot's input via <see cref="ComputeRefined"/>; ISK totals stay 0.
    /// </summary>
    private sealed class StubIskCalculator : IIskCalculator
    {
        public Func<IReadOnlyDictionary<long, long>, IReadOnlyDictionary<long, long>>? ComputeRefined { get; set; }

        /// <summary>Plan 45: optional per-material ISK; when set, TotalIsk = sum of its values.</summary>
        public Func<IReadOnlyDictionary<long, long>, IReadOnlyDictionary<long, double>>? ComputeIskByMaterial { get; set; }

        public Task<IskCalculationResult> ComputeAsync(
            IReadOnlyDictionary<long, long> typeIdToUnitsMined,
            long stationId,
            string sessionId,
            bool allowLazyFetch,
            double refiningYieldPercent,
            PriceMode priceMode,
            CancellationToken ct = default)
            => ComputeAsync(
                typeIdToUnitsMined,
                critTypeIdToUnitsMined: new Dictionary<long, long>(),
                stationId, sessionId, allowLazyFetch, refiningYieldPercent, priceMode, ct);

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
            var refined = ComputeRefined?.Invoke(typeIdToUnitsMined)
                          ?? new Dictionary<long, long>();
            var iskByMaterial = ComputeIskByMaterial?.Invoke(typeIdToUnitsMined)
                                ?? new Dictionary<long, double>();
            return Task.FromResult(new IskCalculationResult(
                iskByMaterial.Values.Sum(), 0.0, new Dictionary<long, double>())
            {
                RefinedQuantitiesByMaterial = refined,
                IskByMaterial = iskByMaterial,
            });
        }
    }

    /// <summary>Minimal settings repo with deterministic defaults — Buy mode, 90.6 yield, Jita.</summary>
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
        public Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<long, bool>>(new Dictionary<long, bool>());
        public Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
        public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default) => Task.FromResult(5);
        public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler<SettingChangedEventArgs>? SettingChanged { add { } remove { } }
    }

    private static SqliteSessionStatsRepository NewRepo(
        SqliteDatabase db,
        IIskCalculator iskCalculator,
        ISdeLookup sdeLookup) =>
        new(
            db,
            iskCalculator,
            new InMemorySettingsRepository(),
            sdeLookup,
            NullLogger<SqliteSessionStatsRepository>.Instance);

    /// <summary>
    /// Seed a session with the two named pilots (both Miners), one active
    /// session_time window, and one mining Tick row each (distinct ore per pilot).
    /// </summary>
    private static async Task<SqliteDatabase> SeedTwoMinersAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test', NULL)",
            new Dictionary<string, object?> { ["$sid"] = SessionId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $start, $stop, 0)",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$start"] = ToIso(T0), ["$stop"] = ToIso(T0.AddMinutes(10)) });

        foreach (var pid in new[] { Pilot1, Pilot2 })
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                new Dictionary<string, object?> { ["$pid"] = pid, ["$name"] = $"Pilot{pid}" });
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, 'Miner')",
                new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = pid });
        }

        // Pilot 1 mines Ore1; pilot 2 mines Ore2 — distinct inputs so the stub
        // can branch deterministically on the pilot's input dict.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES ($sid, $pid, $ts, 'Tick', $tid, 1000, 100.0, 'Miner')",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = Pilot1, ["$ts"] = ToIso(T0.AddSeconds(10)), ["$tid"] = Ore1TypeId });
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES ($sid, $pid, $ts, 'Tick', $tid, 500, 50.0, 'Miner')",
            new Dictionary<string, object?> { ["$sid"] = SessionId, ["$pid"] = Pilot2, ["$ts"] = ToIso(T0.AddSeconds(20)), ["$tid"] = Ore2TypeId });

        return db;
    }

    /// <summary>SDE stub resolving Tritanium (34) and Pyerite (35) by name.</summary>
    private static StubSdeLookup TritPyeriteSde()
    {
        var sde = new StubSdeLookup();
        sde.RefinedNames[TritaniumTypeId] = "Tritanium";
        sde.RefinedNames[PyeriteTypeId]   = "Pyerite";
        return sde;
    }

    /// <summary>
    /// Refined-output stub: pilot 1's input (ore 1230) -> {34:1000, 35:500};
    /// pilot 2's input (ore 1228) -> {34:200, 35:800}. Session sum = {34:1200, 35:1300}.
    /// </summary>
    private static StubIskCalculator TwoPilotRefinedCalculator() =>
        new()
        {
            ComputeRefined = units =>
                units.ContainsKey(Ore1TypeId)
                    ? new Dictionary<long, long> { [TritaniumTypeId] = 1000L, [PyeriteTypeId] = 500L }
                    : new Dictionary<long, long> { [TritaniumTypeId] = 200L,  [PyeriteTypeId] = 800L },
        };

    [Fact]
    public async Task GetSessionStatsAsync_per_pilot_RefinedOutput_is_name_resolved_and_sorted_desc()
    {
        using var db = await SeedTwoMinersAsync();
        var repo = NewRepo(db, TwoPilotRefinedCalculator(), TritPyeriteSde());

        var stats = await repo.GetSessionStatsAsync(SessionId, T0.AddMinutes(10), true);

        // Pilot 1: {34:1000, 35:500} -> Tritanium first (1000 > 500).
        stats.ByCharacter[Pilot1].RefinedOutput.Should().Equal(
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 1000L),
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   500L));

        // Pilot 2: {34:200, 35:800} -> Pyerite first (800 > 200).
        stats.ByCharacter[Pilot2].RefinedOutput.Should().Equal(
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   800L),
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 200L));
    }

    [Fact]
    public async Task GetSessionStatsAsync_session_RefinedOutput_is_element_wise_sum_of_pilot_maps()
    {
        using var db = await SeedTwoMinersAsync();
        var repo = NewRepo(db, TwoPilotRefinedCalculator(), TritPyeriteSde());

        var stats = await repo.GetSessionStatsAsync(SessionId, T0.AddMinutes(10), true);

        // 34: 1000 + 200 = 1200; 35: 500 + 800 = 1300. Sorted desc -> Pyerite, Tritanium.
        stats.RefinedOutput.Should().Equal(
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   1300L),
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 1200L));
    }

    [Fact]
    public async Task GetSessionStatsAsync_RefinedOutput_falls_back_to_hash_typeid_when_sde_unknown()
    {
        using var db = await SeedTwoMinersAsync();
        // Empty SDE -> names unresolved -> "#<typeId>" fallback.
        var repo = NewRepo(db, TwoPilotRefinedCalculator(), new StubSdeLookup());

        var stats = await repo.GetSessionStatsAsync(SessionId, T0.AddMinutes(10), true);

        stats.ByCharacter[Pilot1].RefinedOutput.Select(r => r.Name)
            .Should().OnlyContain(n => n.StartsWith("#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSessionStatsAsync_RefinedOutput_carries_per_material_Isk_reconciling_to_TotalIskMined()
    {
        using var db = await SeedTwoMinersAsync();
        var calc = TwoPilotRefinedCalculator();
        // Pilot 1 ISK {34:5000, 35:2000} (total 7000); pilot 2 {34:1000, 35:8000} (total 9000).
        calc.ComputeIskByMaterial = units =>
            units.ContainsKey(Ore1TypeId)
                ? new Dictionary<long, double> { [TritaniumTypeId] = 5000.0, [PyeriteTypeId] = 2000.0 }
                : new Dictionary<long, double> { [TritaniumTypeId] = 1000.0, [PyeriteTypeId] = 8000.0 };
        var repo = NewRepo(db, calc, TritPyeriteSde());

        var stats = await repo.GetSessionStatsAsync(SessionId, T0.AddMinutes(10), true);

        // Session per-material ISK: 34 -> 5000+1000=6000; 35 -> 2000+8000=10000.
        stats.RefinedOutput.First(r => r.TypeId == TritaniumTypeId).Isk.Should().BeApproximately(6000.0, 1e-6);
        stats.RefinedOutput.First(r => r.TypeId == PyeriteTypeId).Isk.Should().BeApproximately(10000.0, 1e-6);
        // Per-material ISK sums to the session TotalIskMined (7000 + 9000 = 16000).
        stats.TotalIskMined.Should().BeApproximately(16000.0, 1e-6);
        stats.RefinedOutput.Sum(r => r.Isk).Should().BeApproximately(stats.TotalIskMined, 1e-6);
        // And per pilot it reconciles to that pilot's TotalIskMined.
        stats.ByCharacter[Pilot1].RefinedOutput.Sum(r => r.Isk)
            .Should().BeApproximately(stats.ByCharacter[Pilot1].TotalIskMined, 1e-6);
    }
}
