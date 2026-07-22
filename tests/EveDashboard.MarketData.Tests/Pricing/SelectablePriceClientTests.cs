namespace EveDashboard.MarketData.Tests.Pricing;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using FluentAssertions;

public class SelectablePriceClientTests
{
    /// <summary>A price client that records the call and returns one identifiable price.</summary>
    private sealed class SpyClient : IMarketPriceClient
    {
        private readonly long _markerTypeId;
        public bool Called { get; private set; }

        public SpyClient(long markerTypeId) => _markerTypeId = markerTypeId;

        public Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
            long stationId, IReadOnlyList<long> typeIds, CancellationToken ct = default)
        {
            Called = true;
            IReadOnlyList<MarketPrice> result = new[]
            {
                new MarketPrice(_markerTypeId, stationId, 1.0, 2.0, System.DateTimeOffset.UnixEpoch),
            };
            return Task.FromResult(result);
        }
    }

    private const long GoonMarker = 111L;
    private const long EsiMarker = 222L;

    private static (SelectablePriceClient Sut, SpyClient Goon, SpyClient Esi, StubSettingsRepository Settings) Build(PriceSource source)
    {
        var goon = new SpyClient(GoonMarker);
        var esi = new SpyClient(EsiMarker);
        var settings = new StubSettingsRepository { PriceSource = source };
        return (new SelectablePriceClient(settings, goon, esi), goon, esi, settings);
    }

    [Fact]
    public async Task Delegates_to_goonmetrics_when_source_is_goonmetrics()
    {
        var (sut, goon, esi, _) = Build(PriceSource.GoonMetrics);

        var result = await sut.FetchPricesAsync(60003760L, new[] { 34L });

        goon.Called.Should().BeTrue();
        esi.Called.Should().BeFalse();
        result.Should().ContainSingle(p => p.TypeId == GoonMarker);
    }

    [Fact]
    public async Task Delegates_to_esi_when_source_is_esi()
    {
        var (sut, goon, esi, _) = Build(PriceSource.Esi);

        var result = await sut.FetchPricesAsync(60003760L, new[] { 34L });

        esi.Called.Should().BeTrue();
        goon.Called.Should().BeFalse();
        result.Should().ContainSingle(p => p.TypeId == EsiMarker);
    }

    [Fact]
    public async Task Re_reads_the_setting_each_call_so_a_switch_takes_effect_without_restart()
    {
        var (sut, goon, esi, settings) = Build(PriceSource.GoonMetrics);

        await sut.FetchPricesAsync(60003760L, new[] { 34L });
        settings.PriceSource = PriceSource.Esi; // flip mid-life
        var afterSwitch = await sut.FetchPricesAsync(60003760L, new[] { 34L });

        goon.Called.Should().BeTrue();
        esi.Called.Should().BeTrue();
        afterSwitch.Should().ContainSingle(p => p.TypeId == EsiMarker);
    }

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        var goon = new SpyClient(GoonMarker);
        var settings = new StubSettingsRepository();
        ((Action)(() => _ = new SelectablePriceClient(null!, goon, goon))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SelectablePriceClient(settings, null!, goon))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SelectablePriceClient(settings, goon, null!))).Should().Throw<ArgumentNullException>();
    }
}
