namespace EveDashboard.MarketData.Tests.Pricing;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using FluentAssertions;

public class HaulValuationCalculatorTests
{
    private const long Jita = 60003760L;
    private const long Amarr = 60008494L;

    /// <summary>Price client returning caller-supplied prices per station.</summary>
    private sealed class StubClient : IMarketPriceClient
    {
        // (stationId, typeId) -> (buy, sell)
        private readonly Dictionary<(long, long), (double? Buy, double? Sell)> _prices;
        public StubClient(Dictionary<(long, long), (double? Buy, double? Sell)> prices) => _prices = prices;

        public Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
            long stationId, IReadOnlyList<long> typeIds, CancellationToken ct = default)
        {
            var list = new List<MarketPrice>();
            foreach (var t in typeIds)
            {
                if (_prices.TryGetValue((stationId, t), out var bs))
                    list.Add(new MarketPrice(t, stationId, bs.Buy, bs.Sell, System.DateTimeOffset.UnixEpoch));
            }
            return Task.FromResult<IReadOnlyList<MarketPrice>>(list);
        }
    }

    private static readonly (long, string)[] TwoHubs =
        { (Jita, "Jita - 4-4"), (Amarr, "Amarr - Emperor Family Academy") };

    [Fact]
    public void Ctor_throws_on_null_client()
    {
        Action act = () => _ = new HaulValuationCalculator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Values_each_line_and_totals_per_station_using_the_mode()
    {
        // Tritanium(34): Jita buy 5 / sell 6 ; Amarr buy 4 / sell 7. Qty 1000.
        var client = new StubClient(new()
        {
            [(Jita, 34L)]  = (5.0, 6.0),
            [(Amarr, 34L)] = (4.0, 7.0),
        });
        var sut = new HaulValuationCalculator(client);
        var haul = new[] { new HaulLine(34L, "Tritanium", 1000) };

        var buy = await sut.ValueAsync(haul, TwoHubs, PriceMode.Buy);

        // Buy mode: Jita 5*1000=5000 beats Amarr 4*1000=4000 -> Jita first.
        buy.PriceMode.Should().Be(PriceMode.Buy);
        buy.Stations.Select(s => s.StationId).Should().Equal(Jita, Amarr);
        buy.Stations[0].TotalValue.Should().Be(5000);
        buy.Stations[1].TotalValue.Should().Be(4000);
        buy.Stations[0].Lines.Single().UnitPrice.Should().Be(5.0);
        buy.Stations[0].Lines.Single().LineValue.Should().Be(5000);
    }

    [Fact]
    public async Task Sorts_stations_best_total_first_and_mode_flips_the_winner()
    {
        var client = new StubClient(new()
        {
            [(Jita, 34L)]  = (5.0, 6.0),
            [(Amarr, 34L)] = (4.0, 7.0),
        });
        var sut = new HaulValuationCalculator(client);
        var haul = new[] { new HaulLine(34L, "Tritanium", 1000) };

        // Sell mode: Amarr 7 beats Jita 6 -> Amarr first.
        var sell = await sut.ValueAsync(haul, TwoHubs, PriceMode.Sell);

        sell.Stations[0].StationId.Should().Be(Amarr);
        sell.Stations[0].TotalValue.Should().Be(7000);
    }

    [Fact]
    public async Task Split_mode_averages_when_both_sides_present()
    {
        var client = new StubClient(new() { [(Jita, 34L)] = (4.0, 6.0) });
        var sut = new HaulValuationCalculator(client);

        var split = await sut.ValueAsync(new[] { new HaulLine(34L, "Tritanium", 10) }, new[] { (Jita, "Jita") }, PriceMode.Split);

        split.Stations[0].Lines.Single().UnitPrice.Should().Be(5.0); // (4+6)/2
        split.Stations[0].TotalValue.Should().Be(50);
    }

    [Fact]
    public async Task Unpriced_line_contributes_zero_and_is_flagged()
    {
        // Pyerite(35) priced at Jita only; Tritanium(34) priced nowhere.
        var client = new StubClient(new() { [(Jita, 35L)] = (2.0, 3.0) });
        var sut = new HaulValuationCalculator(client);
        var haul = new[] { new HaulLine(34L, "Tritanium", 100), new HaulLine(35L, "Pyerite", 100) };

        var r = await sut.ValueAsync(haul, new[] { (Jita, "Jita") }, PriceMode.Buy);

        r.Stations[0].TotalValue.Should().Be(200); // only Pyerite 2*100
        r.Stations[0].UnpricedCount.Should().Be(1);
        r.Stations[0].Lines.Single(l => l.TypeId == 34L).LineValue.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_type_ids_are_collapsed_and_quantities_summed()
    {
        var client = new StubClient(new() { [(Jita, 34L)] = (5.0, 6.0) });
        var sut = new HaulValuationCalculator(client);
        var haul = new[] { new HaulLine(34L, "Tritanium", 100), new HaulLine(34L, "Tritanium", 400) };

        var r = await sut.ValueAsync(haul, new[] { (Jita, "Jita") }, PriceMode.Buy);

        r.Stations[0].Lines.Should().ContainSingle();
        r.Stations[0].Lines.Single().Quantity.Should().Be(500);
        r.Stations[0].TotalValue.Should().Be(2500);
    }

    [Fact]
    public async Task Empty_haul_yields_zero_totals_without_pricing_calls()
    {
        var client = new StubClient(new());
        var sut = new HaulValuationCalculator(client);

        var r = await sut.ValueAsync(System.Array.Empty<HaulLine>(), new[] { (Jita, "Jita") }, PriceMode.Buy);

        r.Stations.Should().ContainSingle();
        r.Stations[0].TotalValue.Should().Be(0);
        r.Stations[0].Lines.Should().BeEmpty();
    }
}
