namespace EveDashboard.Updates;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveDashboard.Core.Updates;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IReleaseClient"/> backed by the public GitHub Releases API. Issues
/// one HTTP GET to the repository's <c>releases/latest</c> endpoint and parses
/// the JSON for the release tag and its human-facing page URL.
/// </summary>
/// <remarks>
/// <para>
/// Fail-soft per <see cref="IReleaseClient"/>: any transport failure, non-success
/// status (including <c>404</c> = no published release), or malformed JSON is
/// logged and returned as <c>null</c>. No retry, no backoff.
/// </para>
/// <para>
/// GitHub rejects API requests that carry no <c>User-Agent</c>; one is set per
/// request rather than on the shared <see cref="HttpClient"/>'s default headers,
/// because other features (SDE updater, market prices) share that instance.
/// </para>
/// </remarks>
public sealed class GitHubReleaseClient : IReleaseClient
{
    /// <summary>The repository's latest-full-release endpoint (public, unauthenticated).</summary>
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/wolfthefallen/EVEDashboard/releases/latest";

    private const string UserAgent = "EVEDashboard-UpdateCheck";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<GitHubReleaseClient> _log;

    /// <summary>Construct the client.</summary>
    /// <param name="http">App-lifetime <see cref="HttpClient"/> (shared across features).</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public GitHubReleaseClient(HttpClient http, ILogger<GitHubReleaseClient> log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _log.LogInformation(
                        "GitHub reports no published release yet (404); skipping update check.");
                }
                else
                {
                    _log.LogWarning(
                        "GitHub releases/latest returned status {StatusCode}; skipping update check.",
                        (int)response.StatusCode);
                }
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer
                .DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, ct)
                .ConfigureAwait(false);

            if (dto is null ||
                string.IsNullOrWhiteSpace(dto.TagName) ||
                string.IsNullOrWhiteSpace(dto.HtmlUrl))
            {
                _log.LogWarning(
                    "GitHub releases/latest response was missing tag_name or html_url; skipping update check.");
                return null;
            }

            return new ReleaseInfo(dto.TagName, dto.HtmlUrl);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "GitHub update check HTTP request failed; skipping (no retry).");
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException with no token signal;
            // caller-driven cancellation re-throws.
            _log.LogWarning(ex, "GitHub update check timed out; skipping (no retry).");
            return null;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "GitHub update check returned malformed JSON; skipping.");
            return null;
        }
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
