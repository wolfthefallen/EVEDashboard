namespace EveDashboard.Core.Pricing;

/// <summary>
/// Single market-price row as returned by an <see cref="IMarketPriceClient"/>.
/// </summary>
/// <param name="TypeId">EVE type id this price applies to.</param>
/// <param name="StationId">EVE station id the price was fetched at.</param>
/// <param name="IskBuy">
/// Highest buy-order ISK per unit, or <c>null</c> when the upstream service
/// omitted the buy leg (no buy orders on the book or the upstream did not
/// return a <c>&lt;buy&gt;&lt;max&gt;</c> element).
/// </param>
/// <param name="IskSell">
/// Lowest sell-order ISK per unit, or <c>null</c> when the upstream service
/// omitted the sell leg.
/// </param>
/// <param name="FetchedAt">Wall-clock timestamp the price was fetched at (UTC).</param>
/// <remarks>
/// Persisted as one row in the <c>market_data</c> table.
/// Every fetch is a new INSERT; old rows are
/// immutable history.
/// </remarks>
public sealed record MarketPrice(
    long           TypeId,
    long           StationId,
    double?        IskBuy,
    double?        IskSell,
    DateTimeOffset FetchedAt);
