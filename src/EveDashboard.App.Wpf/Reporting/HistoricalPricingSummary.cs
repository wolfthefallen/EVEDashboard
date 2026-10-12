namespace EveDashboard.App.Wpf.Reporting;

using System.Globalization;
using EveDashboard.MarketData.Pricing;

/// <summary>
/// Turns a historical pricing outcome into the sentence the user reads, so a
/// single build and a batch of days describe their prices the same way.
/// </summary>
/// <remarks>
/// Saying where the ISK came from is not decoration: a session priced with
/// today's values is not comparable to one priced from the market as it stood
/// during the window, and the user is the only one who can judge whether that
/// matters for a given day.
/// </remarks>
internal static class HistoricalPricingSummary
{
    /// <summary>Describe one session's pricing.</summary>
    /// <param name="pricing">The pricing outcome.</param>
    /// <returns>One sentence naming the basis and the counts behind it.</returns>
    internal static string Describe(HistoricalPricingResult pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        return pricing.Basis switch
        {
            HistoricalPriceBasis.AveragedFromWindow =>
                $"Prices averaged from {pricing.RowsAveraged} market rows captured inside the window.",
            HistoricalPriceBasis.Mixed =>
                $"Prices averaged from {pricing.RowsAveraged} window rows; {pricing.TypesFetchedNow} material(s) had no window data and use TODAY's prices.",
            _ =>
                "No market prices were captured inside this window at the selected station, so the session is valued with TODAY's prices.",
        };
    }

    /// <summary>Describe a batch's pricing as a count per basis.</summary>
    /// <param name="pricing">One outcome per built session; may be empty.</param>
    /// <returns>One sentence, or an empty string when nothing was priced.</returns>
    internal static string DescribeBatch(IReadOnlyList<HistoricalPricingResult> pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        if (pricing.Count == 0) return string.Empty;

        var fromWindow = pricing.Count(p => p.Basis == HistoricalPriceBasis.AveragedFromWindow);
        var mixed = pricing.Count(p => p.Basis == HistoricalPriceBasis.Mixed);
        var today = pricing.Count(p => p.Basis == HistoricalPriceBasis.FetchedToday);

        if (today == pricing.Count)
        {
            return "No market prices were captured inside these windows, so every session is valued with TODAY's prices.";
        }

        var parts = new List<string>();
        if (fromWindow > 0) parts.Add($"{fromWindow} priced from the market data captured in its own window");
        if (mixed > 0) parts.Add($"{mixed} partly from its window and partly from TODAY's prices");
        if (today > 0) parts.Add($"{today} entirely from TODAY's prices");
        return "Prices: " + string.Join(", ", parts) + ".";
    }

    /// <summary>Format a day for a status line, matching the ISO prefix generated names use.</summary>
    /// <param name="day">The day a session covers.</param>
    /// <returns>The day as <c>yyyy-MM-dd</c>.</returns>
    internal static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
