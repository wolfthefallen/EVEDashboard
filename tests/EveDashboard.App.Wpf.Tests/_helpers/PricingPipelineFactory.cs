namespace EveDashboard.App.Wpf.Tests.Helpers;

using System.Net.Http;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.MarketData.Pricing;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Constructs the three market-pricing pipeline singletons (<see cref="GoonMetricsPriceClient"/>,
/// <see cref="MarketPriceFetcher"/>, <see cref="IskCalculator"/>) for use in
/// <c>ServiceRegistrationTests</c> calls to <c>BuildServiceProvider</c>.
/// </summary>
/// <remarks>
/// Tests do not exercise GoonMetrics (no ActiveSessionChanged firing reaches the
/// HTTP layer in these tests because the session-changed event handler reads
/// <see cref="ISdeLookup.GetRefinedMaterialTypeIds"/> which returns empty for
/// <see cref="FakeSdeLookup"/>). The HttpClient still must be non-null per the
/// ctor's null-arg check.
/// </remarks>
internal static class PricingPipelineFactory
{
    public static (GoonMetricsPriceClient Client, MarketPriceFetcher Fetcher, IskCalculator Calculator) Build(
        ISqliteDatabase       sessionDb,
        ISdeLookup            sdeLookup,
        ISettingsRepository   settings,
        IActiveSessionContext activeSession)
    {
        var http = new HttpClient();
        var client = new GoonMetricsPriceClient(http, NullLogger<GoonMetricsPriceClient>.Instance);
        var fetcher = new MarketPriceFetcher(
            client, sessionDb, sdeLookup, settings, activeSession,
            NullLogger<MarketPriceFetcher>.Instance);
        var calculator = new IskCalculator(
            fetcher, sessionDb, sdeLookup, NullLogger<IskCalculator>.Instance);
        return (client, fetcher, calculator);
    }
}
