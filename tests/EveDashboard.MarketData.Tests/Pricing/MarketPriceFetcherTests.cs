namespace EveDashboard.MarketData.Tests.Pricing;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class MarketPriceFetcherTests
{
    private const long JitaStationId = 60003760L;
    private const string SessionId = "sess-fetcher-1";

    private static async Task<SqliteDatabase> SeededDbAsync()
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        // Seed Jita into market_stations so the FK on market_data is satisfied.
        await db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name) VALUES ($sid, 'Jita', 'Jita IV-Moon-4')",
            new Dictionary<string, object?> { ["$sid"] = JitaStationId });
        return db;
    }

    private static async Task SeedMarketDataAsync(
        SqliteDatabase db,
        long           typeId,
        long           stationId,
        DateTimeOffset fetchedAt,
        double?        iskBuy = 1.0,
        double?        iskSell = 2.0,
        string         sessionId = SessionId)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) VALUES ($t, $s, $sess, $f, $b, $sl)",
            new Dictionary<string, object?>
            {
                ["$t"]    = typeId,
                ["$s"]    = stationId,
                ["$sess"] = sessionId,
                ["$f"]    = fetchedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["$b"]    = (object?)iskBuy ?? DBNull.Value,
                ["$sl"]   = (object?)iskSell ?? DBNull.Value,
            });
    }

    private static async Task<long> RowCountAsync(SqliteDatabase db) =>
        await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM market_data");

    /// <summary>Stub <see cref="IMarketPriceClient"/> with configurable response and call recording.</summary>
    private sealed class StubPriceClient : IMarketPriceClient
    {
        public List<(long StationId, IReadOnlyList<long> TypeIds)> Calls { get; } = new();
        public Func<long, IReadOnlyList<long>, IReadOnlyList<MarketPrice>>? Respond { get; set; }
        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
            long stationId, IReadOnlyList<long> typeIds, CancellationToken ct = default)
        {
            Calls.Add((stationId, typeIds.ToArray()));
            if (Throw is not null) throw Throw;
            var rows = Respond?.Invoke(stationId, typeIds) ?? Array.Empty<MarketPrice>();
            return Task.FromResult(rows);
        }
    }

    private sealed class StubSdeLookup : ISdeLookup
    {
        public IReadOnlyList<long> RefinedTypeIds { get; set; } = Array.Empty<long>();
        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => RefinedTypeIds;
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubSettingsRepository : ISettingsRepository
    {
        public long SelectedStationId { get; set; } = JitaStationId;
        public PriceMode PriceMode { get; set; } = PriceMode.Buy;
        public double RefiningYieldPercent { get; set; } = 90.6;

        public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default) => Task.FromResult(ThemeMode.Dark);
        public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default) => Task.FromResult(PriceMode);
        public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default) => Task.FromResult(RefiningYieldPercent);
        public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default) => Task.FromResult(SelectedStationId);
        public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default)
        {
            // Mirror the real SqliteSettingsRepository: persist AND raise SettingChanged
            // so the fetcher's station-change re-fetch (decision 1.4) is exercised.
            SelectedStationId = stationId;
            SettingChanged?.Invoke(this, new SettingChangedEventArgs("selected_station_id"));
            return Task.CompletedTask;
        }
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
    }

    private static MarketPriceFetcher NewFetcher(
        SqliteDatabase             db,
        IMarketPriceClient         client,
        ISdeLookup                 sde,
        ISettingsRepository        settings,
        IActiveSessionContext      activeSession,
        ILogger<MarketPriceFetcher>? log = null) =>
        new(client, db, sde, settings, activeSession, log ?? NullLogger<MarketPriceFetcher>.Instance);

    [Fact]
    public void Ctor_subscribes_to_ActiveSessionChanged_and_disposes_unsubscribes()
    {
        var activeSession = new ActiveSessionContext();
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        var fetcher = NewFetcher(db, new StubPriceClient(), new StubSdeLookup(), new StubSettingsRepository(), activeSession);

        // Direct verification: dispose then fire — the handler must not run (we observe via no SQL on db).
        fetcher.Dispose();
        Action raise = () => activeSession.Set(SessionId, SessionRunState.Running);
        raise.Should().NotThrow("Dispose must unsubscribe; otherwise the now-disposed db would throw");

        db.Dispose();
    }

    [Fact]
    public async Task FetchForSessionStartAsync_is_unconditional_even_when_ids_have_recent_rows()
    {
        // The session-start fetch is UNCONDITIONAL. Even though both ids already
        // have a recent market_data row (stamped to ANOTHER session here), the new
        // session must still request both so it captures its OWN prices from t0.
        await using var db = await SeededDbAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedMarketDataAsync(db, 34, JitaStationId, now, sessionId: "sess-other");
        await SeedMarketDataAsync(db, 35, JitaStationId, now, sessionId: "sess-other");

        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34, 35 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.FetchForSessionStartAsync(SessionId);

        client.Calls.Should().HaveCount(1, "the start fetch is unconditional");
        client.Calls[0].TypeIds.Should().BeEquivalentTo(new long[] { 34, 35 });
    }

    [Fact]
    public async Task FetchForSessionStartAsync_requests_all_ids_regardless_of_existing_rows()
    {
        // Even a mix of recent and older rows (for another session) does not narrow
        // the start request — every refined-material id is fetched.
        await using var db = await SeededDbAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedMarketDataAsync(db, 34, JitaStationId, now, sessionId: "sess-other");
        await SeedMarketDataAsync(db, 35, JitaStationId, now.AddSeconds(-120), sessionId: "sess-other");

        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34, 35 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.FetchForSessionStartAsync(SessionId);

        client.Calls.Should().HaveCount(1);
        client.Calls[0].TypeIds.Should().BeEquivalentTo(new long[] { 34, 35 }, "the start fetch is unconditional");
    }

    [Fact]
    public async Task FetchForSessionStartAsync_inserts_returned_rows_into_market_data()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34, 35, 36, 37, 38 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.FetchForSessionStartAsync(SessionId);

        (await RowCountAsync(db)).Should().Be(5);
    }

    [Fact]
    public async Task FetchForSessionStartAsync_does_not_insert_when_client_returns_empty()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34, 35 } };
        var client = new StubPriceClient { Respond = (_, _) => Array.Empty<MarketPrice>() };
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.FetchForSessionStartAsync(SessionId);

        (await RowCountAsync(db)).Should().Be(0);
    }

    [Fact]
    public async Task FetchForSessionStartAsync_uses_selected_station_id_from_settings()
    {
        await using var db = await SeededDbAsync();
        // Add a second station and select it.
        const long OtherStation = 99999999L;
        await db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name) VALUES ($s, 'Other', 'Other Station')",
            new Dictionary<string, object?> { ["$s"] = OtherStation });
        var settings = new StubSettingsRepository { SelectedStationId = OtherStation };
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var fetcher = NewFetcher(db, client, sde, settings, new ActiveSessionContext());

        await fetcher.FetchForSessionStartAsync(SessionId);

        client.Calls.Should().ContainSingle();
        client.Calls[0].StationId.Should().Be(OtherStation);
    }

    [Fact]
    public async Task EnsurePricesAsync_inserts_returned_rows()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup();
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 5.5, 6.6, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100, 101 }, JitaStationId, SessionId, PriceMode.Buy);

        (await RowCountAsync(db)).Should().Be(2);
    }

    [Fact]
    public async Task EnsurePricesAsync_skips_fetch_when_material_already_priced_for_mode()
    {
        // Covered: a usable buy price exists for this (session, type) -> never fetch.
        // A covered material is never re-fetched, with no time-based throttle.
        await using var db = await SeededDbAsync();
        await SeedMarketDataAsync(db, 100, JitaStationId, DateTimeOffset.UtcNow, iskBuy: 5.0, iskSell: 6.0);
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Buy);

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsurePricesAsync_fetches_when_price_is_zero_for_mode()
    {
        // Gap: buy price is 0 for this (session, type) -> fetch (no prior attempt).
        await using var db = await SeededDbAsync();
        await SeedMarketDataAsync(db, 100, JitaStationId, DateTimeOffset.UtcNow, iskBuy: 0.0, iskSell: 0.0);
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Buy);

        client.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsurePricesAsync_split_covered_when_only_one_side_present()
    {
        // Split is covered when EITHER side is usable: buy=5, sell=null -> no fetch.
        await using var db = await SeededDbAsync();
        await SeedMarketDataAsync(db, 100, JitaStationId, DateTimeOffset.UtcNow, iskBuy: 5.0, iskSell: null);
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Split);

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsurePricesAsync_split_gap_when_both_zero()
    {
        // Split gap requires BOTH sides 0/null: buy=0, sell=null -> fetch.
        await using var db = await SeededDbAsync();
        await SeedMarketDataAsync(db, 100, JitaStationId, DateTimeOffset.UtcNow, iskBuy: 0.0, iskSell: null);
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Split);

        client.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsurePricesAsync_sell_gap_when_only_buy_present()
    {
        // Sell mode needs a usable sell price: buy=5, sell=null -> gap -> fetch.
        await using var db = await SeededDbAsync();
        await SeedMarketDataAsync(db, 100, JitaStationId, DateTimeOffset.UtcNow, iskBuy: 5.0, iskSell: null);
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Sell);

        client.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsurePricesAsync_throttles_repeat_gap_within_15min()
    {
        // The client returns nothing, so id 100 stays an (un-rowed) gap. The second
        // call within the 15-minute window must be suppressed by the in-memory
        // attempt throttle -> exactly one HTTP attempt. This proves the
        // perpetual-zero loop is closed without a wall-clock wait.
        await using var db = await SeededDbAsync();
        var client = new StubPriceClient { Respond = (_, _) => Array.Empty<MarketPrice>() };
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Buy);
        await fetcher.EnsurePricesAsync(new long[] { 100 }, JitaStationId, SessionId, PriceMode.Buy);

        client.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsurePricesAsync_returns_immediately_when_typeIds_empty()
    {
        await using var db = await SeededDbAsync();
        var client = new StubPriceClient();
        var fetcher = NewFetcher(db, client, new StubSdeLookup(), new StubSettingsRepository(), new ActiveSessionContext());

        await fetcher.EnsurePricesAsync(Array.Empty<long>(), JitaStationId, SessionId, PriceMode.Buy);

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveSessionChanged_kicks_off_FetchForSessionStartAsync_when_session_becomes_running()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var activeSession = new ActiveSessionContext();
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), activeSession);

        activeSession.Set(SessionId, SessionRunState.Running);

        // Background fetch — poll for completion.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (client.Calls.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        client.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ActiveSessionChanged_does_not_fetch_when_state_is_not_running()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient();
        var activeSession = new ActiveSessionContext();
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), activeSession);

        activeSession.Set(SessionId, SessionRunState.Paused);

        await Task.Delay(150);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveSessionChanged_handler_logs_Warning_on_exception_and_does_not_throw()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient { Throw = new InvalidOperationException("boom") };
        var log = new CapturingLogger<MarketPriceFetcher>();
        var activeSession = new ActiveSessionContext();
        var fetcher = NewFetcher(db, client, sde, new StubSettingsRepository(), activeSession, log);

        Action raise = () => activeSession.Set(SessionId, SessionRunState.Running);
        raise.Should().NotThrow();

        // Wait for the background task to log.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!log.Entries.Any(e => e.Level == LogLevel.Warning) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task StationChange_during_running_session_refetches_all_at_new_station()
    {
        // Decision 1.4: changing selected_station_id while Running triggers a full
        // unconditional re-fetch of all refined materials at the new station.
        await using var db = await SeededDbAsync();
        const long OtherStation = 99999999L;
        await db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name) VALUES ($s, 'Other', 'Other Station')",
            new Dictionary<string, object?> { ["$s"] = OtherStation });
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34, 35 } };
        var client = new StubPriceClient
        {
            Respond = (sid, ids) => ids
                .Select(id => new MarketPrice(id, sid, 1.0, 2.0, DateTimeOffset.UtcNow))
                .ToArray(),
        };
        var settings = new StubSettingsRepository();
        var activeSession = new ActiveSessionContext();
        // Set Running BEFORE constructing the fetcher so it does NOT see the
        // ActiveSessionChanged start-prefetch — the only fetch must be the station change.
        activeSession.Set(SessionId, SessionRunState.Running);
        using var fetcher = NewFetcher(db, client, sde, settings, activeSession);

        await settings.SetSelectedStationIdAsync(OtherStation);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (client.Calls.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        client.Calls.Should().HaveCount(1);
        client.Calls[0].StationId.Should().Be(OtherStation);
        client.Calls[0].TypeIds.Should().BeEquivalentTo(new long[] { 34, 35 });
    }

    [Fact]
    public async Task StationChange_with_no_active_session_does_not_fetch()
    {
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient();
        var settings = new StubSettingsRepository();
        var activeSession = new ActiveSessionContext();   // stays None
        using var fetcher = NewFetcher(db, client, sde, settings, activeSession);

        await settings.SetSelectedStationIdAsync(12345L);

        await Task.Delay(150);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StationChange_while_paused_does_not_fetch()
    {
        // Running-only gate: a station change while Paused does not fetch (the next
        // Resume re-fetches unconditionally — decision 1.3 = B).
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient();
        var settings = new StubSettingsRepository();
        var activeSession = new ActiveSessionContext();
        activeSession.Set(SessionId, SessionRunState.Paused);
        using var fetcher = NewFetcher(db, client, sde, settings, activeSession);

        await settings.SetSelectedStationIdAsync(12345L);

        await Task.Delay(150);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_SettingChanged()
    {
        var db = await SeededDbAsync();
        var sde = new StubSdeLookup { RefinedTypeIds = new long[] { 34 } };
        var client = new StubPriceClient();
        var settings = new StubSettingsRepository();
        var activeSession = new ActiveSessionContext();
        // Set Running before construction so no start-prefetch fires on subscribe.
        activeSession.Set(SessionId, SessionRunState.Running);
        var fetcher = NewFetcher(db, client, sde, settings, activeSession);

        fetcher.Dispose();
        await settings.SetSelectedStationIdAsync(12345L);

        await Task.Delay(150);
        client.Calls.Should().BeEmpty("Dispose must unsubscribe SettingChanged; no station-change fetch should fire");
        await db.DisposeAsync();
    }
}
