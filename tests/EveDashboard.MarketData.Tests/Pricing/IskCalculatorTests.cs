namespace EveDashboard.MarketData.Tests.Pricing;

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class IskCalculatorTests
{
    private const long JitaStationId = 60003760L;
    private const long VeldsparTypeId = 1230L;
    private const long TritaniumTypeId = 34L;
    private const string SessionId = "sess-isk-calc";

    private static async Task<SqliteDatabase> SeededDbAsync()
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        await db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name) VALUES ($s, 'Jita', 'Jita IV')",
            new Dictionary<string, object?> { ["$s"] = JitaStationId });
        return db;
    }

    private static async Task SeedPriceAsync(
        SqliteDatabase db, long typeId, long stationId, double? buy, double? sell)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) VALUES ($t,$s,$sess,$f,$b,$sl)",
            new Dictionary<string, object?>
            {
                ["$t"]    = typeId,
                ["$s"]    = stationId,
                ["$sess"] = SessionId,
                ["$f"]    = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["$b"]    = (object?)buy ?? DBNull.Value,
                ["$sl"]   = (object?)sell ?? DBNull.Value,
            });
    }

    private sealed class StubSdeLookup : ISdeLookup
    {
        public Dictionary<long, IReadOnlyList<TypeMaterial>> Materials { get; } = new();
        public Dictionary<long, MiningProduct> Products { get; } = new();

        public long? TryGetTypeId(string productName) => null;
        public MiningProduct? TryGetProduct(long typeId) =>
            Products.TryGetValue(typeId, out var p) ? p : null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) =>
            Materials.TryGetValue(typeId, out var list) ? list : Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() =>
            Materials.Values.SelectMany(l => l.Select(t => t.MaterialTypeId)).Distinct().ToArray();
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingFetcher : IMarketPriceFetcher
    {
        public List<(IReadOnlyList<long> TypeIds, long StationId)> EnsureCalls { get; } = new();
        public List<string> SessionStarts { get; } = new();
        public Func<IReadOnlyList<long>, long, Task>? OnEnsure { get; set; }

        public Task FetchForSessionStartAsync(string sessionId, CancellationToken ct = default)
        {
            SessionStarts.Add(sessionId);
            return Task.CompletedTask;
        }

        public async Task EnsurePricesAsync(IReadOnlyList<long> typeIds, long stationId, string sessionId, CancellationToken ct = default)
        {
            EnsureCalls.Add((typeIds.ToArray(), stationId));
            if (OnEnsure is not null) await OnEnsure(typeIds, stationId).ConfigureAwait(false);
        }
    }

    private static IskCalculator NewCalculator(
        SqliteDatabase db,
        ISdeLookup sde,
        IMarketPriceFetcher fetcher,
        ILogger<IskCalculator>? log = null) =>
        new(fetcher, db, sde, log ?? NullLogger<IskCalculator>.Instance);

    private static StubSdeLookup VeldsparOnlySde()
    {
        var sde = new StubSdeLookup();
        sde.Products[VeldsparTypeId] = new MiningProduct(VeldsparTypeId, "Veldspar", VolumePerUnitM3: 0.1, PortionSize: 100);
        sde.Materials[VeldsparTypeId] = new[] { new TypeMaterial(TritaniumTypeId, 415L) };
        return sde;
    }

    [Fact]
    public async Task ComputeAsync_returns_zero_for_empty_input()
    {
        await using var db = await SeededDbAsync();
        var calc = NewCalculator(db, new StubSdeLookup(), new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long>(), JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        result.TotalIsk.Should().Be(0.0);
    }

    [Fact]
    public async Task ComputeAsync_throws_on_null_input()
    {
        await using var db = await SeededDbAsync();
        var calc = NewCalculator(db, new StubSdeLookup(), new RecordingFetcher());

        Func<Task> act = async () => await calc.ComputeAsync(null!, JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ComputeAsync_veldspar_worked_example_matches_canonical_algorithm()
    {
        // Canonical worked example from MEMORY.md -> project_isk_calculation:
        // 1,000 Veldspar (portion 100, 415 Tritanium per batch) at 90.63% yield, Tritanium @ 5.50:
        // batches = 10, raw Tritanium = 4150, floor(4150 * 0.9063) = 3761, * 5.50 = 20685.50.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, buy: 5.50, sell: 6.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().BeApproximately(20685.50, 1e-6);
    }

    [Fact]
    public async Task ComputeAsync_handles_leftover_below_one_batch()
    {
        // 99 Veldspar < portion size 100 -> no batches -> 0 ISK.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, 5.50, 6.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 99L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().Be(0.0);
    }

    [Fact]
    public async Task ComputeAsync_handles_gas_returns_zero()
    {
        // Gas type has no type_materials -> Step 1 emits the gas type id itself
        // as a base material -> no market_data row for the gas type id -> 0 ISK.
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup();   // no materials at all
        var fetcher = new RecordingFetcher();
        var calc = NewCalculator(db, sde, fetcher);

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [30375L] = 50L },   // Fullerite-C28
            JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        result.TotalIsk.Should().Be(0.0);
    }

    [Fact]
    public async Task ComputeAsync_handles_recursive_depth_greater_than_one()
    {
        // Synthetic: source 1000 -> intermediate 2000 (portion 1, no leftover)
        // -> base 3000. 10 source units, source portion 5, intermediate portion 1.
        // Source: 10 // 5 = 2 batches. Each batch yields 4 of intermediate (2000).
        // Intermediate raw = 8. Intermediate has no portion_size (default 1) -> 8 batches each yielding
        // 7 of base 3000 -> 56 raw base. Yield 100% (eff 1.0) -> 56. Price 1.0 -> ISK 56.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, 3000L, JitaStationId, 1.0, null);
        var sde = new StubSdeLookup();
        sde.Products[1000L] = new MiningProduct(1000L, "SourceOre", 1.0, 5);
        sde.Materials[1000L] = new[] { new TypeMaterial(2000L, 4L) };
        // Intermediate has no Products entry — portionSize defaults to 1.
        sde.Materials[2000L] = new[] { new TypeMaterial(3000L, 7L) };

        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [1000L] = 10L },
            JitaStationId, SessionId, true, 100.0, PriceMode.Buy);

        result.TotalIsk.Should().BeApproximately(56.0, 1e-6);
    }

    [Fact]
    public async Task ComputeAsync_invokes_lazy_fetch_when_market_data_is_empty()
    {
        await using var db = await SeededDbAsync();
        var sde = VeldsparOnlySde();
        var fetcher = new RecordingFetcher();
        var calc = NewCalculator(db, sde, fetcher);

        await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        fetcher.EnsureCalls.Should().HaveCount(1);
        fetcher.EnsureCalls[0].TypeIds.Should().Contain(TritaniumTypeId);
        fetcher.EnsureCalls[0].StationId.Should().Be(JitaStationId);
    }

    [Fact]
    public async Task ComputeAsync_uses_BuyPrice_when_PriceMode_is_Buy()
    {
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, buy: 5.50, sell: 100.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().BeApproximately(20685.50, 1e-6);
    }

    [Fact]
    public async Task ComputeAsync_uses_SellPrice_when_PriceMode_is_Sell()
    {
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, buy: 5.50, sell: 6.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Sell);

        // floor(4150 * 0.9063) = 3761, * 6.0 = 22566.0
        result.TotalIsk.Should().BeApproximately(22566.0, 1e-6);
    }

    [Fact]
    public async Task ComputeAsync_uses_split_when_PriceMode_is_Split()
    {
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, buy: 4.0, sell: 6.0);   // mid = 5.0
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Split);

        // floor(4150 * 0.9063) = 3761, * 5.0 = 18805.0
        result.TotalIsk.Should().BeApproximately(18805.0, 1e-6);
    }

    [Fact]
    public async Task ComputeAsync_treats_missing_price_as_zero_with_Warning_log()
    {
        await using var db = await SeededDbAsync();
        // No SeedPriceAsync for Tritanium -> lazy fetch invoked, still no row -> 0 ISK + Warning.
        var sde = VeldsparOnlySde();
        var log = new CapturingLogger<IskCalculator>();
        var fetcher = new RecordingFetcher();
        var calc = NewCalculator(db, sde, fetcher, log);

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().Be(0.0);
        log.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning &&
                 e.Message.Contains(TritaniumTypeId.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ComputeAsync_reads_price_regardless_of_station_id_argument()
    {
        // Plan 38 amendment (Bug A): the price read is STATION-BLIND. The price
        // was captured at Jita; valuing the session under a DIFFERENT selected
        // station (allowLazyFetch:false so no fetch can paper over it) must still
        // find and use the captured Jita price. station_id is fetch-target only.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, buy: 5.50, sell: 6.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        const long OtherStationId = 1049588174021L;   // not the station the price was captured at
        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            OtherStationId, SessionId, allowLazyFetch: false, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().BeApproximately(20685.50, 1e-6,
            "the read keys on session_id + type_id only; the capture station is irrelevant to reporting");
    }

    // ---- Plan 25: IskByInputType per-input breakdown ------------------------------

    [Fact]
    public async Task IskByInputType_empty_when_input_dict_empty()
    {
        await using var db = await SeededDbAsync();
        var calc = NewCalculator(db, new StubSdeLookup(), new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long>(), JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        result.IskByInputType.Should().BeEmpty();
    }

    [Fact]
    public async Task IskByInputType_keys_match_input_type_ids()
    {
        // Two distinct input types, both with materials. Per-input keys should
        // match the original input dict's keys exactly.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, 5.50, 6.0);
        await SeedPriceAsync(db, 35L, JitaStationId, 10.0, 11.0); // Pyerite for the second ore
        const long ScorditeTypeId = 1228L;
        var sde = VeldsparOnlySde();
        sde.Products[ScorditeTypeId] = new MiningProduct(ScorditeTypeId, "Scordite", 0.15, 100);
        sde.Materials[ScorditeTypeId] = new[]
        {
            new TypeMaterial(TritaniumTypeId, 346L),
            new TypeMaterial(35L, 173L),
        };
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long>
            {
                [VeldsparTypeId]  = 1000L,
                [ScorditeTypeId]  = 500L,
            },
            JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        result.IskByInputType.Keys.Should().BeEquivalentTo(new long[] { VeldsparTypeId, ScorditeTypeId });
    }

    [Fact]
    public async Task IskByInputType_sum_within_rounding_tolerance_of_TotalIsk()
    {
        // Sum-equals-total holds by construction in the new algorithm because
        // total is built as the running sum of per-input contributions.
        await using var db = await SeededDbAsync();
        await SeedPriceAsync(db, TritaniumTypeId, JitaStationId, 5.50, 6.0);
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.IskByInputType.Values.Sum().Should().BeApproximately(result.TotalIsk, 1e-6);
    }

    [Fact]
    public async Task IskByInputType_zero_value_when_no_price_for_input_materials()
    {
        // Veldspar -> Tritanium with no market_data row. Per-input ISK should
        // be 0 and the Veldspar key should still appear.
        await using var db = await SeededDbAsync();
        var sde = VeldsparOnlySde();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [VeldsparTypeId] = 1000L },
            JitaStationId, SessionId, true, 90.63, PriceMode.Buy);

        result.TotalIsk.Should().Be(0.0);
        result.IskByInputType.Should().ContainKey(VeldsparTypeId).WhoseValue.Should().Be(0.0);
    }

    [Fact]
    public async Task IskByInputType_zero_value_for_gas_input_with_no_materials()
    {
        // Gas input has no type_materials -> base material is the gas type id
        // itself with no market_data row -> 0 ISK contribution. Per-input dict
        // still carries the gas type id with value 0.
        await using var db = await SeededDbAsync();
        var sde = new StubSdeLookup();
        var calc = NewCalculator(db, sde, new RecordingFetcher());

        var result = await calc.ComputeAsync(
            new Dictionary<long, long> { [30375L] = 50L },
            JitaStationId, SessionId, true, 90.6, PriceMode.Buy);

        result.IskByInputType.Should().ContainKey(30375L).WhoseValue.Should().Be(0.0);
    }
}
