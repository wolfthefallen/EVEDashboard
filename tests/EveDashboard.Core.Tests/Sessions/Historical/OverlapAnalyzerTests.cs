using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Sessions.Historical;

public class OverlapAnalyzerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    private static FleetMember Member(long id, PilotRole role = PilotRole.Miner) => new(id, $"Pilot {id}", role);
    private static SessionSpanPilot Pilot(long id, PilotRole role = PilotRole.Miner) => new(id, $"Pilot {id}", role);

    private static SessionSpan Span(string id, int startMin, int stopMin, params SessionSpanPilot[] pilots) =>
        new(id, $"Session {id}", T0.AddMinutes(startMin), T0.AddMinutes(stopMin), pilots);

    private static readonly IReadOnlyList<FleetMember> Roster = new[] { Member(1), Member(2, PilotRole.Booster) };

    // ---- Analyze ----------------------------------------------------------------

    [Fact]
    public void Analyze_classifies_a_session_whose_pilots_are_all_in_the_roster_as_Full()
    {
        var sessions = new[] { Span("a", 10, 20, Pilot(1)) };

        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        var o = overlaps.Should().ContainSingle().Subject;
        o.Kind.Should().Be(FleetMatchKind.Full);
        o.SharedPilotIds.Should().Equal(1L);
        o.ExtraPilots.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_classifies_a_session_with_pilots_outside_the_roster_as_Partial_listing_the_extras()
    {
        var sessions = new[] { Span("a", 10, 20, Pilot(2, PilotRole.Booster), Pilot(9, PilotRole.Cyno)) };

        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        var o = overlaps.Should().ContainSingle().Subject;
        o.Kind.Should().Be(FleetMatchKind.Partial);
        o.SharedPilotIds.Should().Equal(2L);
        o.ExtraPilots.Should().ContainSingle().Which.Should().Be(Pilot(9, PilotRole.Cyno));
    }

    [Fact]
    public void Analyze_ignores_sessions_sharing_no_pilot_with_the_roster()
    {
        var sessions = new[] { Span("a", 10, 20, Pilot(7), Pilot(8)) };

        OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60)).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ignores_sessions_outside_the_window_but_keeps_those_touching_its_edges()
    {
        var sessions = new[]
        {
            Span("before",   -30, -10, Pilot(1)),
            Span("touchStart", -10, 0,  Pilot(1)),   // stops exactly at window start
            Span("inside",    10, 20,  Pilot(1)),
            Span("touchStop", 60, 70,  Pilot(1)),    // starts exactly at window stop
            Span("after",     61, 90,  Pilot(1)),
        };

        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        overlaps.Select(o => o.Session.SessionId).Should().Equal("touchStart", "inside", "touchStop");
    }

    [Fact]
    public void Analyze_orders_results_by_session_start()
    {
        var sessions = new[] { Span("late", 40, 50, Pilot(1)), Span("early", 5, 15, Pilot(2)) };

        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        overlaps.Select(o => o.Session.SessionId).Should().Equal("early", "late");
    }

    [Fact]
    public void Analyze_rejects_nulls()
    {
        var a = () => OverlapAnalyzer.Analyze(null!, Array.Empty<SessionSpan>(), T0, T0);
        var b = () => OverlapAnalyzer.Analyze(Roster, null!, T0, T0);
        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
    }

    // ---- ProposeUnionRoster -----------------------------------------------------

    [Fact]
    public void ProposeUnionRoster_appends_extra_pilots_with_their_session_role_and_keeps_roster_roles()
    {
        var sessions = new[] { Span("a", 10, 20, Pilot(2, PilotRole.Combat), Pilot(9, PilotRole.Cyno)) };
        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        var union = OverlapAnalyzer.ProposeUnionRoster(Roster, overlaps);

        union.Select(m => m.PilotId).Should().Equal(1L, 2L, 9L);
        union.Single(m => m.PilotId == 2).Role.Should().Be(PilotRole.Booster, "the picked roster's role wins for a shared pilot");
        union.Single(m => m.PilotId == 9).Role.Should().Be(PilotRole.Cyno, "an extra pilot carries the role the session recorded");
        union.Single(m => m.PilotId == 9).Name.Should().Be("Pilot 9");
    }

    [Fact]
    public void ProposeUnionRoster_does_not_duplicate_an_extra_pilot_seen_in_two_partials()
    {
        var sessions = new[]
        {
            Span("a", 10, 20, Pilot(1), Pilot(9, PilotRole.Cyno)),
            Span("b", 30, 40, Pilot(1), Pilot(9, PilotRole.Miner)),
        };
        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        var union = OverlapAnalyzer.ProposeUnionRoster(Roster, overlaps);

        union.Select(m => m.PilotId).Should().Equal(1L, 2L, 9L);
        union.Single(m => m.PilotId == 9).Role.Should().Be(PilotRole.Cyno, "first seen wins");
    }

    [Fact]
    public void ProposeUnionRoster_with_only_full_matches_returns_the_roster_unchanged()
    {
        var sessions = new[] { Span("a", 10, 20, Pilot(1)) };
        var overlaps = OverlapAnalyzer.Analyze(Roster, sessions, T0, T0.AddMinutes(60));

        OverlapAnalyzer.ProposeUnionRoster(Roster, overlaps).Should().Equal(Roster);
    }

    // ---- ComputeExclusions ------------------------------------------------------

    [Fact]
    public void ComputeExclusions_clips_each_declined_session_to_the_window_per_shared_pilot()
    {
        var declined = new[] { Span("a", -10, 20, Pilot(1), Pilot(2), Pilot(9)) };

        var exclusions = OverlapAnalyzer.ComputeExclusions(Roster, declined, T0, T0.AddMinutes(60));

        exclusions.Should().HaveCount(2, "pilot 9 is not in the roster so nothing of theirs is parsed anyway");
        exclusions.Select(e => e.PilotId).Should().Equal(1L, 2L);
        exclusions.Should().OnlyContain(e => e.Start == T0 && e.Stop == T0.AddMinutes(20) && e.SessionId == "a");
    }

    [Fact]
    public void ComputeExclusions_yields_nothing_for_a_declined_session_outside_the_window()
    {
        var declined = new[] { Span("a", 70, 80, Pilot(1)) };

        OverlapAnalyzer.ComputeExclusions(Roster, declined, T0, T0.AddMinutes(60)).Should().BeEmpty();
    }

    [Fact]
    public void ComputeExclusions_orders_by_pilot_then_start_across_several_sessions()
    {
        var declined = new[]
        {
            Span("late",  40, 50, Pilot(1), Pilot(2)),
            Span("early", 5,  15, Pilot(1)),
        };

        var exclusions = OverlapAnalyzer.ComputeExclusions(Roster, declined, T0, T0.AddMinutes(60));

        exclusions.Select(e => (e.PilotId, e.SessionId)).Should().Equal((1L, "early"), (1L, "late"), (2L, "late"));
    }

    [Fact]
    public void PilotExclusion_Covers_is_inclusive_on_both_ends()
    {
        var e = new PilotExclusion(1, T0, T0.AddMinutes(10), "a");

        e.Covers(T0).Should().BeTrue();
        e.Covers(T0.AddMinutes(10)).Should().BeTrue();
        e.Covers(T0.AddMinutes(5)).Should().BeTrue();
        e.Covers(T0.AddSeconds(-1)).Should().BeFalse();
        e.Covers(T0.AddMinutes(10).AddSeconds(1)).Should().BeFalse();
    }
}
