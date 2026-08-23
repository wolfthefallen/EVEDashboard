namespace EveDashboard.App.Wpf.Tests.Reporting.HaulValue;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.App.Wpf.Reporting.HaulValue;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.Stats.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

public class HaulValueWindowViewModelTests
{
    private const long Jita = 60003760L;

    private static HaulValueWindowViewModel Build(
        FakeReporting? reporting = null,
        FakeStats? stats = null,
        FakeStations? stations = null,
        FakeResolver? resolver = null,
        StubPriceClient? prices = null)
    {
        reporting ??= new FakeReporting();
        stats ??= new FakeStats();
        stations ??= new FakeStations(new MarketStation(Jita, "Jita", "4-4", 10000002L, "The Forge"));
        resolver ??= new FakeResolver();
        prices ??= new StubPriceClient();
        return new HaulValueWindowViewModel(
            reporting, stats, stations, new FakeSettingsRepository(), resolver,
            new HaulValuationCalculator(prices), NullLogger<HaulValueWindowViewModel>.Instance);
    }

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        Action act = () => _ = new HaulValueWindowViewModel(
            null!, new FakeStats(), new FakeStations(), new FakeSettingsRepository(),
            new FakeResolver(), new HaulValuationCalculator(new StubPriceClient()),
            NullLogger<HaulValueWindowViewModel>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RefreshSessions_populates_picker_with_nothing_checked()
    {
        var reporting = new FakeReporting(
            new SessionSummaryRow("s1", "Belt run", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0),
            new SessionSummaryRow("s2", "Moon pop", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0));
        var vm = Build(reporting: reporting);

        await vm.RefreshSessionsAsync();

        vm.Sessions.Should().HaveCount(2);
        vm.Sessions.Should().OnlyContain(s => !s.IsSelected);
    }

    [Fact]
    public async Task RefreshSessions_preserves_the_users_checks_by_id()
    {
        var reporting = new FakeReporting(
            new SessionSummaryRow("s1", "Belt run", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0),
            new SessionSummaryRow("s2", "Moon pop", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0));
        var vm = Build(reporting: reporting);
        await vm.RefreshSessionsAsync();
        vm.Sessions.Single(s => s.SessionId == "s2").IsSelected = true;

        await vm.RefreshSessionsAsync(); // e.g. window reshown

        vm.Sessions.Single(s => s.SessionId == "s2").IsSelected.Should().BeTrue();
        vm.Sessions.Single(s => s.SessionId == "s1").IsSelected.Should().BeFalse();
    }

    [Fact]
    public async Task Paste_mode_values_resolved_items_and_flags_unresolved()
    {
        var resolver = new FakeResolver(new() { ["Tritanium"] = 34L });   // "Unobtainium" won't resolve
        var prices = new StubPriceClient(new() { [(Jita, 34L)] = (5.0, 6.0) });
        var vm = Build(resolver: resolver, prices: prices);
        vm.UsePasteMode = true;
        vm.PasteText = "Tritanium\t1000\nUnobtainium\t5";

        await vm.ValueHaulAsync();

        vm.StationResults.Should().ContainSingle();
        vm.StationResults[0].TotalValue.Should().Contain("5,000"); // 5 * 1000, buy mode
        vm.Breakdown.Should().ContainSingle(l => l.Name == "Tritanium");
        vm.StatusText.Should().Contain("Not found: Unobtainium");
    }

    [Fact]
    public async Task Paste_mode_empty_box_reports_and_clears()
    {
        var vm = Build();
        vm.UsePasteMode = true;
        vm.PasteText = "   ";

        await vm.ValueHaulAsync();

        vm.StationResults.Should().BeEmpty();
        vm.StatusText.Should().Contain("empty");
    }

    [Fact]
    public async Task Session_mode_values_the_selected_sessions_refined_output()
    {
        var stats = new FakeStats(new[] { new RefinedMaterialAmount(34L, "Tritanium", 2000) });
        var prices = new StubPriceClient(new() { [(Jita, 34L)] = (5.0, 6.0) });
        var vm = Build(
            reporting: new FakeReporting(new SessionSummaryRow("s1", "Belt run", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0)),
            stats: stats, prices: prices);
        await vm.RefreshSessionsAsync();
        vm.Sessions.Single(s => s.SessionId == "s1").IsSelected = true;
        vm.UsePasteMode = false; // session mode

        await vm.ValueHaulAsync();

        vm.StationResults.Should().ContainSingle();
        vm.StationResults[0].TotalValue.Should().Contain("10,000"); // 5 * 2000
        vm.Breakdown.Should().ContainSingle(l => l.Name == "Tritanium");
        vm.BreakdownHeader.Should().Contain("Jita"); // best station is named
    }

    [Fact]
    public async Task Session_mode_combines_multiple_checked_sessions()
    {
        var stats = new FakeStats(new Dictionary<string, RefinedMaterialAmount[]>
        {
            ["s1"] = new[] { new RefinedMaterialAmount(34L, "Tritanium", 1000) },
            ["s2"] = new[] { new RefinedMaterialAmount(34L, "Tritanium", 3000) },
        });
        var prices = new StubPriceClient(new() { [(Jita, 34L)] = (5.0, 6.0) });
        var vm = Build(
            reporting: new FakeReporting(
                new SessionSummaryRow("s1", "A", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0),
                new SessionSummaryRow("s2", "B", System.DateTimeOffset.UnixEpoch, System.TimeSpan.Zero, 1, 0)),
            stats: stats, prices: prices);
        await vm.RefreshSessionsAsync();
        foreach (var s in vm.Sessions) s.IsSelected = true; // both
        vm.UsePasteMode = false;

        await vm.ValueHaulAsync();

        // 1000 + 3000 = 4000 units summed across sessions; * 5 buy = 20,000.
        vm.Breakdown.Should().ContainSingle(l => l.Name == "Tritanium" && l.Quantity == "4,000");
        vm.StationResults[0].TotalValue.Should().Contain("20,000");
        vm.StatusText.Should().Contain("Combined 2 sessions");
    }

    [Fact]
    public async Task Session_mode_with_no_selection_reports()
    {
        var vm = Build();
        vm.UsePasteMode = false; // nothing checked

        await vm.ValueHaulAsync();

        vm.StationResults.Should().BeEmpty();
        vm.StatusText.Should().Contain("at least one session");
    }

    // ---- fakes ----

    private sealed class FakeReporting : IReportingQueries
    {
        private readonly SessionsReport _report;
        public FakeReporting(params SessionSummaryRow[] sessions) =>
            _report = new SessionsReport(sessions, LifetimeSummary.Empty);
        public Task<SessionsReport> GetAllSessionSummariesAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(_report);
        public Task<IReadOnlyList<PilotSummaryRow>> GetPilotsForSessionAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OreBreakdownRow>> GetOreBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RefineMatBreakdownRow>> GetRefinedMatBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionTimeline> GetSessionTimelineAsync(string sessionId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeStats : ISessionStatsRepository
    {
        private readonly IReadOnlyList<RefinedMaterialAmount> _default;
        private readonly Dictionary<string, IReadOnlyList<RefinedMaterialAmount>> _bySession = new();

        public FakeStats(IReadOnlyList<RefinedMaterialAmount>? refined = null) =>
            _default = refined ?? System.Array.Empty<RefinedMaterialAmount>();

        public FakeStats(Dictionary<string, RefinedMaterialAmount[]> bySession)
        {
            _default = System.Array.Empty<RefinedMaterialAmount>();
            foreach (var kv in bySession) _bySession[kv.Key] = kv.Value;
        }

        public Task<SessionStats> GetSessionStatsAsync(string sessionId, System.DateTimeOffset asOf, bool allowLazyFetch, CancellationToken ct = default)
        {
            var refined = _bySession.TryGetValue(sessionId, out var r) ? r : _default;
            var stats = new SessionStats(sessionId, System.TimeSpan.Zero, asOf, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<long, CharacterStats>()) { RefinedOutput = refined };
            return Task.FromResult(stats);
        }
    }

    private sealed class FakeStations : IMarketStationCatalog
    {
        private readonly IReadOnlyList<MarketStation> _stations;
        public FakeStations(params MarketStation[] stations) => _stations = stations;
        public Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_stations);
        public Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(MarketStation station, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(MarketStation station, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(long stationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeResolver : ISdeTypeNameResolver
    {
        private readonly Dictionary<string, long> _byName;
        public FakeResolver(Dictionary<string, long>? byName = null) => _byName = byName ?? new();
        public Task<long?> ResolveTypeIdAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_byName.TryGetValue(name.Trim(), out var id) ? id : (long?)null);
    }

    private sealed class StubPriceClient : IMarketPriceClient
    {
        private readonly Dictionary<(long, long), (double? Buy, double? Sell)> _prices;
        public StubPriceClient(Dictionary<(long, long), (double? Buy, double? Sell)>? prices = null) => _prices = prices ?? new();
        public Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(long stationId, IReadOnlyList<long> typeIds, CancellationToken ct = default)
        {
            var list = new List<MarketPrice>();
            foreach (var t in typeIds)
                if (_prices.TryGetValue((stationId, t), out var bs))
                    list.Add(new MarketPrice(t, stationId, bs.Buy, bs.Sell, System.DateTimeOffset.UnixEpoch));
            return Task.FromResult<IReadOnlyList<MarketPrice>>(list);
        }
    }
}
