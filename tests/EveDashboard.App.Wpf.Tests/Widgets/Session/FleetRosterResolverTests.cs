namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using FluentAssertions;

/// <summary>
/// <see cref="FleetRosterResolver.Resolve"/> pairs a fleet roster
/// against a discovered-character snapshot at Start: matched
/// ids resolve to (Character, role); unmatched ids are skipped.
/// </summary>
public class FleetRosterResolverTests
{
    private static Character Char(long id, string name) =>
        new(id, name, $@"C:\logs\{id}.txt");

    [Fact]
    public void Resolve_all_discoverable_resolves_all_with_roles_and_skips_none()
    {
        var roster = new[]
        {
            new FleetMember(1, "Alice", PilotRole.Miner),
            new FleetMember(2, "Bob", PilotRole.Booster),
        };
        var discovered = new Dictionary<long, Character>
        {
            [1] = Char(1, "Alice"),
            [2] = Char(2, "Bob"),
        };

        var (resolved, skipped) = FleetRosterResolver.Resolve(roster, discovered);

        skipped.Should().BeEmpty();
        resolved.Should().HaveCount(2);
        resolved[0].Char.CharacterId.Should().Be(1);
        resolved[0].Role.Should().Be(PilotRole.Miner);
        resolved[1].Char.CharacterId.Should().Be(2);
        resolved[1].Role.Should().Be(PilotRole.Booster);
    }

    [Fact]
    public void Resolve_one_missing_skips_that_id_and_resolves_the_rest()
    {
        var roster = new[]
        {
            new FleetMember(1, "Alice", PilotRole.Miner),
            new FleetMember(2, "Bob", PilotRole.Cyno),
        };
        var discovered = new Dictionary<long, Character> { [1] = Char(1, "Alice") };

        var (resolved, skipped) = FleetRosterResolver.Resolve(roster, discovered);

        resolved.Should().ContainSingle().Which.Char.CharacterId.Should().Be(1);
        skipped.Should().ContainSingle().Which.Should().Be(2);
    }

    [Fact]
    public void Resolve_all_missing_resolves_empty_and_skips_all()
    {
        var roster = new[]
        {
            new FleetMember(1, "Alice", PilotRole.Miner),
            new FleetMember(2, "Bob", PilotRole.Combat),
        };
        var discovered = new Dictionary<long, Character>();

        var (resolved, skipped) = FleetRosterResolver.Resolve(roster, discovered);

        resolved.Should().BeEmpty("Start must block when no fleet pilot is discoverable");
        skipped.Should().Equal(1L, 2L);
    }

    [Fact]
    public void Resolve_uses_the_discovered_character_not_the_roster_name()
    {
        // The freshly-discovered name wins (the roster's Name is informational).
        var roster = new[] { new FleetMember(1, "Stale Name", PilotRole.Miner) };
        var discovered = new Dictionary<long, Character> { [1] = Char(1, "Fresh Name") };

        var (resolved, _) = FleetRosterResolver.Resolve(roster, discovered);

        resolved.Should().ContainSingle().Which.Char.Name.Should().Be("Fresh Name");
    }

    [Fact]
    public void Resolve_throws_on_null_roster()
    {
        Action act = () => FleetRosterResolver.Resolve(null!, new Dictionary<long, Character>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_throws_on_null_discovered()
    {
        Action act = () => FleetRosterResolver.Resolve(Array.Empty<FleetMember>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
