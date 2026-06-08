namespace EveDashboard.Core.Settings;

/// <summary>
/// ISK price source used by the v1 ISK calculator. Persisted to
/// <c>setting.price_mode</c> as the lowercase string <c>"buy"</c>,
/// <c>"sell"</c>, or <c>"split"</c>.
/// </summary>
public enum PriceMode
{
    /// <summary>Use the highest buy-order price (default — typical instant-realize).</summary>
    Buy,

    /// <summary>Use the lowest sell-order price (replacement cost).</summary>
    Sell,

    /// <summary>Use <c>(buy_price + sell_price) / 2</c> as a mid-market neutral.</summary>
    Split,
}
