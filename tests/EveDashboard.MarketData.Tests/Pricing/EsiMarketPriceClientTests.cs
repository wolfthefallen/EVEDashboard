namespace EveDashboard.MarketData.Tests.Pricing;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class EsiMarketPriceClientTests
{
    private const long JitaStationId = 60003760L;
    private const long TheForgeRegionId = 10000002L;
    private const long OtherStationId = 60000001L;

    private static MarketStation JitaStation(long? regionId = TheForgeRegionId) =>
        new(JitaStationId, "Jita", "IV - Moon 4 - CNAP", regionId, regionId is null ? null : "The Forge");

    /// <summary>Handler that maps each request to a response keyed on the URL's <c>type_id</c>.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string? LastUri;
        public int CallCount;
        public string? LastUserAgent;
        public Func<long, HttpResponseMessage> Responder = _ => Json("[]");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri?.ToString();
            LastUserAgent = request.Headers.UserAgent.ToString();
            CallCount++;
            return Task.FromResult(Responder(ExtractTypeId(request.RequestUri!)));
        }

        private static long ExtractTypeId(Uri uri)
        {
            foreach (var kv in uri.Query.TrimStart('?').Split('&'))
            {
                var parts = kv.Split('=');
                if (parts.Length == 2 && parts[0] == "type_id" && long.TryParse(parts[1], out var id)) return id;
            }
            return 0;
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static EsiMarketPriceClient NewClient(RoutingHandler handler, MarketStation? station) =>
        new(new HttpClient(handler),
            new FakeCatalog(station),
            NullLogger<EsiMarketPriceClient>.Instance);

    // An order-book for type 34 at Jita (best sell 5.0, best buy 4.2), with decoy orders at
    // another station that must be ignored (its sell 1.0 / buy 100.0 must not win).
    private const string Type34OrdersJson = """
        [
          {"is_buy_order": false, "location_id": 60003760, "price": 5.5},
          {"is_buy_order": false, "location_id": 60003760, "price": 5.0},
          {"is_buy_order": true,  "location_id": 60003760, "price": 4.0},
          {"is_buy_order": true,  "location_id": 60003760, "price": 4.2},
          {"is_buy_order": false, "location_id": 60000001, "price": 1.0},
          {"is_buy_order": true,  "location_id": 60000001, "price": 100.0}
        ]
        """;

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        Action a = () => _ = new EsiMarketPriceClient(null!, new FakeCatalog(JitaStation()), NullLogger<EsiMarketPriceClient>.Instance);
        Action b = () => _ = new EsiMarketPriceClient(new HttpClient(), null!, NullLogger<EsiMarketPriceClient>.Instance);
        Action c = () => _ = new EsiMarketPriceClient(new HttpClient(), new FakeCatalog(JitaStation()), null!);
        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Empty_type_ids_returns_empty_and_makes_no_request()
    {
        var handler = new RoutingHandler();
        var sut = NewClient(handler, JitaStation());

        var result = await sut.FetchPricesAsync(JitaStationId, Array.Empty<long>());

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Reduces_region_orders_to_best_buy_and_sell_at_the_target_station()
    {
        var handler = new RoutingHandler { Responder = _ => Json(Type34OrdersJson) };
        var sut = NewClient(handler, JitaStation());

        var result = await sut.FetchPricesAsync(JitaStationId, new[] { 34L });

        result.Should().ContainSingle();
        var p = result[0];
        p.TypeId.Should().Be(34L);
        p.StationId.Should().Be(JitaStationId);
        p.IskSell.Should().Be(5.0);   // min sell AT Jita, not the 1.0 decoy elsewhere
        p.IskBuy.Should().Be(4.2);    // max buy AT Jita, not the 100.0 decoy elsewhere
    }

    [Fact]
    public async Task Hits_versioned_region_orders_route_with_type_and_order_type_and_user_agent()
    {
        var handler = new RoutingHandler { Responder = _ => Json(Type34OrdersJson) };
        var sut = NewClient(handler, JitaStation());

        await sut.FetchPricesAsync(JitaStationId, new[] { 34L });

        handler.LastUri.Should().Be(
            $"https://esi.evetech.net/v1/markets/{TheForgeRegionId}/orders/?type_id=34&order_type=all");
        handler.LastUserAgent.Should().Contain("EVEDashboard");
    }

    [Fact]
    public async Task Type_with_no_orders_at_the_station_is_omitted()
    {
        // All orders are at some OTHER station, so Jita has no price for this type.
        const string elsewhereOnly = """
            [ {"is_buy_order": false, "location_id": 60000001, "price": 9.9} ]
            """;
        var handler = new RoutingHandler { Responder = _ => Json(elsewhereOnly) };
        var sut = NewClient(handler, JitaStation());

        var result = await sut.FetchPricesAsync(JitaStationId, new[] { 34L });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_station_returns_empty_and_makes_no_request()
    {
        var handler = new RoutingHandler();
        var sut = NewClient(handler, station: null); // catalog has no such station

        var result = await sut.FetchPricesAsync(JitaStationId, new[] { 34L });

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Station_with_null_region_returns_empty_and_makes_no_request()
    {
        var handler = new RoutingHandler();
        var sut = NewClient(handler, JitaStation(regionId: null));

        var result = await sut.FetchPricesAsync(JitaStationId, new[] { 34L });

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task One_type_failing_still_returns_the_others()
    {
        // type 34 -> ok; type 35 -> HTTP 500 (EnsureSuccessStatusCode throws, that type is skipped).
        var handler = new RoutingHandler
        {
            Responder = typeId => typeId == 35L
                ? Json("{}", HttpStatusCode.InternalServerError)
                : Json(Type34OrdersJson),
        };
        var sut = NewClient(handler, JitaStation());

        var result = await sut.FetchPricesAsync(JitaStationId, new[] { 34L, 35L });

        result.Should().ContainSingle(p => p.TypeId == 34L);
    }

    private sealed class FakeCatalog : IMarketStationCatalog
    {
        private readonly MarketStation? _station;
        public FakeCatalog(MarketStation? station) => _station = station;

        public Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default) =>
            Task.FromResult(_station is not null && _station.StationId == stationId ? _station : null);

        public Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(MarketStation station, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(MarketStation station, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(long stationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
