namespace EveDashboard.Core.Pricing;

using EveDashboard.Core.Settings;

/// <summary>
/// Single source of the "pick one price from a buy/sell pair for the active
/// <see cref="PriceMode"/>" rule. Shared by the live ISK calculator and the
/// haul-value calculator so both value items identically.
/// </summary>
public static class PriceModeSelector
{
    /// <summary>
    /// Choose the per-unit price for <paramref name="mode"/>: Buy -&gt; the buy
    /// price, Sell -&gt; the sell price, Split -&gt; the midpoint when both are
    /// present, otherwise whichever single side exists. Returns <c>null</c> when
    /// no usable price is available for the mode.
    /// </summary>
    /// <param name="buy">Highest buy-order price, or <c>null</c>.</param>
    /// <param name="sell">Lowest sell-order price, or <c>null</c>.</param>
    /// <param name="mode">The active price mode.</param>
    /// <returns>The selected per-unit price, or <c>null</c>.</returns>
    public static double? Pick(double? buy, double? sell, PriceMode mode) => mode switch
    {
        PriceMode.Buy   => buy,
        PriceMode.Sell  => sell,
        PriceMode.Split => buy.HasValue && sell.HasValue
                            ? (buy.Value + sell.Value) / 2.0
                            : (buy ?? sell),
        _               => buy,
    };
}
