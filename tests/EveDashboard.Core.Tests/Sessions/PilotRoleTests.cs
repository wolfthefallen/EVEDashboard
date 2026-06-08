using EveDashboard.Core.Sessions;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Sessions;

public class PilotRoleTests
{
    [Theory]
    [InlineData("Miner",   PilotRole.Miner)]
    [InlineData("Booster", PilotRole.Booster)]
    [InlineData("Cyno",    PilotRole.Cyno)]
    [InlineData("Combat",  PilotRole.Combat)]
    public void Parse_returns_matching_enum_for_each_canonical_string(string raw, PilotRole expected)
    {
        PilotRoleExtensions.Parse(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(PilotRole.Miner,   "Miner")]
    [InlineData(PilotRole.Booster, "Booster")]
    [InlineData(PilotRole.Cyno,    "Cyno")]
    [InlineData(PilotRole.Combat,  "Combat")]
    public void ToCanonicalString_returns_matching_string_for_each_enum(PilotRole role, string expected)
    {
        PilotRoleExtensions.ToCanonicalString(role).Should().Be(expected);
    }

    [Theory]
    [InlineData("miner")]   // lowercase
    [InlineData("MINER")]   // uppercase
    [InlineData("MiNeR")]   // mixed
    [InlineData("Hauler")]  // unknown role
    [InlineData("")]        // empty
    [InlineData("  ")]      // whitespace
    public void Parse_throws_on_unknown_or_misspelled_string(string raw)
    {
        var act = () => PilotRoleExtensions.Parse(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_throws_on_null()
    {
        var act = () => PilotRoleExtensions.Parse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToCanonicalString_throws_on_undefined_enum_value()
    {
        var bogus = (PilotRole)999;
        var act = () => PilotRoleExtensions.ToCanonicalString(bogus);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PilotRole.Miner)]
    [InlineData(PilotRole.Booster)]
    [InlineData(PilotRole.Cyno)]
    [InlineData(PilotRole.Combat)]
    public void RoundTrip_via_ToCanonicalString_then_Parse_returns_original(PilotRole role)
    {
        var s = PilotRoleExtensions.ToCanonicalString(role);
        PilotRoleExtensions.Parse(s).Should().Be(role);
    }
}
