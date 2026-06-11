namespace EveDashboard.Updates.Tests;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class GitHubReleaseClientTests
{
    /// <summary>
    /// Test seam <see cref="HttpMessageHandler"/> returning a fixed response (or
    /// throwing / delaying) regardless of the request, and capturing the request
    /// so header assertions are possible.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public HttpResponseMessage? Response;
        public Exception? Throw;
        public TimeSpan? Delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (Delay is { } d)
            {
                await Task.Delay(d, cancellationToken).ConfigureAwait(false);
            }
            if (Throw is not null) throw Throw;
            return Response ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        }
    }

    private static GitHubReleaseClient NewClient(
        StubHandler handler,
        ILogger<GitHubReleaseClient>? log = null,
        TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler);
        if (timeout is { } t) http.Timeout = t;
        return new GitHubReleaseClient(http, log ?? NullLogger<GitHubReleaseClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public void Ctor_throws_on_null_http_client()
    {
        Action act = () => _ = new GitHubReleaseClient(null!, NullLogger<GitHubReleaseClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_null_logger()
    {
        Action act = () => _ = new GitHubReleaseClient(new HttpClient(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetLatestReleaseAsync_parses_tag_and_url_from_live_fixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "testdata", "releases-latest.json");
        File.Exists(fixturePath).Should().BeTrue("the live-captured GitHub release fixture must be copied to test output");
        var json = await File.ReadAllTextAsync(fixturePath);

        var client = NewClient(new StubHandler { Response = JsonResponse(json) });

        var result = await client.GetLatestReleaseAsync();

        result.Should().NotBeNull();
        result!.TagName.Should().Be("v0.1.0");
        result.ReleaseUrl.Should().Be("https://github.com/wolfthefallen/EVEDashboard/releases/tag/v0.1.0");
    }

    [Fact]
    public async Task GetLatestReleaseAsync_sends_user_agent_header()
    {
        var handler = new StubHandler
        {
            Response = JsonResponse("""{"tag_name":"v9.9.9","html_url":"https://example/x"}"""),
        };
        var client = NewClient(handler);

        await client.GetLatestReleaseAsync();

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.UserAgent.Should().NotBeEmpty(
            "GitHub rejects API requests that carry no User-Agent");
    }

    [Fact]
    public async Task GetLatestReleaseAsync_returns_null_on_404()
    {
        var client = NewClient(new StubHandler { Response = JsonResponse("Not Found", HttpStatusCode.NotFound) });

        var result = await client.GetLatestReleaseAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestReleaseAsync_logs_Warning_and_returns_null_on_http_exception()
    {
        var log = new CapturingLogger<GitHubReleaseClient>();
        var client = NewClient(new StubHandler { Throw = new HttpRequestException("boom") }, log);

        var result = await client.GetLatestReleaseAsync();

        result.Should().BeNull();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is HttpRequestException);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_logs_Warning_and_returns_null_on_malformed_json()
    {
        var log = new CapturingLogger<GitHubReleaseClient>();
        var client = NewClient(new StubHandler { Response = JsonResponse("{ this is not json ") }, log);

        var result = await client.GetLatestReleaseAsync();

        result.Should().BeNull();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_logs_Warning_and_returns_null_on_missing_tag_name()
    {
        var log = new CapturingLogger<GitHubReleaseClient>();
        var client = NewClient(new StubHandler { Response = JsonResponse("""{"html_url":"https://example/x"}""") }, log);

        var result = await client.GetLatestReleaseAsync();

        result.Should().BeNull();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_returns_null_and_warns_on_timeout()
    {
        var log = new CapturingLogger<GitHubReleaseClient>();
        var client = NewClient(
            new StubHandler { Delay = TimeSpan.FromSeconds(2) },
            log,
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await client.GetLatestReleaseAsync();

        result.Should().BeNull();
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }
}
