namespace EveDashboard.App.Wpf.Tests.Reporting.Lifetime;

using EveDashboard.App.Wpf.Reporting.Lifetime;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Reporting;
using EveDashboard.Stats.Queries;
using FluentAssertions;

/// <summary>
/// Unit tests for the empty-state lifetime-rollup VM. Verifies the
/// pre-rendered Time/ISK labels and that the refined rows mirror the supplied
/// <see cref="LifetimeSummary"/> in order, including the empty case.
/// </summary>
public class LifetimeSummaryViewModelTests
{
    /// <summary>Construct the VM with a throwaway settings fake (these tests exercise the rollup rendering, not persistence).</summary>
    private static LifetimeSummaryViewModel Vm(LifetimeSummary summary) =>
        new(summary, new FakeSettingsRepository());


    [Fact]
    public void TimeTrackedDisplay_and_TotalIskDisplay_render_lifetime_totals()
    {
        var summary = new LifetimeSummary(
            TimeSpan.FromSeconds(180),
            3_000.0,
            Array.Empty<RefinedMaterialAmount>());

        var vm = Vm(summary);

        vm.TimeTrackedDisplay.Should().Be("00:03:00");
        vm.TotalIskDisplay.Should().Be(
            3_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TimeTrackedDisplay_renders_hours_unbounded()
    {
        // 30 hours -> hours are not wrapped at 24 (active-duration formatting).
        var summary = new LifetimeSummary(
            TimeSpan.FromHours(30),
            0.0,
            Array.Empty<RefinedMaterialAmount>());

        var vm = Vm(summary);

        vm.TimeTrackedDisplay.Should().Be("30:00:00");
    }

    [Fact]
    public void RefinedRows_mirror_the_summary_refined_in_order()
    {
        var refined = new[]
        {
            new RefinedMaterialAmount(35, "Pyerite",   1300L) { Isk = 10_000.0 },
            new RefinedMaterialAmount(34, "Tritanium", 1200L) { Isk = 6_000.0 },
        };
        var summary = new LifetimeSummary(TimeSpan.FromMinutes(75), 350_000.0, refined);

        var vm = Vm(summary);

        vm.RefinedRows.Should().BeSameAs(refined);
        vm.RefinedRows.Select(r => r.Name).Should().ContainInOrder("Pyerite", "Tritanium");
    }

    [Fact]
    public void Empty_summary_renders_zeros_and_no_rows()
    {
        var vm = Vm(LifetimeSummary.Empty);

        vm.TimeTrackedDisplay.Should().Be("00:00:00");
        vm.TotalIskDisplay.Should().Be(
            0.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        vm.RefinedRows.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_throws_on_null_summary()
    {
        Action act = () => _ = Vm(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        Action act = () => _ = new LifetimeSummaryViewModel(LifetimeSummary.Empty, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
