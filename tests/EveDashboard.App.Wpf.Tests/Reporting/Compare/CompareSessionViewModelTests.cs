namespace EveDashboard.App.Wpf.Tests.Reporting.Compare;

using System.Collections.Immutable;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.Stats.Queries;
using FluentAssertions;

/// <summary>
/// Plan 36: unit tests for the decluttered compare-mode VM. Verifies the
/// stacked session-name subheader, row counts/labels, totals/averages,
/// corrected rate math (5.2 = C), divide-by-zero guards, and that rate rows
/// have <c>null</c> Total cells (5.1 — Rates table has no Total column).
/// </summary>
public class CompareSessionViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Build a SessionStats snapshot with totals shaped for the tests; defaults to all-zero crit fields.</summary>
    private static SessionStats Snap(
        long   sessionId,
        double totalSeconds,
        double totalM3,
        double totalIsk,
        double totalCritIsk = 0.0,
        double critIskPercent = 0.0) =>
        new(
            // Plan 38: SessionStats.SessionId is a string id; the numeric arg is a
            // convenient per-snapshot label for these compare-math tests.
            SessionId:          sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TotalTimeMined:     TimeSpan.FromSeconds(totalSeconds),
            LastCalculated:     T0,
            TotalM3Mined:       totalM3,
            M3PerSecond:        totalSeconds > 0 ? totalM3  / totalSeconds : 0.0,
            TotalIskMined:      totalIsk,
            IskPerHour:         totalSeconds > 0 ? totalIsk / (totalSeconds / 3600.0) : 0.0,
            TotalIskFromCrits:  totalCritIsk,
            CritIskPercent:     critIskPercent,
            TotalM3FromCrits:   0.0,
            CritCount:          0,
            CritChance:         0.0,
            ByCharacter:        ImmutableDictionary<long, CharacterStats>.Empty);

    [Fact]
    public void Ctor_throws_on_null_snapshots()
    {
        Action act = () => _ = new CompareSessionViewModel(null!, new[] { "a" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_null_session_names()
    {
        Action act = () => _ = new CompareSessionViewModel(new[] { Snap(1, 60, 100, 100) }, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_mismatched_lengths()
    {
        Action act = () => _ = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100), Snap(2, 60, 100, 100) },
            new[] { "only-one" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_throws_on_empty_snapshots()
    {
        Action act = () => _ = new CompareSessionViewModel(Array.Empty<SessionStats>(), Array.Empty<string>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SessionNames_mirrors_input_in_order()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100), Snap(2, 60, 100, 100), Snap(3, 60, 100, 100) },
            new[] { "Alpha", "Bravo", "Charlie" });

        vm.SessionNames.Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void CumulativeRows_has_4_rows_with_expected_labels_in_order()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100) },
            new[] { "S1" });

        vm.CumulativeRows.Should().HaveCount(4);
        vm.CumulativeRows.Select(r => r.Label).Should().Equal("ISK", "Time", "m³", "Crit ISK %");
    }

    [Fact]
    public void RateRows_has_2_rows_with_expected_labels_in_order()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100) },
            new[] { "S1" });

        vm.RateRows.Should().HaveCount(2);
        vm.RateRows.Select(r => r.Label).Should().Equal("m³/sec", "ISK/h");
    }

    [Fact]
    public void Cumulative_ISK_row_Total_is_sum_and_Avg_is_sum_over_N()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 1_000), Snap(2, 60, 100, 2_000), Snap(3, 60, 100, 3_000) },
            new[] { "S1", "S2", "S3" });

        var iskRow = vm.CumulativeRows.First(r => r.Label == "ISK");
        iskRow.Total.Should().Be(6_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        iskRow.Avg.Should().Be(2_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Cumulative_Time_row_renders_Total_and_Avg_as_HHmmss()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 0, 0), Snap(2, 120, 0, 0) },
            new[] { "S1", "S2" });

        var timeRow = vm.CumulativeRows.First(r => r.Label == "Time");
        timeRow.Total.Should().Be("00:03:00");      // 60 + 120 = 180s
        timeRow.Avg.Should().Be("00:01:30");        // 180 / 2 = 90s
    }

    [Fact]
    public void Cumulative_CritIskPercent_row_uses_union_wide_ratio_for_both_Avg_and_Total()
    {
        // S1: 100 ISK, 30 of it crit. S2: 200 ISK, 90 of it crit.
        // Union: 300 ISK, 120 of it crit. Ratio = 120/300 = 0.4.
        using var vm = new CompareSessionViewModel(
            new[]
            {
                Snap(1, 60, 100, 100, totalCritIsk: 30,  critIskPercent: 0.30),
                Snap(2, 60, 100, 200, totalCritIsk: 90,  critIskPercent: 0.45),
            },
            new[] { "S1", "S2" });

        var critRow = vm.CumulativeRows.First(r => r.Label == "Crit ISK %");
        var expected = (0.4).ToString("P1", System.Globalization.CultureInfo.InvariantCulture);
        critRow.Avg.Should().Be(expected);
        critRow.Total.Should().Be(expected);
    }

    [Fact]
    public void Rate_m3PerSec_uses_union_totals_not_mean_of_per_session_rates()
    {
        // S1: 100m³ over 50s -> per-session rate 2.0. S2: 300m³ over 50s -> rate 6.0.
        // Naive mean would be 4.0. Corrected math: (100+300) / (50+50) = 4.0... hmm same.
        // Pick asymmetric durations to make them differ: S1 100m³/50s=2.0, S2 300m³/100s=3.0.
        // Mean of rates = 2.5. Corrected union rate = (100+300)/(50+100) = 400/150 ≈ 2.6667.
        using var vm = new CompareSessionViewModel(
            new[]
            {
                Snap(1, 50,  100, 0),
                Snap(2, 100, 300, 0),
            },
            new[] { "S1", "S2" });

        var rateRow = vm.RateRows.First(r => r.Label == "m³/sec");
        var expected = (400.0 / 150.0).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        rateRow.Avg.Should().Be(expected);
    }

    [Fact]
    public void Rate_iskPerHour_uses_union_totals_not_mean_of_per_session_rates()
    {
        // S1: 100 ISK over 3600s -> 100/h. S2: 300 ISK over 1800s -> 600/h.
        // Mean of rates = 350. Corrected: (100+300) / ((3600+1800)/3600) = 400/1.5 ≈ 266.67.
        using var vm = new CompareSessionViewModel(
            new[]
            {
                Snap(1, 3600, 0, 100),
                Snap(2, 1800, 0, 300),
            },
            new[] { "S1", "S2" });

        var rateRow = vm.RateRows.First(r => r.Label == "ISK/h");
        var expected = (400.0 / 1.5).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        rateRow.Avg.Should().Be(expected);
    }

    [Fact]
    public void All_RateRows_have_null_Total()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100), Snap(2, 60, 100, 100) },
            new[] { "S1", "S2" });

        vm.RateRows.Should().OnlyContain(r => r.Total == null);
    }

    [Fact]
    public void Zero_time_edge_case_renders_rate_as_zero_not_throws()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 0, 0, 0), Snap(2, 0, 0, 0) },
            new[] { "S1", "S2" });

        vm.RateRows.First(r => r.Label == "m³/sec").Avg.Should().Be("0.00");
        vm.RateRows.First(r => r.Label == "ISK/h").Avg.Should().Be("0");
        // Divide-by-zero guard on union-wide crit percent as well.
        vm.CumulativeRows.First(r => r.Label == "Crit ISK %").Avg.Should().Be(0.0.ToString(
            "P1", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Five_session_compare_builds_clean()
    {
        using var vm = new CompareSessionViewModel(
            Enumerable.Range(1, 5).Select(i => Snap(i, 60, 100 * i, 1000 * i)).ToArray(),
            Enumerable.Range(1, 5).Select(i => $"Session {i}").ToArray());

        vm.SessionNames.Should().HaveCount(5);
        vm.CumulativeRows.Should().HaveCount(4);
        vm.RateRows.Should().HaveCount(2);
        // Total ISK = 1+2+3+4+5 * 1000 = 15000.
        vm.CumulativeRows.First(r => r.Label == "ISK").Total
            .Should().Be(15_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- Plan 45: grand-total labels + Total Refined Output table ---------------

    [Fact]
    public void TotalTimeDisplay_and_TotalIskDisplay_render_union_totals()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 0, 1_000), Snap(2, 120, 0, 2_000) },
            new[] { "S1", "S2" });

        vm.TotalTimeDisplay.Should().Be("00:03:00");   // 60 + 120 = 180s
        vm.TotalIskDisplay.Should().Be(
            3_000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RefinedRows_sum_quantity_and_isk_per_material_across_sessions_sorted_desc()
    {
        var s1 = Snap(1, 60, 0, 7_000) with
        {
            RefinedOutput = new[]
            {
                new RefinedMaterialAmount(34, "Tritanium", 1000L) { Isk = 5000.0 },
                new RefinedMaterialAmount(35, "Pyerite",    500L) { Isk = 2000.0 },
            },
        };
        var s2 = Snap(2, 60, 0, 9_000) with
        {
            RefinedOutput = new[]
            {
                new RefinedMaterialAmount(34, "Tritanium", 200L) { Isk = 1000.0 },
                new RefinedMaterialAmount(35, "Pyerite",   800L) { Isk = 8000.0 },
            },
        };
        using var vm = new CompareSessionViewModel(new[] { s1, s2 }, new[] { "S1", "S2" });

        // Tritanium: 1200 qty / 6000 ISK; Pyerite: 1300 qty / 10000 ISK. Sorted by qty desc.
        vm.RefinedRows.Should().HaveCount(2);
        vm.RefinedRows[0].Material.Should().Be("Pyerite");
        vm.RefinedRows[0].Quantity.Should().Be(1300L);
        vm.RefinedRows[0].Isk.Should().BeApproximately(10_000.0, 1e-6);
        vm.RefinedRows[1].Material.Should().Be("Tritanium");
        vm.RefinedRows[1].Quantity.Should().Be(1200L);
        vm.RefinedRows[1].Isk.Should().BeApproximately(6_000.0, 1e-6);
        // Reconciliation: refined ISK sums to the union Total ISK (7000 + 9000 = 16000).
        vm.RefinedRows.Sum(r => r.Isk).Should().BeApproximately(16_000.0, 1e-6);
    }

    [Fact]
    public void RefinedRows_empty_when_no_refined_output()
    {
        using var vm = new CompareSessionViewModel(
            new[] { Snap(1, 60, 100, 100), Snap(2, 60, 100, 100) },
            new[] { "S1", "S2" });

        vm.RefinedRows.Should().BeEmpty();
    }
}
