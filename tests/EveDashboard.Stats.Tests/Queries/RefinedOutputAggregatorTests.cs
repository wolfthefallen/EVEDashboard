namespace EveDashboard.Stats.Tests.Queries;

using EveDashboard.Stats.Queries;
using FluentAssertions;

/// <summary>
/// Unit tests for <see cref="RefinedOutputAggregator.SumAcross"/> — the
/// single source of the per-material refined sum+sort shared by the compare view
/// and the empty-state lifetime rollup.
/// </summary>
public class RefinedOutputAggregatorTests
{
    private const long TritaniumTypeId = 34L;
    private const long PyeriteTypeId = 35L;

    [Fact]
    public void SumAcross_sums_quantity_and_isk_per_material_across_scopes()
    {
        var scope1 = new[]
        {
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 1000L) { Isk = 5000.0 },
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",    500L) { Isk = 2000.0 },
        };
        var scope2 = new[]
        {
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 200L) { Isk = 1000.0 },
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   800L) { Isk = 8000.0 },
        };

        var result = RefinedOutputAggregator.SumAcross(new[] { scope1, scope2 });

        result.Should().HaveCount(2);
        var trit = result.Single(m => m.TypeId == TritaniumTypeId);
        trit.Quantity.Should().Be(1200L);
        trit.Isk.Should().BeApproximately(6000.0, 1e-6);
        var pye = result.Single(m => m.TypeId == PyeriteTypeId);
        pye.Quantity.Should().Be(1300L);
        pye.Isk.Should().BeApproximately(10_000.0, 1e-6);
    }

    [Fact]
    public void SumAcross_takes_name_from_first_occurrence()
    {
        // Same TypeId, different names across scopes — the first wins.
        var scope1 = new[] { new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 100L) };
        var scope2 = new[] { new RefinedMaterialAmount(TritaniumTypeId, "#34", 50L) };

        var result = RefinedOutputAggregator.SumAcross(new[] { scope1, scope2 });

        result.Single().Name.Should().Be("Tritanium");
        result.Single().Quantity.Should().Be(150L);
    }

    [Fact]
    public void SumAcross_sorts_by_quantity_descending()
    {
        var scope = new[]
        {
            new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 100L),
            new RefinedMaterialAmount(PyeriteTypeId,   "Pyerite",   900L),
        };

        var result = RefinedOutputAggregator.SumAcross(new[] { scope });

        result.Select(m => m.Name).Should().ContainInOrder("Pyerite", "Tritanium");
    }

    [Fact]
    public void SumAcross_empty_input_returns_empty()
    {
        RefinedOutputAggregator.SumAcross(Array.Empty<IReadOnlyList<RefinedMaterialAmount>>())
            .Should().BeEmpty();
    }

    [Fact]
    public void SumAcross_unpriced_material_keeps_quantity_with_zero_isk()
    {
        // Isk defaults to 0 when no price was captured — the quantity must still
        // sum correctly.
        var scope1 = new[] { new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 300L) };
        var scope2 = new[] { new RefinedMaterialAmount(TritaniumTypeId, "Tritanium", 700L) };

        var result = RefinedOutputAggregator.SumAcross(new[] { scope1, scope2 });

        result.Single().Quantity.Should().Be(1000L);
        result.Single().Isk.Should().Be(0.0);
    }
}
