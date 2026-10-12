namespace EveDashboard.Core.Pricing;

using EveDashboard.Core.Settings;

/// <summary>
/// The one definition of "this material has a usable price" - the rule that
/// decides whether a captured <c>(isk_buy, isk_sell)</c> pair answers the active
/// <see cref="PriceMode"/> or counts as a gap to be filled.
/// </summary>
/// <remarks>
/// Shared because two components must agree on it: the live fetcher uses it to
/// decide what to pull from the market, and the historical pricer uses it to
/// decide which materials a session still needs after its window averages are
/// in. If the two ever disagreed, a historical session would either miss prices
/// it should have or overwrite window averages with today's values.
/// </remarks>
public static class PriceCoverage
{
    /// <summary>
    /// True when a single side is a real price: non-null and strictly positive.
    /// Null or <c>&lt;= 0</c> is treated as no price at all.
    /// </summary>
    /// <param name="value">A captured <c>isk_buy</c> or <c>isk_sell</c>.</param>
    /// <returns>Whether the value can be used in a valuation.</returns>
    public static bool IsUsable(double? value) => value is > 0;

    /// <summary>
    /// True when the captured pair satisfies <paramref name="mode"/> - i.e. the
    /// material is NOT a gap.
    /// </summary>
    /// <param name="buy">Captured buy price, or <c>null</c>.</param>
    /// <param name="sell">Captured sell price, or <c>null</c>.</param>
    /// <param name="mode">The active price mode.</param>
    /// <returns>Whether the material is already priced for this mode.</returns>
    /// <remarks>
    /// Buy needs a usable buy price, Sell a usable sell price, and Split needs
    /// EITHER side - so a Split gap requires both sides to be missing or zero.
    /// </remarks>
    public static bool Covers(double? buy, double? sell, PriceMode mode) => mode switch
    {
        PriceMode.Sell => IsUsable(sell),
        PriceMode.Split => IsUsable(buy) || IsUsable(sell),
        _ => IsUsable(buy),   // Buy (default)
    };
}
