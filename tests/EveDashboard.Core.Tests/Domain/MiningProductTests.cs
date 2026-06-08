using EveDashboard.Core.Domain;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Domain;

public class MiningProductTests
{
    [Fact]
    public void TwoProductsWithSameFields_AreEqual()
    {
        var a = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var b = new MiningProduct(1230, "Veldspar", 0.1, 100);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ProductsDifferingByTypeId_AreNotEqual()
    {
        var a = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var b = new MiningProduct(1231, "Veldspar", 0.1, 100);
        a.Should().NotBe(b);
    }

    [Fact]
    public void ProductsDifferingByVolume_AreNotEqual()
    {
        var a = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var b = new MiningProduct(1230, "Veldspar", 0.15, 100);
        a.Should().NotBe(b);
    }

    [Fact]
    public void ProductsDifferingByPortionSize_AreNotEqual()
    {
        var a = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var b = new MiningProduct(1230, "Veldspar", 0.1, 333);
        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ContainsNameAndTypeId()
    {
        var p = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var s = p.ToString();
        s.Should().Contain("Veldspar");
        s.Should().Contain("1230");
    }

    [Fact]
    public void PositionalDeconstruction_ReturnsAllFields()
    {
        var p = new MiningProduct(1230, "Veldspar", 0.1, 100);
        var (typeId, name, volume, portionSize) = p;
        typeId.Should().Be(1230);
        name.Should().Be("Veldspar");
        volume.Should().Be(0.1);
        portionSize.Should().Be(100);
    }
}
