namespace EveDashboard.App.Wpf.Tests.Dialogs;

using EveDashboard.App.Wpf.Dialogs;
using FluentAssertions;

/// <summary>
/// Unit tests for <see cref="AddStationDialogViewModel"/>.
/// Covers required-field validation, station_id parsing
/// rules (positive long), optional region fields, and BuildStation projection.
/// </summary>
public class AddStationDialogViewModelTests
{
    [Fact]
    public void Validate_AllRequiredFieldsPresent_NoErrors()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
            RegionIdText = "10000002",
            RegionName = "The Forge",
        };
        vm.Validate().Should().BeEmpty();

        var station = vm.BuildStation();
        station.StationId.Should().Be(60003760L);
        station.SystemName.Should().Be("Jita");
        station.StationName.Should().Be("IV - Moon 4");
        station.RegionId.Should().Be(10000002L);
        station.RegionName.Should().Be("The Forge");
    }

    [Fact]
    public void Validate_MissingStationId_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = string.Empty,
            SystemName = "Jita",
            StationName = "IV - Moon 4",
        };
        vm.Validate().Should().Contain(e => e.Contains("Station ID must be a positive integer"));
    }

    [Fact]
    public void Validate_NonNumericStationId_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "abc",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
        };
        vm.Validate().Should().Contain(e => e.Contains("Station ID must be a positive integer"));
    }

    [Fact]
    public void Validate_NegativeStationId_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "-1",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
        };
        vm.Validate().Should().Contain(e => e.Contains("Station ID must be a positive integer"));
    }

    [Fact]
    public void Validate_MissingSystemName_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "   ",
            StationName = "IV - Moon 4",
        };
        vm.Validate().Should().Contain(e => e.Contains("System name is required"));
    }

    [Fact]
    public void Validate_MissingStationName_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "Jita",
            StationName = string.Empty,
        };
        vm.Validate().Should().Contain(e => e.Contains("Station name is required"));
    }

    [Fact]
    public void Validate_MissingOptionalRegion_NoError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
            RegionIdText = string.Empty,
            RegionName = string.Empty,
        };
        vm.Validate().Should().BeEmpty();
        var station = vm.BuildStation();
        station.RegionId.Should().BeNull();
        station.RegionName.Should().BeNull();
    }

    [Fact]
    public void Validate_NonNumericRegionId_ReturnsError()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
            RegionIdText = "not-a-number",
        };
        vm.Validate().Should().Contain(e => e.Contains("Region ID, if provided, must be a positive integer"));
    }

    [Fact]
    public void BuildStation_OptionalRegionFilled_ReturnsRecordWithValues()
    {
        var vm = new AddStationDialogViewModel
        {
            StationIdText = "60003760",
            SystemName = "Jita",
            StationName = "IV - Moon 4",
            RegionIdText = "10000002",
            RegionName = "The Forge",
        };
        var station = vm.BuildStation();
        station.RegionId.Should().Be(10000002L);
        station.RegionName.Should().Be("The Forge");
    }

    [Fact]
    public void Validate_AllInvalid_ReturnsAllErrors()
    {
        var vm = new AddStationDialogViewModel(); // empty
        var errors = vm.Validate();
        errors.Should().Contain(e => e.Contains("Station ID"));
        errors.Should().Contain(e => e.Contains("System name"));
        errors.Should().Contain(e => e.Contains("Station name"));
        errors.Should().HaveCount(3);
    }
}
