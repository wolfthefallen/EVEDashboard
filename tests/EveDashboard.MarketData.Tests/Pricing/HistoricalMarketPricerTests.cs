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
