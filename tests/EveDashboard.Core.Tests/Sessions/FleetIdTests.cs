namespace EveDashboard.Core.Tests.Sessions;

using EveDashboard.Core.Sessions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for <see cref="FleetId"/> — the content-addressed fleet identity helper.
/// The recipe is lowercase-hex SHA-256 over the name plus each member (sorted by
/// pilot id ascending) as <c>0x1F + pilotId + ":" + canonicalRole</c>. These lock
/// down determinism, format, order-independence, and the name/role sensitivities
/// that distinguish one fleet from another.
/// </summary>
public class FleetIdTests
{
    private static IReadOnlyList<(long PilotId, PilotRole Role)> SampleMembers() => new[]
    {
        (101L, PilotRole.Miner),
        (202L, PilotRole.Booster),
        (303L, PilotRole.Cyno),
    };

    [Fact]
    public void Compute_is_deterministic()
    {
        var a = FleetId.Compute("KDG-TA Belt Run", SampleMembers());
        var b = FleetId.Compute("KDG-TA Belt Run", SampleMembers());
        a.Should().Be(b);
    }

    [Fact]
    public void Compute_returns_64_char_lowercase_hex()
    {
        var id = FleetId.Compute("KDG-TA Belt Run", SampleMembers());
        id.Should().HaveLength(64);
        id.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_is_order_independent()
    {
        var ascending = new[]
        {
            (101L, PilotRole.Miner),
            (202L, PilotRole.Booster),
            (303L, PilotRole.Cyno),
        };
        var shuffled = new[]
        {
            (303L, PilotRole.Cyno),
            (101L, PilotRole.Miner),
            (202L, PilotRole.Booster),
        };

        FleetId.Compute("KDG-TA Belt Run", ascending)
            .Should().Be(FleetId.Compute("KDG-TA Belt Run", shuffled));
    }

    [Fact]
    public void Compute_is_role_sensitive()
    {
        var original = new[]
        {
            (101L, PilotRole.Miner),
            (202L, PilotRole.Booster),
            (303L, PilotRole.Cyno),
        };
        var roleChanged = new[]
        {
            (101L, PilotRole.Miner),
            (202L, PilotRole.Combat), // 202's role changed Booster -> Combat
            (303L, PilotRole.Cyno),
        };

        FleetId.Compute("KDG-TA Belt Run", original)
            .Should().NotBe(FleetId.Compute("KDG-TA Belt Run", roleChanged));
    }

    [Fact]
    public void Compute_is_name_sensitive()
    {
        FleetId.Compute("KDG-TA Belt Run", SampleMembers())
            .Should().NotBe(FleetId.Compute("PT-21c Belt Run", SampleMembers()));
    }

    [Fact]
    public void Compute_throws_on_null_name()
    {
        var act = () => FleetId.Compute(null!, SampleMembers());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_throws_on_null_members()
    {
        var act = () => FleetId.Compute("KDG-TA Belt Run", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
