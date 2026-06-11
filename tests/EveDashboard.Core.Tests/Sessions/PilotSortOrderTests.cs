namespace EveDashboard.Core.Tests.Sessions;

using EveDashboard.Core.Sessions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for <see cref="PilotSortOrderExtensions"/> — the string round-trip and
/// display-label helpers for <see cref="PilotSortOrder"/>. The canonical strings
/// are the enum names verbatim and are the values persisted in the
/// <c>pilot_sort_order</c> setting, so these lock down the exact spellings, the
/// default-on-unknown behaviour, and a full round-trip across every enum value.
/// </summary>
public class PilotSortOrderTests
{
    [Theory]
    [InlineData(PilotSortOrder.LastActiveNewest, "LastActiveNewest")]
    [InlineData(PilotSortOrder.LastActiveOldest, "LastActiveOldest")]
    [InlineData(PilotSortOrder.NameAsc, "NameAsc")]
    [InlineData(PilotSortOrder.NameDesc, "NameDesc")]
    public void ToCanonicalString_returns_enum_name_verbatim(PilotSortOrder order, string expected)
    {
        PilotSortOrderExtensions.ToCanonicalString(order).Should().Be(expected);
    }

    [Theory]
    [InlineData("LastActiveNewest", PilotSortOrder.LastActiveNewest)]
    [InlineData("LastActiveOldest", PilotSortOrder.LastActiveOldest)]
    [InlineData("NameAsc", PilotSortOrder.NameAsc)]
    [InlineData("NameDesc", PilotSortOrder.NameDesc)]
    public void TryParse_returns_true_and_matching_value_for_canonical_string(
        string value, PilotSortOrder expected)
    {
        PilotSortOrderExtensions.TryParse(value, out var order).Should().BeTrue();
        order.Should().Be(expected);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("lastactivenewest")] // wrong case is not canonical
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_returns_false_and_default_for_unrecognised_value(string? value)
    {
        PilotSortOrderExtensions.TryParse(value, out var order).Should().BeFalse();
        order.Should().Be(PilotSortOrder.LastActiveNewest);
    }

    [Theory]
    [InlineData("LastActiveNewest", PilotSortOrder.LastActiveNewest)]
    [InlineData("LastActiveOldest", PilotSortOrder.LastActiveOldest)]
    [InlineData("NameAsc", PilotSortOrder.NameAsc)]
    [InlineData("NameDesc", PilotSortOrder.NameDesc)]
    public void Parse_returns_matching_order_for_canonical_string(
        string value, PilotSortOrder expected)
    {
        PilotSortOrderExtensions.Parse(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void Parse_returns_default_for_unknown_or_null(string? value)
    {
        PilotSortOrderExtensions.Parse(value).Should().Be(PilotSortOrder.LastActiveNewest);
    }

    [Theory]
    [InlineData(PilotSortOrder.LastActiveNewest, "Last active (newest)")]
    [InlineData(PilotSortOrder.LastActiveOldest, "Last active (oldest)")]
    [InlineData(PilotSortOrder.NameAsc, "Name (A-Z)")]
    [InlineData(PilotSortOrder.NameDesc, "Name (Z-A)")]
    public void DisplayLabel_returns_human_readable_label(PilotSortOrder order, string expected)
    {
        PilotSortOrderExtensions.DisplayLabel(order).Should().Be(expected);
    }

    [Theory]
    [InlineData(PilotSortOrder.LastActiveNewest)]
    [InlineData(PilotSortOrder.LastActiveOldest)]
    [InlineData(PilotSortOrder.NameAsc)]
    [InlineData(PilotSortOrder.NameDesc)]
    public void Canonical_string_round_trips_for_every_enum_value(PilotSortOrder value)
    {
        var canonical = PilotSortOrderExtensions.ToCanonicalString(value);
        PilotSortOrderExtensions.TryParse(canonical, out var parsed).Should().BeTrue();
        parsed.Should().Be(value);
    }
}
