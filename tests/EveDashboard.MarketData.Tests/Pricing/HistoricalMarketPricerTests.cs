namespace EveDashboard.MarketData.Tests.Pricing;

using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class HistoricalMarketPricerTests
{
    private const long Jita = 60003760L;
    private const long Amarr = 60008494L;
    private const string NewSession = "sess-historical";
    private const string SourceSession = "sess-source";
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset M(int minutes) => T0.AddMinutes(minutes);
    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task<SqliteDatabase> SeededDbAsync()
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        foreach (var (id, sys) in new[] { (Jita, "Jita"), (Amarr, "Amarr") })
        {
            await db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name) VALUES ($sid, $sys, $name)",
                new Dictionary<string, object?> { ["$sid"] = id, ["$sys"] = sys, ["$name"] = sys + " hub" });
        }
        return db;
    }

    private static Task SeedAsync(SqliteDatabase db, long typeId, long station, DateTimeOffset at, double? buy, double? sell, string session = SourceSession) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) VALUES ($t, $s, $sess, $f, $b, $sl)",
            new Dictionary<string, object?>
            {
                ["$t"] = typeId, ["$s"] = station, ["$sess"] = session, ["$f"] = Iso(at),
                ["$b"] = (object?)buy ?? DBNull.Value, ["$sl"] = (object?)sell ?? DBNull.Value,
            });

    private static async Task<List<(long Type, long Station, string At, double? Buy, double? Sell)>> RowsOfAsync(SqliteDatabase db, string session) =>
        (await db.QueryAsync(
            "SELECT type_id, station_id, fetched_at, isk_buy, isk_sell FROM market_data WHERE session_id = $sid ORDER BY type_id, marketdata_id",
            static r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? (double?)null : r.GetDouble(3), r.IsDBNull(4) ? (double?)null : r.GetDouble(4)),
            new Dictionary<string, object?> { ["$sid"] = session })).ToList();

    private static (HistoricalMarketPricer Pricer, StubPriceClient Client) NewPricer(SqliteDatabase db, long[] types, PriceMode mode = PriceMode.Buy, Func<long, IReadOnlyList<long>, IReadOnlyList<MarketPrice>>? respond = null)
    {
        var client = new StubPriceClient { Respond = respond ?? DefaultRespond };
        var sde = new StubSdeLookup { RefinedTypeIds = types };
        var settings = new StubSettingsRepository { SelectedStationId = Jita, PriceMode = mode };
        var fetcher = new MarketPriceFetcher(client, db, sde, settings, new ActiveSessionContext(), NullLogger<MarketPriceFetcher>.Instance);
        return (new HistoricalMarketPricer(db, sde, settings, fetcher, NullLogger<HistoricalMarketPricer>.Instance), client);
    }

    private static IReadOnlyList<MarketPrice> DefaultRespond(long station, IReadOnlyList<long> ids) =>
        ids.Select(id => new MarketPrice(id, station, 50.0, 60.0, DateTimeOffset.UtcNow)).ToList();

    [Fact]
    public async Task PriceAsync_averages_each_side_per_type_over_window_rows_at_the_selected_station_only()
    {
        await using var db = await SeededDbAsync();
        await SeedAsync(db, 34, Jita, M(10), buy: 1, sell: 3);
        await SeedAsync(db, 34, Jita, M(20), buy: 3, sell: 5);
        await SeedAsync(db, 35, Jita, M(15), buy: null, sell: 7);
        await SeedAsync(db, 35, Jita, M(16), buy: 5, sell: null);
        await SeedAsync(db, 34, Jita, M(-60), buy: 100, sell: 100);   // before the window: ignored
        await SeedAsync(db, 34, Jita, M(61), buy: 100, sell: 100);    // after the window: ignored
        await SeedAsync(db, 34, Amarr, M(12), buy: 999, sell: 999);   // other hub: ignored
        var (pricer, client) = NewPricer(db, new long[] { 34, 35 });

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.AveragedFromWindow);
        result.TypesAveraged.Should().Be(2);
        result.RowsAveraged.Should().Be(4);
        result.TypesFetchedNow.Should().Be(0);
        result.TypesUnpriced.Should().Be(0);
        result.StationId.Should().Be(Jita);
        client.Calls.Should().BeEmpty("every material is covered by an averaged row, so no HTTP");

        var rows = await RowsOfAsync(db, NewSession);
        rows.Should().Equal(
            (34L, Jita, Iso(M(60)), 2.0, 4.0),
            (35L, Jita, Iso(M(60)), 5.0, 7.0));
        (await RowsOfAsync(db, SourceSession)).Should().HaveCount(7, "source rows are read, never altered or removed");
    }

    [Fact]
    public async Task PriceAsync_fetches_everything_today_when_the_window_has_no_captured_rows()
    {
        await using var db = await SeededDbAsync();
        await SeedAsync(db, 34, Jita, M(-600), buy: 1, sell: 3); // long before the window
        var (pricer, client) = NewPricer(db, new long[] { 34, 35 });

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.FetchedToday);
        result.TypesAveraged.Should().Be(0);
        result.TypesFetchedNow.Should().Be(2);
        result.TypesUnpriced.Should().Be(0);
        client.Calls.Should().ContainSingle().Which.TypeIds.Should().BeEquivalentTo(new long[] { 34, 35 });
        (await RowsOfAsync(db, NewSession)).Select(r => (r.Type, r.Buy, r.Sell)).Should().BeEquivalentTo(new[] { (34L, (double?)50.0, (double?)60.0), (35L, (double?)50.0, (double?)60.0) });
    }

    [Fact]
    public async Task PriceAsync_mixes_averaged_types_with_a_fetch_for_the_missing_ones()
    {
        await using var db = await SeededDbAsync();
        await SeedAsync(db, 34, Jita, M(10), buy: 2, sell: 4);
        var (pricer, client) = NewPricer(db, new long[] { 34, 35 });

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.Mixed);
        result.TypesAveraged.Should().Be(1);
        result.TypesFetchedNow.Should().Be(1);
        client.Calls.Should().ContainSingle().Which.TypeIds.Should().Equal(35L);
    }

    [Fact]
    public async Task PriceAsync_counts_a_type_the_fetcher_had_to_refetch_as_fetched_today()
    {
        // Sell mode, but the window only captured a buy price for type 34: the
        // averaged row lacks the side the mode needs, so the gap-fill fetches 34
        // and the calculator will use that newer row.
        await using var db = await SeededDbAsync();
        await SeedAsync(db, 34, Jita, M(10), buy: 2, sell: null);
        var (pricer, client) = NewPricer(db, new long[] { 34 }, PriceMode.Sell);

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.FetchedToday);
        result.TypesAveraged.Should().Be(0);
        result.TypesFetchedNow.Should().Be(1);
        client.Calls.Should().ContainSingle().Which.TypeIds.Should().Equal(34L);
    }

    [Fact]
    public async Task PriceAsync_reports_types_the_source_could_not_price_as_unpriced()
    {
        await using var db = await SeededDbAsync();
        var (pricer, _) = NewPricer(db, new long[] { 34, 35 }, respond: (_, _) => Array.Empty<MarketPrice>());

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.FetchedToday);
        result.TypesUnpriced.Should().Be(2);
        (await RowsOfAsync(db, NewSession)).Should().BeEmpty();
    }

    [Fact]
    public async Task PriceAsync_with_no_refined_materials_writes_nothing()
    {
        await using var db = await SeededDbAsync();
        var (pricer, client) = NewPricer(db, Array.Empty<long>());

        var result = await pricer.PriceAsync(NewSession, M(0), M(60));

        result.Basis.Should().Be(HistoricalPriceBasis.FetchedToday);
        client.Calls.Should().BeEmpty();
        (await RowsOfAsync(db, NewSession)).Should().BeEmpty();
    }

    [Fact]
    public async Task PriceAsync_rejects_a_null_session_id_and_the_ctor_rejects_nulls()
    {
        await using var db = await SeededDbAsync();
        var (pricer, _) = NewPricer(db, new long[] { 34 });

        var act = async () => await pricer.PriceAsync(null!, M(0), M(60));
        await act.Should().ThrowAsync<ArgumentNullException>();

        var ctor = () => new HistoricalMarketPricer(null!, new StubSdeLookup(), new StubSettingsRepository(), null!, NullLogger<HistoricalMarketPricer>.Instance);
        ctor.Should().Throw<ArgumentNullException>();
    }

    // ---- Batch pricing ---------------------------------------------------------

    [Fact]
    public async Task PriceBatchAsync_fetches_todays_prices_once_for_the_whole_batch()
    {
        await using var db = await SeededDbAsync();
        var (pricer, client) = NewPricer(db, new long[] { 34, 35 });
        var targets = new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(1440), M(1500)),
            new HistoricalPricingTarget("day-3", M(2880), M(2940)),
        };

        var results = await pricer.PriceBatchAsync(targets);

        client.Calls.Should().ContainSingle("three days of gaps must not mean three trips to the market")
            .Which.TypeIds.Should().BeEquivalentTo(new long[] { 34, 35 });
        results.Select(r => r.SessionId).Should().Equal(new[] { "day-1", "day-2", "day-3" });
        results.Should().OnlyContain(r => r.Basis == HistoricalPriceBasis.FetchedToday);
        results.Should().OnlyContain(r => r.TypesFetchedNow == 2);
    }

    [Fact]
    public async Task PriceBatchAsync_still_gives_every_session_its_own_rows()
    {
        await using var db = await SeededDbAsync();
        var (pricer, _) = NewPricer(db, new long[] { 34, 35 });

        await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(1440), M(1500)),
        });

        (await RowsOfAsync(db, "day-1")).Select(r => (r.Type, r.Buy, r.Sell)).Should().BeEquivalentTo(
            new[] { (34L, (double?)50.0, (double?)60.0), (35L, (double?)50.0, (double?)60.0) });
        (await RowsOfAsync(db, "day-2")).Select(r => (r.Type, r.Buy, r.Sell)).Should().BeEquivalentTo(
            new[] { (34L, (double?)50.0, (double?)60.0), (35L, (double?)50.0, (double?)60.0) });
    }

    [Fact]
    public async Task PriceBatchAsync_keeps_each_window_on_its_own_captured_prices()
    {
        await using var db = await SeededDbAsync();
        await SeedAsync(db, 34, Jita, M(10), buy: 10, sell: 20);        // inside day 1
        await SeedAsync(db, 34, Jita, M(1450), buy: 30, sell: 40);      // inside day 2
        var (pricer, client) = NewPricer(db, new long[] { 34 });

        var results = await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(1440), M(1500)),
        });

        results.Should().OnlyContain(r => r.Basis == HistoricalPriceBasis.AveragedFromWindow);
        client.Calls.Should().BeEmpty("both windows are fully covered");
        (await RowsOfAsync(db, "day-1")).Should().Equal((34L, Jita, Iso(M(60)), 10.0, 20.0));
        (await RowsOfAsync(db, "day-2")).Should().Equal((34L, Jita, Iso(M(1500)), 30.0, 40.0));
    }

    [Fact]
    public async Task PriceBatchAsync_never_averages_an_earlier_days_own_averaged_row()
    {
        await using var db = await SeededDbAsync();
        // Day 1 averages this to 10 and stamps its row at M(60) - which falls
        // inside day 2's window. Day 2 must not treat that derived row as a
        // captured price.
        await SeedAsync(db, 34, Jita, M(10), buy: 10, sell: 20);
        var (pricer, _) = NewPricer(db, new long[] { 34 });

        var results = await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(55), M(120)),
        });

        results[0].Basis.Should().Be(HistoricalPriceBasis.AveragedFromWindow);
        results[1].Basis.Should().Be(
            HistoricalPriceBasis.FetchedToday,
            "day 2's window holds no genuinely captured price, only day 1's derived one");
        (await RowsOfAsync(db, "day-2")).Select(r => (r.Type, r.Buy, r.Sell)).Should().Equal(
            (34L, (double?)50.0, (double?)60.0));
    }

    [Fact]
    public async Task PriceBatchAsync_fills_a_later_days_gap_from_the_price_already_fetched()
    {
        await using var db = await SeededDbAsync();
        // Day 1 covers type 34 from its window but needs 35; day 2 needs both.
        await SeedAsync(db, 34, Jita, M(10), buy: 10, sell: 20);
        var (pricer, client) = NewPricer(db, new long[] { 34, 35 });

        var results = await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(1440), M(1500)),
        });

        results[0].Basis.Should().Be(HistoricalPriceBasis.Mixed);
        results[1].Basis.Should().Be(HistoricalPriceBasis.FetchedToday);
        client.Calls.Should().HaveCount(2, "day 1 fetches 35, then day 2 fetches only the 34 nobody has yet");
        client.Calls[0].TypeIds.Should().BeEquivalentTo(new long[] { 35 });
        client.Calls[1].TypeIds.Should().BeEquivalentTo(new long[] { 34 });
        (await RowsOfAsync(db, "day-2")).Select(r => r.Type).Should().BeEquivalentTo(new long[] { 34, 35 });
    }

    [Fact]
    public async Task PriceBatchAsync_leaves_a_material_the_market_cannot_price_unpriced()
    {
        await using var db = await SeededDbAsync();
        var (pricer, _) = NewPricer(db, new long[] { 34, 35 }, respond: (station, ids) =>
            ids.Where(id => id == 34).Select(id => new MarketPrice(id, station, 50.0, 60.0, DateTimeOffset.UtcNow)).ToList());

        var results = await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("day-1", M(0), M(60)),
            new HistoricalPricingTarget("day-2", M(1440), M(1500)),
        });

        results.Should().OnlyContain(r => r.TypesFetchedNow == 1 && r.TypesUnpriced == 1);
        (await RowsOfAsync(db, "day-2")).Select(r => r.Type).Should().Equal(34L);
    }

    [Fact]
    public async Task PriceBatchAsync_validates_its_input()
    {
        await using var db = await SeededDbAsync();
        var (pricer, _) = NewPricer(db, new long[] { 34 });

        var nullTargets = async () => await pricer.PriceBatchAsync(null!);
        await nullTargets.Should().ThrowAsync<ArgumentNullException>();

        var duplicates = async () => await pricer.PriceBatchAsync(new[]
        {
            new HistoricalPricingTarget("same", M(0), M(60)),
            new HistoricalPricingTarget("same", M(60), M(120)),
        });
        await duplicates.Should().ThrowAsync<ArgumentException>();

        (await pricer.PriceBatchAsync(Array.Empty<HistoricalPricingTarget>())).Should().BeEmpty();
    }

    private sealed class StubPriceClient : IMarketPriceClient
    {
        public List<(long StationId, IReadOnlyList<long> TypeIds)> Calls { get; } = new();
        public Func<long, IReadOnlyList<long>, IReadOnlyList<MarketPrice>>? Respond { get; set; }

        public Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(long stationId, IReadOnlyList<long> typeIds, CancellationToken ct = default)
        {
            Calls.Add((stationId, typeIds.ToArray()));
            return Task.FromResult(Respond?.Invoke(stationId, typeIds) ?? Array.Empty<MarketPrice>());
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
}
