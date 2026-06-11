using System.Globalization;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Queries;

public class SqliteSessionStatsRepositoryTests
{
    private const string InMemory = "Data Source=:memory:";

    private static readonly DateTimeOffset T0 =
        new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteSessionStatsRepository NewRepo(
        ISqliteDatabase db,
        ILogger<SqliteSessionStatsRepository>? log = null,
        IIskCalculator? iskCalculator = null,
        ISettingsRepository? settings = null) =>
        new(
            db,
            iskCalculator ?? new ZeroIskCalculator(),
            settings ?? new InMemorySettingsRepository(),
            log ?? NullLogger<SqliteSessionStatsRepository>.Instance);

    /// <summary>Test stub <see cref="IIskCalculator"/> that always returns 0.</summary>
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

    /// <summary>
    /// Test stub <see cref="IIskCalculator"/> with a per-pilot lookup table; computes ISK as the seeded value (no recursion).
    /// Plan 36: two-dict overload is the only path the repository now calls; the single-dict overload remains for tests that exercise it directly.
    /// </summary>
    private sealed class StubIskCalculator : IIskCalculator
    {
        public List<(IReadOnlyDictionary<long, long> Units, IReadOnlyDictionary<long, long> CritUnits, long StationId, double Yield, PriceMode Mode)> Calls { get; } = new();
        public Func<IReadOnlyDictionary<long, long>, double>? Compute { get; set; }
        public Func<IReadOnlyDictionary<long, long>, double>? ComputeCrit { get; set; }

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
            Calls.Add((typeIdToUnitsMined, critTypeIdToUnitsMined, stationId, refiningYieldPercent, priceMode));
            var isk = Compute?.Invoke(typeIdToUnitsMined) ?? 0.0;
            // Default crit-isk derivation: caller-supplied ComputeCrit, else proportional
            // share by total crit units / total units (close enough for tests that only
            // need a non-zero crit total).
            double crit;
            if (ComputeCrit is not null)
            {
                crit = ComputeCrit(critTypeIdToUnitsMined);
            }
            else
            {
                var totalUnits = typeIdToUnitsMined.Values.Sum();
                var critUnitsTotal = critTypeIdToUnitsMined.Values.Sum();
                crit = totalUnits == 0 ? 0.0 : isk * critUnitsTotal / totalUnits;
            }
            // Plan 25: split the total proportionally by units across input types so
            // tests that ignore IskByInputType keep TotalIsk semantics, and tests that
            // do read it see a deterministic per-input value.
            var perInput = new Dictionary<long, double>(typeIdToUnitsMined.Count);
            var totalUnitsForSplit = typeIdToUnitsMined.Values.Sum();
            foreach (var (typeId, units) in typeIdToUnitsMined)
            {
                perInput[typeId] = totalUnitsForSplit == 0 ? 0.0 : isk * units / totalUnitsForSplit;
            }
            return Task.FromResult(new IskCalculationResult(isk, crit, perInput));
        }
    }

    /// <summary>Minimal settings repo with deterministic defaults — Buy mode, 90.6 yield, Jita.</summary>
    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        public ThemeMode Theme = ThemeMode.Dark;
        public PriceMode PriceMode = PriceMode.Buy;
        public double RefiningYield = 90.6;
        public long SelectedStationId = 60003760L;
        public WindowGeometry? Geometry;

        public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default) => Task.FromResult(Theme);
        public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default) { Theme = value; return Task.CompletedTask; }
        public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default) => Task.FromResult(PriceMode);
        public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default) { PriceMode = value; return Task.CompletedTask; }
        public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default) => Task.FromResult(RefiningYield);
        public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default) { RefiningYield = value; return Task.CompletedTask; }
        public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default) => Task.FromResult(SelectedStationId);
        public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default) { SelectedStationId = stationId; return Task.CompletedTask; }
        public Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult(Geometry);
        public Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) { Geometry = value; return Task.CompletedTask; }
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
        public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
        public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default) => Task.FromResult(5);
        public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;
        // Suppress CS0067 — event is part of the interface but no test raises it.
        private void _suppress() => SettingChanged?.Invoke(this, new SettingChangedEventArgs(""));
    }

    /// <summary>
    /// Open an in-memory SQLite, run the session schema, insert a session row
    /// + the requested pilots into both <c>pilot</c> and <c>session_pilot</c>.
    /// Returns the opened DB and the inserted session id.
    /// </summary>
    private static async Task<(SqliteDatabase Db, string SessionId)> NewSessionAsync(params long[] pilotIds)
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        const string sessionId = "sess-stats-1";
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, 'test session', NULL)",
            new Dictionary<string, object?> { ["$sid"] = sessionId });

        foreach (var pid in pilotIds)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                new Dictionary<string, object?>
                {
                    ["$pid"]  = pid,
                    ["$name"] = $"Pilot{pid}",
                });
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
                new Dictionary<string, object?>
                {
                    ["$sid"] = sessionId,
                    ["$pid"] = pid,
                });
        }

        return (db, sessionId);
    }

    private static async Task InsertSessionTimeAsync(
        SqliteDatabase db,
        string sessionId,
        DateTimeOffset start,
        DateTimeOffset? stop,
        bool isPause)
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
        DateTimeOffset eveTimestamp,
        string eventKind,
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
                ["$ts"]    = ToIso(eveTimestamp),
                ["$kind"]  = eventKind,
                ["$type"]  = typeId,
                ["$units"] = units,
                ["$vol"]   = volumeM3,
            });
    }

    private static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // ---- Constructor / null-arg ---------------------------------------------------

    [Fact]
    public void Ctor_throws_on_null_database()
    {
        Action act = () => _ = new SqliteSessionStatsRepository(
            null!,
            new ZeroIskCalculator(),
            new InMemorySettingsRepository(),
            NullLogger<SqliteSessionStatsRepository>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Ctor_throws_on_null_iskCalculator()
    {
        await using var db = OpenInMemory();
        Action act = () => _ = new SqliteSessionStatsRepository(
            db,
            null!,
            new InMemorySettingsRepository(),
            NullLogger<SqliteSessionStatsRepository>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Ctor_throws_on_null_settings()
    {
        await using var db = OpenInMemory();
        Action act = () => _ = new SqliteSessionStatsRepository(
            db,
            new ZeroIskCalculator(),
            null!,
            NullLogger<SqliteSessionStatsRepository>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Ctor_throws_on_null_logger()
    {
        await using var db = OpenInMemory();
        Action act = () => _ = new SqliteSessionStatsRepository(
            db,
            new ZeroIskCalculator(),
            new InMemorySettingsRepository(),
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- 8.1 — session-not-found returns empty + logs Warning ---------------------

    [Fact]
    public async Task GetSessionStatsAsync_returns_empty_SessionStats_when_session_id_not_found()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var repo = NewRepo(db);
        var result = await repo.GetSessionStatsAsync("sess-missing", T0, true);

        result.SessionId.Should().Be("sess-missing");
        result.TotalTimeMined.Should().Be(TimeSpan.Zero);
        result.LastCalculated.Should().Be(T0);
        result.TotalM3Mined.Should().Be(0.0);
        result.M3PerSecond.Should().Be(0.0);
        result.TotalIskMined.Should().Be(0.0);
        result.IskPerHour.Should().Be(0.0);
        result.TotalM3FromCrits.Should().Be(0.0);
        result.CritCount.Should().Be(0);
        result.CritChance.Should().Be(0.0);
        result.ByCharacter.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessionStatsAsync_logs_Warning_when_session_id_not_found()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var log = new CapturingLogger<SqliteSessionStatsRepository>();
        var repo = NewRepo(db, log);
        await repo.GetSessionStatsAsync("sess-missing", T0, true);

        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        var entry = log.Entries.Single(e => e.Level == LogLevel.Warning);
        entry.Message.Should().Contain("sess-missing");
        entry.Message.ToLowerInvariant().Should().Contain("no active session could be found");
    }

    [Fact]
    public async Task GetSessionStatsAsync_does_not_aggregate_other_sessions_when_session_id_not_found()
    {
        // Seed a real session 1 with log rows. Then ask for session 999 which does not exist.
        // The empty result must not contain values from session 1.
        var (db, sid1) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid1, T0, T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid1, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 50, volumeM3: 500.0);
            await InsertLogAsync(db, sid1, 100, T0.AddSeconds(20), "Crit", typeId: 1, units: 80, volumeM3: 800.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync("sess-missing", T0.AddSeconds(60), true);

            result.SessionId.Should().Be("sess-missing");
            result.TotalM3Mined.Should().Be(0.0);
            result.TotalM3FromCrits.Should().Be(0.0);
            result.CritCount.Should().Be(0);
            result.ByCharacter.Should().BeEmpty();
        }
    }

    // ---- 8.2 — divide-by-zero handling --------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_M3PerSecond_is_zero_when_active_duration_is_zero()
    {
        // 1.1 = C: single open active row, asOf == start_time → julianday delta = 0.
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: null, isPause: false);
            // Even though no events would actually exist in this micro-window, seed one to
            // prove rates default to 0 even when m3 > 0.
            await InsertLogAsync(db, sid, 100, T0, "Tick", typeId: 1, units: 50, volumeM3: 500.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0, true); // asOf == start_time

            result.TotalTimeMined.TotalSeconds.Should().Be(0.0);
            result.M3PerSecond.Should().Be(0.0);
            result.IskPerHour.Should().Be(0.0);
            result.ByCharacter[100].M3PerSecond.Should().Be(0.0);
            result.ByCharacter[100].IskPerHour.Should().Be(0.0);
        }
    }

    // ---- Empty-session base case --------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_with_no_events_returns_zero_stats_for_each_pilot()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100, 200 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalM3Mined.Should().Be(0.0);
            result.CritCount.Should().Be(0);
            result.CritChance.Should().Be(0.0);
            result.ByCharacter.Should().HaveCount(2);
            result.ByCharacter[100].TotalM3Mined.Should().Be(0.0);
            result.ByCharacter[100].LastEventAt.Should().BeNull();
            result.ByCharacter[200].TotalM3Mined.Should().Be(0.0);
            result.ByCharacter[200].LastEventAt.Should().BeNull();
        }
    }

    // ---- Session-level aggregates -------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_aggregates_TotalM3Mined_from_log_volume_m3()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(20), "Tick", typeId: 1, units: 2, volumeM3: 20.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(30), "Tick", typeId: 1, units: 3, volumeM3: 30.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalM3Mined.Should().BeApproximately(60.0, 1e-9);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_aggregates_TotalM3FromCrits_only_from_Crit_kind_rows()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(1), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(2), "Tick", typeId: 1, units: 2, volumeM3: 20.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(3), "Crit", typeId: 1, units: 5, volumeM3: 50.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(4), "Crit", typeId: 1, units: 7, volumeM3: 70.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalM3FromCrits.Should().BeApproximately(120.0, 1e-9);
            result.TotalM3Mined.Should().BeApproximately(150.0, 1e-9);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_CritCount_counts_only_Crit_kind_rows()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(1), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(2), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(3), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(4), "Crit", typeId: 1, units: 5, volumeM3: 50.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(5), "Crit", typeId: 1, units: 5, volumeM3: 50.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(6), "Residue", typeId: null, units: 7, volumeM3: null);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.CritCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_CritChance_is_crit_count_over_crit_plus_tick_count()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            for (int i = 0; i < 7; i++)
            {
                await InsertLogAsync(db, sid, 100, T0.AddSeconds(i + 1), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            }
            for (int i = 0; i < 3; i++)
            {
                await InsertLogAsync(db, sid, 100, T0.AddSeconds(i + 10), "Crit", typeId: 1, units: 5, volumeM3: 50.0);
            }

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.CritChance.Should().BeApproximately(0.3, 1e-9);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_CritChance_is_zero_when_no_crits_and_no_ticks()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            // Only a Residue event - no Tick or Crit.
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(1), "Residue", typeId: null, units: 7, volumeM3: null);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.CritChance.Should().Be(0.0);
            result.CritCount.Should().Be(0);
        }
    }

    // ---- Active duration / TotalTimeMined / asOf ----------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_TotalTimeMined_excludes_pause_periods()
    {
        // Active 60s -> Pause 30s -> Active 60s (all closed). Total active = 120s.
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            var t1 = T0;
            var t2 = t1.AddSeconds(60);
            var t3 = t2.AddSeconds(30);
            var t4 = t3.AddSeconds(60);
            await InsertSessionTimeAsync(db, sid, t1, stop: t2, isPause: false);
            await InsertSessionTimeAsync(db, sid, t2, stop: t3, isPause: true);
            await InsertSessionTimeAsync(db, sid, t3, stop: t4, isPause: false);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, t4, true);

            result.TotalTimeMined.TotalSeconds.Should().BeApproximately(120.0, 1e-3);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_TotalTimeMined_uses_asOf_for_open_active_row()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: null, isPause: false);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(90), true);

            result.TotalTimeMined.TotalSeconds.Should().BeApproximately(90.0, 1e-3);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_LastCalculated_equals_asOf_argument()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);

            var repo = NewRepo(db);
            var asOf = new DateTimeOffset(2026, 5, 2, 12, 34, 56, TimeSpan.Zero);
            var result = await repo.GetSessionStatsAsync(sid, asOf, true);

            result.LastCalculated.Should().Be(asOf);
        }
    }

    // ---- Per-pilot ---------------------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_per_character_aggregates_by_pilot_id()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100, 200 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(1), "Tick", typeId: 1, units: 10, volumeM3: 100.0);
            await InsertLogAsync(db, sid, 200, T0.AddSeconds(2), "Tick", typeId: 1, units: 20, volumeM3: 200.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.ByCharacter[100].TotalM3Mined.Should().BeApproximately(100.0, 1e-9);
            result.ByCharacter[200].TotalM3Mined.Should().BeApproximately(200.0, 1e-9);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_per_character_LastEventAt_is_max_eve_timestamp_for_that_pilot()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            var t1 = T0.AddSeconds(10);
            var t2 = T0.AddSeconds(20);
            var t3 = T0.AddSeconds(30);
            await InsertLogAsync(db, sid, 100, t1, "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, t2, "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, t3, "Tick", typeId: 1, units: 1, volumeM3: 10.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.ByCharacter[100].LastEventAt.Should().Be(t3.ToUniversalTime());
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_per_character_LastEventAt_is_null_when_pilot_has_no_events()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100, 200 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 1, volumeM3: 10.0);
            // pilot 200 has no log rows.

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.ByCharacter[200].LastEventAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_per_character_M3PerSecond_uses_session_active_duration()
    {
        // Session active 60s; pilot mined 600 m3 -> 10 m3/s.
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(30), "Tick", typeId: 1, units: 60, volumeM3: 600.0);

            var repo = NewRepo(db);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            // Tolerance is 1e-3 because the denominator comes from SQLite julianday
            // arithmetic, which carries small floating-point drift (typically ~6e-7).
            result.ByCharacter[100].M3PerSecond.Should().BeApproximately(10.0, 1e-3);
        }
    }

    // ---- ISK pipeline (plan 18) ---------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_TotalIskMined_is_zero_when_iskCalculator_returns_zero()
    {
        // Graceful degradation path — calculator returns 0 (e.g. no market_data rows).
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 100, volumeM3: 1000.0);

            var repo = NewRepo(db, iskCalculator: new ZeroIskCalculator());
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalIskMined.Should().Be(0.0);
            result.ByCharacter[100].TotalIskMined.Should().Be(0.0);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_IskPerHour_is_zero_when_iskCalculator_returns_zero()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 100, volumeM3: 1000.0);

            var repo = NewRepo(db, iskCalculator: new ZeroIskCalculator());
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.IskPerHour.Should().Be(0.0);
            result.ByCharacter[100].IskPerHour.Should().Be(0.0);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_TotalIskMined_returns_real_value_from_iskCalculator()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1230, units: 100, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(20), "Crit", typeId: 1230, units: 200, volumeM3: 20.0);

            var calc = new StubIskCalculator
            {
                // 100 + 200 = 300 units of typeId 1230; assume 1.5 ISK / unit -> 450.
                Compute = units => units.Sum(kvp => kvp.Value * 1.5),
            };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalIskMined.Should().BeApproximately(450.0, 1e-6);
            result.ByCharacter[100].TotalIskMined.Should().BeApproximately(450.0, 1e-6);
            calc.Calls.Should().HaveCount(1, "one IskCalculator call per pilot");
            calc.Calls[0].Units.Should().ContainKey(1230L);
            calc.Calls[0].Units[1230L].Should().Be(300L);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_per_pilot_isk_aggregates_only_that_pilots_units()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100, 200 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(3600), isPause: false);
            // Pilot 100 mines 100u of type 34; pilot 200 mines 200u of type 35.
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 34, units: 100, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 200, T0.AddSeconds(20), "Tick", typeId: 35, units: 200, volumeM3: 20.0);

            var calc = new StubIskCalculator
            {
                // 1 ISK per unit, regardless of type.
                Compute = units => units.Sum(kvp => (double)kvp.Value),
            };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(3600), true);

            result.ByCharacter[100].TotalIskMined.Should().BeApproximately(100.0, 1e-6);
            result.ByCharacter[200].TotalIskMined.Should().BeApproximately(200.0, 1e-6);
            result.TotalIskMined.Should().BeApproximately(300.0, 1e-6,
                "session ISK is the sum of per-pilot ISK");
            calc.Calls.Should().HaveCount(2, "one IskCalculator call per pilot with mining events");
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_IskPerHour_uses_session_active_duration()
    {
        // Active 1800s = 0.5 hours. ISK = 100. -> 200 ISK/hour.
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(1800), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1, units: 100, volumeM3: 10.0);

            var calc = new StubIskCalculator { Compute = _ => 100.0 };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(1800), true);

            result.TotalIskMined.Should().BeApproximately(100.0, 1e-6);
            result.IskPerHour.Should().BeApproximately(200.0, 1e-3);
            result.ByCharacter[100].IskPerHour.Should().BeApproximately(200.0, 1e-3);
        }
    }

    // ---- Crit ISK (plan 36) ------------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_TotalIskFromCrits_is_non_zero_and_CritIskPercent_matches_when_session_has_crit_rows()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            // 100u Tick + 200u Crit of type 1230. With ComputeCrit = sum * 2.0, crit ISK = 400.
            // With Compute = sum * 2.0 over all units (300), total ISK = 600. Percent = 400/600 = 2/3.
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1230, units: 100, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(20), "Crit", typeId: 1230, units: 200, volumeM3: 20.0);

            var calc = new StubIskCalculator
            {
                Compute     = units     => units.Sum(kvp => kvp.Value * 2.0),
                ComputeCrit = critUnits => critUnits.Sum(kvp => kvp.Value * 2.0),
            };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalIskMined.Should().BeApproximately(600.0, 1e-6);
            result.TotalIskFromCrits.Should().BeApproximately(400.0, 1e-6);
            result.CritIskPercent.Should().BeApproximately(400.0 / 600.0, 1e-9);
            calc.Calls.Should().HaveCount(1, "single per-pilot ISK call covers both totals");
            calc.Calls[0].CritUnits.Should().ContainKey(1230L);
            calc.Calls[0].CritUnits[1230L].Should().Be(200L);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_TotalIskFromCrits_and_CritIskPercent_are_zero_when_no_crit_rows()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 1230, units: 100, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(20), "Tick", typeId: 1230, units: 200, volumeM3: 20.0);

            var calc = new StubIskCalculator
            {
                Compute = units => units.Sum(kvp => kvp.Value * 2.0),
            };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalIskMined.Should().BeApproximately(600.0, 1e-6);
            result.TotalIskFromCrits.Should().Be(0.0);
            result.CritIskPercent.Should().Be(0.0);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_CritIskPercent_is_zero_when_TotalIskMined_is_zero_divide_by_zero_guard()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            // ZeroIskCalculator returns 0 for both totals; the percent must not throw.
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Crit", typeId: 1230, units: 100, volumeM3: 10.0);

            var repo = NewRepo(db, iskCalculator: new ZeroIskCalculator());
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.TotalIskMined.Should().Be(0.0);
            result.TotalIskFromCrits.Should().Be(0.0);
            result.CritIskPercent.Should().Be(0.0);
        }
    }

    [Fact]
    public async Task GetSessionStatsAsync_per_pilot_TotalIskFromCrits_appears_on_CharacterStats_for_pilots_with_crit_rows()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100, 200 });
        await using (db)
        {
            await InsertSessionTimeAsync(db, sid, T0, stop: T0.AddSeconds(60), isPause: false);
            // Pilot 100: Tick 100 + Crit 50 of type 34. Pilot 200: Tick 200 of type 35 (no crits).
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(10), "Tick", typeId: 34, units: 100, volumeM3: 10.0);
            await InsertLogAsync(db, sid, 100, T0.AddSeconds(20), "Crit", typeId: 34, units: 50,  volumeM3: 5.0);
            await InsertLogAsync(db, sid, 200, T0.AddSeconds(30), "Tick", typeId: 35, units: 200, volumeM3: 20.0);

            var calc = new StubIskCalculator
            {
                Compute     = units     => units.Sum(kvp => kvp.Value),       // 1 ISK / unit
                ComputeCrit = critUnits => critUnits.Sum(kvp => kvp.Value),   // 1 ISK / unit
            };
            var repo = NewRepo(db, iskCalculator: calc);
            var result = await repo.GetSessionStatsAsync(sid, T0.AddSeconds(60), true);

            result.ByCharacter[100].TotalIskMined.Should().BeApproximately(150.0, 1e-6);
            result.ByCharacter[100].TotalIskFromCrits.Should().BeApproximately(50.0, 1e-6);
            result.ByCharacter[100].CritIskPercent.Should().BeApproximately(50.0 / 150.0, 1e-9);

            result.ByCharacter[200].TotalIskMined.Should().BeApproximately(200.0, 1e-6);
            result.ByCharacter[200].TotalIskFromCrits.Should().Be(0.0);
            result.ByCharacter[200].CritIskPercent.Should().Be(0.0);
        }
    }

    // ---- Cancellation -----------------------------------------------------------

    [Fact]
    public async Task GetSessionStatsAsync_honors_cancellation_token()
    {
        var (db, sid) = await NewSessionAsync(pilotIds: new long[] { 100 });
        await using (db)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var repo = NewRepo(db);
            Func<Task> act = async () => await repo.GetSessionStatsAsync(sid, T0, true, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
