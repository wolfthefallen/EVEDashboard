namespace EveDashboard.App.Wpf.Tests.Reporting.Lifetime;

using EveDashboard.App.Wpf.Reporting.Lifetime;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using EveDashboard.Stats.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Plan 51: unit tests for <see cref="LifetimeTotalsWindowViewModel"/>. Covers
/// the refresh-from-query population, the dispose-and-rebuild on re-refresh, and
/// the swallow-and-keep-prior behaviour on a query failure.
/// </summary>
public class LifetimeTotalsWindowViewModelTests
{
    private static LifetimeTotalsWindowViewModel NewVm(IReportingQueries queries, FakeSettingsRepository settings) =>
        new(queries, settings, NullLogger<LifetimeTotalsWindowViewModel>.Instance);

    [Fact]
    public async Task RefreshAsync_populates_Summary_from_the_query()
    {
        var lifetime = new LifetimeSummary(
            TimeSpan.FromSeconds(180),
            3_000.0,
            Array.Empty<RefinedMaterialAmount>());
        var queries = new StubReportingQueries
        {
            OnGetAll = () => Task.FromResult(new SessionsReport(Array.Empty<SessionSummaryRow>(), lifetime)),
        };
        var vm = NewVm(queries, new FakeSettingsRepository());

        await vm.RefreshAsync();

        vm.Summary.Should().NotBeNull();
        vm.Summary!.TimeTrackedDisplay.Should().Be("00:03:00");
        vm.Summary.TotalIskDisplay.Should().Be(
            3_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RefreshAsync_twice_replaces_the_Summary_instance()
    {
        var queries = new StubReportingQueries();
        var vm = NewVm(queries, new FakeSettingsRepository());

        await vm.RefreshAsync();
        var first = vm.Summary;
        await vm.RefreshAsync();

        first.Should().NotBeNull();
        vm.Summary.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task RefreshAsync_swallows_a_query_failure_and_keeps_the_prior_Summary()
    {
        var lifetime = new LifetimeSummary(TimeSpan.FromMinutes(10), 500.0, Array.Empty<RefinedMaterialAmount>());
        var queries = new StubReportingQueries
        {
            OnGetAll = () => Task.FromResult(new SessionsReport(Array.Empty<SessionSummaryRow>(), lifetime)),
        };
        var vm = NewVm(queries, new FakeSettingsRepository());

        await vm.RefreshAsync();
        var first = vm.Summary;

        // Now make the query fault — the refresh must swallow it and keep `first`.
        queries.OnGetAll = () => Task.FromException<SessionsReport>(new InvalidOperationException("boom"));
        Func<Task> act = async () => await vm.RefreshAsync();

        await act.Should().NotThrowAsync();
        vm.Summary.Should().BeSameAs(first);
    }

    /// <summary>
    /// Hand-rolled <see cref="IReportingQueries"/> stub. Only
    /// <see cref="GetAllSessionSummariesAsync"/> is exercised by the VM; the
    /// remaining members are never called and throw if they ever are.
    /// </summary>
    private sealed class StubReportingQueries : IReportingQueries
    {
        public Func<Task<SessionsReport>> OnGetAll { get; set; } =
            () => Task.FromResult(new SessionsReport(Array.Empty<SessionSummaryRow>(), LifetimeSummary.Empty));

        public Task<SessionsReport> GetAllSessionSummariesAsync(CancellationToken ct = default) => OnGetAll();

        public Task<IReadOnlyList<PilotSummaryRow>> GetPilotsForSessionAsync(string sessionId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<OreBreakdownRow>> GetOreBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<RefineMatBreakdownRow>> GetRefinedMatBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SessionTimeline> GetSessionTimelineAsync(string sessionId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
