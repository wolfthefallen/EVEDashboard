namespace EveDashboard.MarketData.Pricing;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IMarketPriceClient"/> backed by the public (no-auth) ESI market
/// endpoint <c>GET /v1/markets/{region_id}/orders/</c>. For each requested type
/// it fetches the region's order book, filters to the target station's
/// <c>location_id</c>, and reduces to the best buy (max) and best sell (min)
/// price on that station.
/// </summary>
/// <remarks>
/// <para>
/// Pure HTTP + parse — does NOT write to SQLite (the fetcher owns persistence).
/// Official CCP data, so it is independent of the third-party GoonMetrics
/// aggregator; it covers NPC trade-hub stations (Jita 4-4, Amarr, etc.). Player
/// structures are NOT included in the public region order book and would require
/// an authenticated scope, so they are out of scope here.
/// </para>
/// <para>
/// The target station's region is resolved via <see cref="IMarketStationCatalog"/>
/// (the seeded hubs carry <c>region_id</c>). A station with no known region logs a
/// Warning and yields no prices — the same "no usable price -&gt; ISK 0" outcome as
/// a GoonMetrics miss. Per-type failures (HTTP error, timeout, malformed JSON) log
/// a Warning and are skipped; other types still return. No retry, no backoff.
/// </para>
/// <para>
/// Uses the version-pinned route (<c>/v1/</c>) per CCP's versioning guidance;
/// a <c>Warning</c> response header (e.g. a <c>299</c> deprecation notice) is
/// logged so a future version bump is noticed rather than silently 404'ing.
/// </para>
/// </remarks>
public sealed class EsiMarketPriceClient : IMarketPriceClient
{
    /// <summary>Version-pinned ESI base (labels like <c>/latest/</c> can shift; pinned versions do not).</summary>
    private const string OrdersUrlFormat =
        "https://esi.evetech.net/v1/markets/{0}/orders/?type_id={1}&order_type=all";

    /// <summary>Descriptive User-Agent per ESI etiquette (identifies the app + a contact/repo).</summary>
    private const string UserAgent = "EVEDashboard (+https://github.com/wolfthefallen/EVEDashboard)";

    private readonly HttpClient _http;
    private readonly IMarketStationCatalog _stations;
    private readonly ILogger<EsiMarketPriceClient> _log;

    /// <summary>Construct the client.</summary>
    /// <param name="http">App-lifetime <see cref="HttpClient"/>.</param>
    /// <param name="stations">Station catalog, used to resolve a station's <c>region_id</c>.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public EsiMarketPriceClient(
        HttpClient http,
        IMarketStationCatalog stations,
        ILogger<EsiMarketPriceClient> log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
        _stations = stations;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
        long                stationId,
        IReadOnlyList<long> typeIds,
        CancellationToken   ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeIds);
        if (typeIds.Count == 0)
        {
            return Array.Empty<MarketPrice>();
        }

        var station = await _stations.GetAsync(stationId, ct).ConfigureAwait(false);
        if (station?.RegionId is not long regionId)
        {
            _log.LogWarning(
                "ESI pricing needs the station's region_id, but station {StationId} is unknown or has no region; " +
                "returning no prices (materials will value at 0).",
                stationId);
            return Array.Empty<MarketPrice>();
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var results = new List<MarketPrice>(typeIds.Count);
        foreach (var typeId in typeIds)
        {
            var price = await FetchOneAsync(regionId, stationId, typeId, fetchedAt, ct).ConfigureAwait(false);
            if (price is not null)
            {
                results.Add(price);
            }
        }
        return results;
    }

    /// <summary>
    /// Fetch and reduce one type's region order book to the best buy/sell on the
    /// target station. Returns <c>null</c> when the station has no orders for the
    /// type or the request fails (logged as a Warning; other types continue).
    /// </summary>
    private async Task<MarketPrice?> FetchOneAsync(
        long regionId, long stationId, long typeId, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        var url = string.Format(
            System.Globalization.CultureInfo.InvariantCulture, OrdersUrlFormat, regionId, typeId);

        List<EsiOrder>? orders;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            LogDeprecationWarningIfAny(response, typeId);

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            orders = await JsonSerializer.DeserializeAsync<List<EsiOrder>>(stream, JsonOpts, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "ESI orders request failed for region={RegionId} type={TypeId}; skipped.", regionId, typeId);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (not caller cancellation) — treat as a transport failure.
            _log.LogWarning(ex, "ESI orders request timed out for region={RegionId} type={TypeId}; skipped.", regionId, typeId);
            return null;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "ESI orders response was malformed JSON for region={RegionId} type={TypeId}; skipped.", regionId, typeId);
            return null;
        }

        if (orders is null || orders.Count == 0)
        {
            return null;
        }

        double? bestSell = null;
        double? bestBuy = null;
        foreach (var o in orders)
        {
            if (o.LocationId != stationId)
            {
                continue; // orders elsewhere in the region (or player structures) don't set this station's price
            }
            if (o.IsBuyOrder)
            {
                if (bestBuy is null || o.Price > bestBuy) bestBuy = o.Price;
            }
            else
            {
                if (bestSell is null || o.Price < bestSell) bestSell = o.Price;
            }
        }

        // Omit types with no order on this station at all (no NULL-only sentinel rows,
        // matching the GoonMetrics client + plan 18 decision 5.1).
        if (bestBuy is null && bestSell is null)
        {
            return null;
        }
        return new MarketPrice(typeId, stationId, bestBuy, bestSell, fetchedAt);
    }

    private void LogDeprecationWarningIfAny(HttpResponseMessage response, long typeId)
    {
        if (response.Headers.TryGetValues("Warning", out var values))
        {
            _log.LogWarning(
                "ESI returned a Warning header for the markets/orders route (type {TypeId}): {Warning}. " +
                "The pinned /v1/ version may be deprecated; check ESI versioning.",
                typeId, string.Join("; ", values));
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Minimal projection of an ESI market order — only the fields pricing needs.</summary>
    private sealed record EsiOrder(
        [property: JsonPropertyName("is_buy_order")] bool IsBuyOrder,
        [property: JsonPropertyName("location_id")] long LocationId,
        [property: JsonPropertyName("price")]       double Price);
}
