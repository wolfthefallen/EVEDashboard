namespace EveDashboard.MarketData.Tests.Pricing;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Pricing;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class GoonMetricsPriceClientTests
{
    private const long JitaStationId = 60003760L;

    /// <summary>
    /// Test seam <see cref="HttpMessageHandler"/> that returns a fixed response
    /// (or throws a fixed exception) regardless of the request. Captures the
    /// last URL hit so URL-format tests can assert against it.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastUri;
        public HttpResponseMessage? Response;
        public Exception? Throw;
        public TimeSpan? Delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString();
            if (Delay is { } d)
            {
                await Task.Delay(d, cancellationToken).ConfigureAwait(false);
            }
            if (Throw is not null) throw Throw;
            return Response ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<goonmetrics method=\"price_data\" version=\"1.0\"><price_data/></goonmetrics>"),
            };
        }
    }

    private static GoonMetricsPriceClient NewClient(
        StubHandler handler,
        ILogger<GoonMetricsPriceClient>? log = null,
        TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler);
        if (timeout is { } t) http.Timeout = t;
        return new GoonMetricsPriceClient(http, log ?? NullLogger<GoonMetricsPriceClient>.Instance);
    }

    private static HttpResponseMessage XmlResponse(string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/xml");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public void Ctor_throws_on_null_http_client()
    {
        Action act = () => _ = new GoonMetricsPriceClient(null!, NullLogger<GoonMetricsPriceClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_null_logger()
    {
        Action act = () => _ = new GoonMetricsPriceClient(new HttpClient(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task FetchPricesAsync_throws_on_null_typeIds()
    {
        var client = NewClient(new StubHandler());
        Func<Task> act = async () => await client.FetchPricesAsync(JitaStationId, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FetchPricesAsync_returns_empty_when_typeIds_empty()
    {
        var handler = new StubHandler();
        var client = NewClient(handler);

        var result = await client.FetchPricesAsync(JitaStationId, Array.Empty<long>());

        result.Should().BeEmpty();
        handler.LastUri.Should().BeNull("no HTTP call should be issued for an empty type-id list");
    }

    [Fact]
    public async Task FetchPricesAsync_parses_full_response_against_live_fixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "testdata", "goon-jita-response.xml");
        File.Exists(fixturePath).Should().BeTrue("the live-captured GoonMetrics fixture must be copied to test output");
        var xml = await File.ReadAllTextAsync(fixturePath);

        var handler = new StubHandler { Response = XmlResponse(xml) };
        var client = NewClient(handler);

        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34, 40 });

        result.Should().HaveCountGreaterThan(0, "the fixture has many <type> elements");
        var tritanium = result.Should().Contain(p => p.TypeId == 34L).Subject;
        tritanium.IskBuy.Should().BeApproximately(3.59, 1e-9);
        tritanium.IskSell.Should().BeApproximately(3.99, 1e-9);
        tritanium.StationId.Should().Be(JitaStationId);

        var megacyte = result.Should().Contain(p => p.TypeId == 40L).Subject;
        megacyte.IskBuy.Should().BeApproximately(2401.00, 1e-9);
    }

    [Fact]
    public async Task FetchPricesAsync_handles_missing_buy_branch()
    {
        const string Xml = """
            <goonmetrics method="price_data" version="1.0">
              <price_data>
                <type id="34">
                  <sell><min>3.99</min></sell>
                </type>
              </price_data>
            </goonmetrics>
            """;
        var client = NewClient(new StubHandler { Response = XmlResponse(Xml) });
        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().HaveCount(1);
        result[0].IskBuy.Should().BeNull();
        result[0].IskSell.Should().BeApproximately(3.99, 1e-9);
    }

    [Fact]
    public async Task FetchPricesAsync_handles_missing_sell_branch()
    {
        const string Xml = """
            <goonmetrics method="price_data" version="1.0">
              <price_data>
                <type id="34">
                  <buy><max>3.59</max></buy>
                </type>
              </price_data>
            </goonmetrics>
            """;
        var client = NewClient(new StubHandler { Response = XmlResponse(Xml) });
        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().HaveCount(1);
        result[0].IskBuy.Should().BeApproximately(3.59, 1e-9);
        result[0].IskSell.Should().BeNull();
    }

    [Fact]
    public async Task FetchPricesAsync_skips_type_with_no_max_or_min_returns_nulls()
    {
        const string Xml = """
            <goonmetrics method="price_data" version="1.0">
              <price_data>
                <type id="34">
                  <buy></buy>
                  <sell></sell>
                </type>
              </price_data>
            </goonmetrics>
            """;
        var client = NewClient(new StubHandler { Response = XmlResponse(Xml) });
        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().HaveCount(1);
        result[0].IskBuy.Should().BeNull();
        result[0].IskSell.Should().BeNull();
    }

    [Fact]
    public async Task FetchPricesAsync_logs_Warning_and_returns_empty_on_method_mismatch()
    {
        const string Xml = """
            <goonmetrics method="something_else" version="1.0">
              <price_data><type id="34"><buy><max>3.59</max></buy></type></price_data>
            </goonmetrics>
            """;
        var log = new CapturingLogger<GoonMetricsPriceClient>();
        var client = NewClient(new StubHandler { Response = XmlResponse(Xml) }, log);

        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("method"));
    }

    [Fact]
    public async Task FetchPricesAsync_logs_Warning_and_returns_empty_on_version_mismatch()
    {
        const string Xml = """
            <goonmetrics method="price_data" version="2.0">
              <price_data><type id="34"><buy><max>3.59</max></buy></type></price_data>
            </goonmetrics>
            """;
        var log = new CapturingLogger<GoonMetricsPriceClient>();
        var client = NewClient(new StubHandler { Response = XmlResponse(Xml) }, log);

        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task FetchPricesAsync_logs_Warning_and_returns_empty_on_HttpRequestException()
    {
        var log = new CapturingLogger<GoonMetricsPriceClient>();
        var client = NewClient(new StubHandler { Throw = new HttpRequestException("boom") }, log);

        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is HttpRequestException);
    }

    [Fact]
    public async Task FetchPricesAsync_logs_Warning_and_returns_empty_on_timeout()
    {
        var log = new CapturingLogger<GoonMetricsPriceClient>();
        var client = NewClient(
            new StubHandler { Delay = TimeSpan.FromSeconds(2) },
            log,
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await client.FetchPricesAsync(JitaStationId, new long[] { 34 });

        result.Should().BeEmpty();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task FetchPricesAsync_url_contains_station_id_and_csv_type_ids()
    {
        var handler = new StubHandler();
        var client = NewClient(handler);

        await client.FetchPricesAsync(JitaStationId, new long[] { 34, 35, 36 });

        handler.LastUri.Should().NotBeNull();
        handler.LastUri.Should().Contain("station_id=" + JitaStationId);
        // The CSV is URL-encoded; the comma-separated form 34,35,36 becomes "34%2C35%2C36".
        handler.LastUri.Should().MatchRegex("type_id=34(%2C|,)35(%2C|,)36");
    }
}
