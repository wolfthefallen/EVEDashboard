using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Sessions.Historical;

public class PilotRoleHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static SessionSpan Span(string id, int startMin, params SessionSpanPilot[] pilots) =>
        new(id, "Session " + id, T0.AddMinutes(startMin), T0.AddMinutes(startMin + 30), pilots);

    private static SessionSpanPilot Pilot(long id, PilotRole role) => new(id, "Pilot " + id, role);

    [Fact]
    public void No_spans_resolves_to_nothing()
    {
        PilotRoleHistory.Resolve(Array.Empty<SessionSpan>()).Should().BeEmpty();
    }

    [Fact]
    public void A_single_span_gives_each_pilot_its_role()
    {
        var spans = new[] { Span("a", 0, Pilot(1, PilotRole.Miner), Pilot(2, PilotRole.Booster)) };

        var roles = PilotRoleHistory.Resolve(spans);

        roles[1].Should().Be(PilotRole.Miner);
        roles[2].Should().Be(PilotRole.Booster);
    }

    [Fact]
    public void The_latest_session_wins()
    {
        var spans = new[]
        {
            Span("old", 0, Pilot(1, PilotRole.Miner)),
            Span("new", 100, Pilot(1, PilotRole.Booster)),
        };

        PilotRoleHistory.Resolve(spans)[1].Should().Be(PilotRole.Booster);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var newer = Span("new", 100, Pilot(1, PilotRole.Cyno));
        var older = Span("old", 0, Pilot(1, PilotRole.Miner));

        PilotRoleHistory.Resolve(new[] { newer, older })[1].Should().Be(PilotRole.Cyno);
        PilotRoleHistory.Resolve(new[] { older, newer })[1].Should().Be(PilotRole.Cyno);
    }

    [Fact]
    public void A_tie_on_start_time_is_broken_deterministically_by_session_id()
    {
        var a = Span("aaa", 0, Pilot(1, PilotRole.Miner));
        var b = Span("bbb", 0, Pilot(1, PilotRole.Combat));

        PilotRoleHistory.Resolve(new[] { a, b })[1].Should().Be(PilotRole.Combat);
        PilotRoleHistory.Resolve(new[] { b, a })[1].Should().Be(
            PilotRole.Combat, "the same set of spans must always give the same answer");
    }

    [Fact]
    public void Pilots_are_tracked_independently()
    {
        var spans = new[]
        {
            Span("a", 0, Pilot(1, PilotRole.Booster), Pilot(2, PilotRole.Miner)),
            Span("b", 100, Pilot(2, PilotRole.Cyno)),
        };

        var roles = PilotRoleHistory.Resolve(spans);

        roles[1].Should().Be(PilotRole.Booster, "pilot 1's latest appearance is still the earlier session");
        roles[2].Should().Be(PilotRole.Cyno);
    }

    [Fact]
    public void An_unknown_pilot_is_absent_so_the_caller_can_default_it()
    {
        var roles = PilotRoleHistory.Resolve(new[] { Span("a", 0, Pilot(1, PilotRole.Miner)) });

        roles.ContainsKey(99).Should().BeFalse();
    }

    [Fact]
    public void Null_spans_throws()
    {
        var act = () => PilotRoleHistory.Resolve(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
