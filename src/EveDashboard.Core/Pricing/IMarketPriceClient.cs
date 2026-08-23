namespace EveDashboard.Core.Pricing;

/// <summary>
/// HTTP client over a market-price service. Pure HTTP+parse — does not write
/// to SQLite. Implementations live in <c>EveDashboard.MarketData</c>.
/// </summary>
/// <remarks>
/// Caller is responsible for de-duplication and the gaps-only fetch policy (fetch
/// only a type id with no usable price for the active mode, at most once per 15
/// minutes per session); the client just issues
/// the HTTP request and parses the response.
/// </remarks>
public interface IMarketPriceClient
{
    /// <summary>Fetch prices for a list of type ids at a single station.</summary>
    /// <param name="stationId">EVE station id (e.g. <c>60003760</c> = Jita IV - Moon 4).</param>
    /// <param name="typeIds">Type ids to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// One <see cref="MarketPrice"/> per type id the upstream service returned
    /// data for. Type ids the upstream omitted are absent from the result —
    /// deliberately no NULL-only sentinel rows.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown if <paramref name="typeIds"/> is null.
    /// </exception>
    Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
        long                stationId,
        IReadOnlyList<long> typeIds,
        CancellationToken   ct = default);
}
