namespace EveDashboard.App.Wpf.Tests.Reporting.HaulValue;

using EveDashboard.App.Wpf.Reporting.HaulValue;
using FluentAssertions;

public class EveInventoryPasteParserTests
{
    [Fact]
    public void Parses_tab_separated_inventory_copy_lines()
    {
        // EVE inventory copy: Name<TAB>Quantity<TAB>Group<TAB>...
        var text = "Tritanium\t1000\tMineral\tMaterial\nPyerite\t250\tMineral\tMaterial";

        var lines = EveInventoryPasteParser.Parse(text);

        lines.Should().HaveCount(2);
        lines[0].Should().Be(new PasteLine("Tritanium", 1000));
        lines[1].Should().Be(new PasteLine("Pyerite", 250));
    }

    [Fact]
    public void Strips_thousands_separators_in_the_quantity()
    {
        EveInventoryPasteParser.Parse("Veldspar\t1,234,567").Should()
            .ContainSingle().Which.Should().Be(new PasteLine("Veldspar", 1234567));
        EveInventoryPasteParser.Parse("Veldspar\t1.000.000").Should()
            .ContainSingle().Which.Should().Be(new PasteLine("Veldspar", 1000000));
    }

    [Theory]
    [InlineData("Tritanium 500", "Tritanium", 500)]
    [InlineData("Tritanium x500", "Tritanium", 500)]
    [InlineData("Dark Ochre 250", "Dark Ochre", 250)]
    public void Parses_space_separated_name_and_quantity(string input, string name, long qty)
    {
        EveInventoryPasteParser.Parse(input).Should()
            .ContainSingle().Which.Should().Be(new PasteLine(name, qty));
    }

    [Theory]
    [InlineData("Rifter")]
    [InlineData("Compressed Veldspar")]
    public void Defaults_quantity_to_one_when_no_number(string input)
    {
        EveInventoryPasteParser.Parse(input).Should()
            .ContainSingle().Which.Should().Be(new PasteLine(input, 1));
    }

    [Fact]
    public void Skips_blank_lines_and_preserves_order()
    {
        var text = "\nTritanium\t100\n\n  \nPyerite\t200\n";

        var lines = EveInventoryPasteParser.Parse(text);

        lines.Should().HaveCount(2);
        lines[0].Name.Should().Be("Tritanium");
        lines[1].Name.Should().Be("Pyerite");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Empty_input_yields_no_lines(string? input)
    {
        EveInventoryPasteParser.Parse(input).Should().BeEmpty();
    }
}
