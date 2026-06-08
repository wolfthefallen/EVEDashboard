namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;

/// <summary>
/// Test stub <see cref="IIskCalculator"/> that always returns <c>0</c> ISK.
/// Used by fixtures whose tests do not exercise ISK assertions — keeps the
/// SqliteSessionStatsRepository ctor satisfied without pulling the real
/// MarketData stack into every WPF unit test.
/// </summary>
internal sealed class NoOpIskCalculator : IIskCalculator
{
    public Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        long stationId,
        string sessionId,
        bool allowLazyFetch,
        double refiningYieldPercent,
        PriceMode priceMode,
        CancellationToken ct = default) =>
        Task.FromResult(new IskCalculationResult(0.0, 0.0, new Dictionary<long, double>()));

    public Task<IskCalculationResult> ComputeAsync(
        IReadOnlyDictionary<long, long> typeIdToUnitsMined,
        IReadOnlyDictionary<long, long> critTypeIdToUnitsMined,
        long stationId,
        string sessionId,
        bool allowLazyFetch,
        double refiningYieldPercent,
        PriceMode priceMode,
        CancellationToken ct = default) =>
        Task.FromResult(new IskCalculationResult(0.0, 0.0, new Dictionary<long, double>()));
}
