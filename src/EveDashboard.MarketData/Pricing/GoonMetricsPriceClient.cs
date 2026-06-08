namespace EveDashboard.MarketData.Pricing;

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using EveDashboard.Core.Pricing;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IMarketPriceClient"/> backed by the GoonMetrics public price API
/// at <c>https://goonmetrics.apps.gnf.lt/api/price_data/</c>. Issues one HTTP
/// GET per call and parses the XML response with <see cref="XDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pure HTTP + parse — does NOT write to SQLite. Throttling, INSERT, and the
/// active-session subscription live in <see cref="MarketPriceFetcher"/>.
/// </para>
/// <para>
/// On any transport failure (HTTP error, timeout, malformed XML, method/version
/// mismatch on the response root) returns an empty list and logs a Warning.
/// No retry, no backoff (per <c>docs/market-schema.md</c>: "If the API is
/// unreachable, the affected type_material gets ISK = 0 with a Warning log").
/// </para>
/// </remarks>
public sealed class GoonMetricsPriceClient : IMarketPriceClient
{
    private const string BaseUrl = "https://goonmetrics.apps.gnf.lt/api/price_data/";
    private const string ExpectedMethod  = "price_data";
    private const string ExpectedVersion = "1.0";

    private readonly HttpClient _http;
    private readonly ILogger<GoonMetricsPriceClient> _log;

    /// <summary>Construct the client.</summary>
    /// <param name="http">App-lifetime <see cref="HttpClient"/>.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public GoonMetricsPriceClient(HttpClient http, ILogger<GoonMetricsPriceClient> log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
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

        var typeIdCsv = string.Join(
            ",",
            typeIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var url = BaseUrl + "?station_id=" +
            stationId.ToString(CultureInfo.InvariantCulture) +
            "&type_id=" + WebUtility.UrlEncode(typeIdCsv);

        XDocument doc;
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);
            doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(
                ex,
                "GoonMetrics HTTP request failed for stationId={StationId} typeIdCount={Count}; returning empty list (no retry).",
                stationId, typeIds.Count);
            return Array.Empty<MarketPrice>();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Caller-driven cancellation re-throws; HttpClient timeout (which surfaces as
            // TaskCanceledException with no token signal) is treated as a transport failure.
            _log.LogWarning(
                ex,
                "GoonMetrics HTTP request timed out for stationId={StationId} typeIdCount={Count}; returning empty list (no retry).",
                stationId, typeIds.Count);
            return Array.Empty<MarketPrice>();
        }
        catch (XmlException ex)
        {
            _log.LogWarning(
                ex,
                "GoonMetrics returned malformed XML for stationId={StationId} typeIdCount={Count}; returning empty list.",
                stationId, typeIds.Count);
            return Array.Empty<MarketPrice>();
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "goonmetrics")
        {
            _log.LogWarning(
                "GoonMetrics response root is not <goonmetrics>; returning empty list.");
            return Array.Empty<MarketPrice>();
        }

        var method  = (string?)root.Attribute("method");
        var version = (string?)root.Attribute("version");
        if (method != ExpectedMethod || version != ExpectedVersion)
        {
            _log.LogWarning(
                "GoonMetrics response method/version mismatch (got method={Method} version={Version}, expected method={ExpectedMethod} version={ExpectedVersion}); returning empty list.",
                method, version, ExpectedMethod, ExpectedVersion);
            return Array.Empty<MarketPrice>();
        }

        var priceData = root.Element("price_data");
        if (priceData is null)
        {
            return Array.Empty<MarketPrice>();
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var results = new List<MarketPrice>();
        foreach (var typeElem in priceData.Elements("type"))
        {
            var idAttr = (string?)typeElem.Attribute("id");
            if (idAttr is null ||
                !long.TryParse(idAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeId))
            {
                _log.LogWarning("GoonMetrics <type> element has missing or unparseable id attribute; skipped.");
                continue;
            }

            var iskBuy  = ReadDecimalChild(typeElem.Element("buy"),  "max");
            var iskSell = ReadDecimalChild(typeElem.Element("sell"), "min");
            results.Add(new MarketPrice(typeId, stationId, iskBuy, iskSell, fetchedAt));
        }
        return results;
    }

    private static double? ReadDecimalChild(XElement? parent, string childName)
    {
        if (parent is null)
        {
            return null;
        }
        var raw = (string?)parent.Element(childName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
