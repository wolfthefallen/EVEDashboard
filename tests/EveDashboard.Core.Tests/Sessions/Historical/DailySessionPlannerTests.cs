using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Sessions.Historical;

public class DailySessionPlannerTests
{
    private const long PilotA = 1001L;
    private const long PilotB = 1002L;

    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset RangeStart = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeStop = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<long, PilotRole> NoRoles = new Dictionary<long, PilotRole>();
    private static readonly IReadOnlyDictionary<long, string> Names = new Dictionary<long, string>
    {
        [PilotA] = "Alice", [PilotB] = "Bob",
    };

    /// <summary>A timestamp on 2026-10-11 unless another day is named.</summary>
    private static DateTimeOffset At(int hour, int minute, int day = 11) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static MiningEvent Tick(long pilot, DateTimeOffset at) =>
        new(MiningEventKind.Tick, at, pilot, "Veldspar", 1230L, 100);

    private static MiningEvent Residue(long pilot, DateTimeOffset at) =>
        new(MiningEventKind.Residue, at, pilot, "Veldspar", 1230L, 7);

    private static SessionSpan Span(
        string id, DateTimeOffset start, DateTimeOffset stop, params SessionSpanPilot[] pilots) =>
        new(id, "Saved " + id, start, stop, pilots);

    private static SessionSpanPilot Pilot(long id, PilotRole role = PilotRole.Miner) =>
        new(id, "Pilot " + id, role);

    private static DailyPlanResult Plan(
        IEnumerable<MiningEvent> events,
        IEnumerable<SessionSpan>? spans = null,
        bool merge = false,
        IReadOnlyDictionary<long, PilotRole>? roles = null) =>
        DailySessionPlanner.Plan(
            events.ToList(),
            (spans ?? Array.Empty<SessionSpan>()).ToList(),
            roles ?? NoRoles,
            Names,
            RangeStart,
            RangeStop,
            Idle,
            merge);

    // ---- Days and windows ------------------------------------------------------

    [Fact]
    public void A_single_run_becomes_one_session_for_its_day()
    {
        var result = Plan(new[] { Tick(PilotA, At(14, 0)), Tick(PilotA, At(14, 2)) });

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Day.Should().Be(new DateOnly(2026, 10, 11));
        plan.WindowStart.Should().Be(At(14, 0), "the session starts when mining started");
        plan.WindowStop.Should().Be(At(14, 7), "and ends one idle threshold after the last ore");
        plan.Roster.Select(m => m.PilotId).Should().Equal(PilotA);
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Pilots_mining_the_same_day_share_one_session()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, At(14, 0)),
            Tick(PilotB, At(14, 1)),
            Tick(PilotA, At(14, 3)),
        });

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Roster.Select(m => m.PilotId).Should().Equal(PilotA, PilotB);
    }

    [Fact]
    public void A_run_crossing_midnight_stays_one_session_on_the_day_it_started()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, At(23, 56)),
            Tick(PilotA, At(23, 58)),
            Tick(PilotA, At(0, 1, day: 12)),
        });

        var plan = result.Plans.Should().ContainSingle(
            "a trip out is one session even when the clock rolls over").Subject;
        plan.Day.Should().Be(new DateOnly(2026, 10, 11));
        plan.WindowStart.Should().Be(At(23, 56));
        plan.WindowStop.Should().Be(At(0, 6, day: 12));
    }

    [Fact]
    public void Two_runs_on_one_day_share_a_session_spanning_both()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, At(9, 0)),
            Tick(PilotA, At(9, 2)),
            Tick(PilotA, At(20, 0)),
            Tick(PilotA, At(20, 2)),
        });

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.WindowStart.Should().Be(At(9, 0));
        plan.WindowStop.Should().Be(At(20, 7), "the long gap between trips becomes a pause, not a second session");
    }

    [Fact]
    public void Each_day_with_mining_gets_its_own_session()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, At(14, 0, day: 11)),
            Tick(PilotA, At(14, 0, day: 12)),
            Tick(PilotA, At(14, 0, day: 13)),
        });

        result.Plans.Select(p => p.Day).Should().Equal(
            new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13));
    }

    [Fact]
    public void Events_outside_the_range_are_ignored()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, RangeStart.AddHours(-1)),
            Tick(PilotA, RangeStop.AddHours(1)),
            Tick(PilotA, At(14, 0)),
        });

        result.Plans.Should().ContainSingle().Which.Day.Should().Be(new DateOnly(2026, 10, 11));
    }

    // ---- Rosters and roles -----------------------------------------------------

    [Fact]
    public void A_pilot_with_no_ore_is_left_off_the_roster_but_reported()
    {
        var result = Plan(new[]
        {
            Tick(PilotA, At(14, 0)),
            Residue(PilotB, At(14, 1)),
        });

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Roster.Select(m => m.PilotId).Should().Equal(PilotA);
        plan.PilotsWithNoOre.Should().Equal(PilotB);
    }

    [Fact]
    public void A_day_with_activity_but_no_ore_is_skipped()
    {
        var result = Plan(new[] { Residue(PilotA, At(14, 0)) });

        result.Plans.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("the logs hold activity but no ore was mined");
    }

    [Fact]
    public void Discovered_pilots_inherit_their_last_known_role()
    {
        var roles = new Dictionary<long, PilotRole> { [PilotA] = PilotRole.Booster };

        var result = Plan(new[] { Tick(PilotA, At(14, 0)) }, roles: roles);

        result.Plans.Should().ContainSingle()
            .Which.Roster.Should().ContainSingle()
            .Which.Role.Should().Be(PilotRole.Booster);
    }

    [Fact]
    public void A_pilot_with_no_history_defaults_to_miner()
    {
        var result = Plan(new[] { Tick(PilotA, At(14, 0)) });

        result.Plans.Should().ContainSingle()
            .Which.Roster.Should().ContainSingle()
            .Which.Role.Should().Be(PilotRole.Miner);
    }

    [Fact]
    public void Names_come_from_the_supplied_map()
    {
        var result = Plan(new[] { Tick(PilotA, At(14, 0)) });

        result.Plans.Should().ContainSingle()
            .Which.Roster.Should().ContainSingle()
            .Which.Name.Should().Be("Alice");
    }

    // ---- Merging and exclusion -------------------------------------------------

    [Fact]
    public void With_merging_on_a_same_day_session_is_folded_in_even_if_it_ended_before_the_untracked_mining()
    {
        // The session ran 13:00-13:30 and the untracked mining started at 14:00,
        // so they do not overlap at all. Folding in is a question about the DAY,
        // not about overlap: both are part of that day's mining.
        var saved = Span("s1", At(13, 0), At(13, 30), Pilot(PilotA));

        var result = Plan(new[] { Tick(PilotA, At(14, 0)) }, new[] { saved }, merge: true);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Merge.Select(s => s.SessionId).Should().Equal("s1");
        plan.Exclusions.Should().BeEmpty("a folded-in session's rows are copied, not excluded");
        plan.WindowStart.Should().Be(At(13, 0), "the window widens to cover what it absorbed");
    }

    [Fact]
    public void With_merging_off_an_overlapping_same_day_session_is_excluded_instead()
    {
        var saved = Span("s1", At(13, 55), At(14, 2), Pilot(PilotA));

        var result = Plan(
            new[] { Tick(PilotA, At(14, 0)), Tick(PilotA, At(15, 0)) },
            new[] { saved },
            merge: false);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Merge.Should().BeEmpty();
        plan.Exclusions.Should().ContainSingle().Which.Should().Match<PilotExclusion>(
            x => x.PilotId == PilotA && x.SessionId == "s1");
    }

    [Fact]
    public void With_merging_off_a_session_that_cannot_overlap_needs_no_exclusion()
    {
        var saved = Span("s1", At(13, 0), At(13, 30), Pilot(PilotA));

        var result = Plan(new[] { Tick(PilotA, At(14, 0)) }, new[] { saved }, merge: false);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Merge.Should().BeEmpty();
        plan.Exclusions.Should().BeEmpty(
            "nothing this day mined falls inside that session, so there is nothing to double count");
        plan.Events.Should().HaveCount(1);
    }

    [Fact]
    public void A_sessions_own_mining_is_dropped_from_the_day_that_declines_it()
    {
        var saved = Span("s1", At(13, 55), At(14, 30), Pilot(PilotA));

        var result = Plan(
            new[] { Tick(PilotA, At(14, 0)), Tick(PilotA, At(14, 2)), Tick(PilotA, At(15, 0)) },
            new[] { saved },
            merge: false);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Events.Select(e => e.EveTimestamp).Should().Equal(
            new[] { At(15, 0) },
            "the two ticks inside the declined session's span belong to it, not to this day");
        plan.WindowStart.Should().Be(At(15, 0), "the window follows what survived, never starting in dead air");
    }

    [Fact]
    public void A_session_straddling_midnight_is_merged_into_one_day_and_excluded_from_the_other()
    {
        // The saved session starts on day 11 and runs to 00:35 on day 12, so it
        // touches both days - exactly the shape that could be counted twice. Day
        // 12 mines both inside its span (00:30) and after it (00:50).
        var saved = Span("s1", At(23, 45), At(0, 35, day: 12), Pilot(PilotA));

        var result = Plan(
            new[]
            {
                Tick(PilotA, At(23, 50)),
                Tick(PilotA, At(23, 52)),
                Tick(PilotA, At(0, 30, day: 12)),
                Tick(PilotA, At(0, 32, day: 12)),
                Tick(PilotA, At(0, 50, day: 12)),
                Tick(PilotA, At(0, 52, day: 12)),
            },
            new[] { saved },
            merge: true);

        result.Plans.Should().HaveCount(2);
        var day11 = result.Plans[0];
        var day12 = result.Plans[1];

        day11.Merge.Select(s => s.SessionId).Should().Equal("s1");
        day12.Merge.Should().BeEmpty("a saved session may only ever be folded into one day");
        var savedEnd = At(0, 35, day: 12);
        day12.Exclusions.Should().ContainSingle().Which.Should().Match<PilotExclusion>(
            x => x.SessionId == "s1" && x.Stop == savedEnd, "the exclusion is clipped to the session's end");
        day12.Events.Select(e => e.EveTimestamp).Should().Equal(
            new[] { At(0, 50, day: 12), At(0, 52, day: 12) },
            "day 12 keeps only the mining that happened after the session day 11 absorbed");
        day12.WindowStart.Should().Be(At(0, 50, day: 12));
    }

    [Fact]
    public void A_session_is_never_merged_into_two_days_even_when_both_could_claim_it()
    {
        var saved = Span("s1", At(23, 50), At(0, 40, day: 12), Pilot(PilotA));

        var result = Plan(
            new[]
            {
                Tick(PilotA, At(23, 50)),
                Tick(PilotA, At(23, 52)),
                Tick(PilotA, At(0, 30, day: 12)),
            },
            new[] { saved },
            merge: true);

        result.Plans.SelectMany(p => p.Merge).Select(s => s.SessionId)
            .Should().Equal(new[] { "s1" }, "exactly one day folds it in");
    }

    [Fact]
    public void A_day_entirely_owned_by_a_declined_session_is_skipped_with_a_reason()
    {
        var saved = Span("s1", At(13, 0), At(16, 0), Pilot(PilotA));

        var result = Plan(
            new[] { Tick(PilotA, At(14, 0)), Tick(PilotA, At(14, 2)) },
            new[] { saved },
            merge: false);

        result.Plans.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("every pilot's mining that day already belongs to 'Saved s1'");
    }

    [Fact]
    public void A_merged_sessions_pilots_join_the_roster_with_the_role_it_recorded()
    {
        var saved = Span("s1", At(13, 0), At(13, 30), Pilot(PilotA), Pilot(PilotB, PilotRole.Cyno));
        var roles = new Dictionary<long, PilotRole> { [PilotB] = PilotRole.Miner };

        var result = Plan(new[] { Tick(PilotA, At(14, 0)) }, new[] { saved }, merge: true, roles: roles);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Roster.Select(m => m.PilotId).Should().Equal(
            new[] { PilotA, PilotB },
            "a pilot who was in the absorbed session belongs to the new one even without ore of their own");
        plan.Roster.Single(m => m.PilotId == PilotB).Role.Should().Be(PilotRole.Cyno,
            "the role the absorbed session actually used wins over the history");
    }

    [Fact]
    public void A_saved_session_sharing_no_pilot_is_ignored()
    {
        var saved = Span("s1", At(13, 0), At(16, 0), Pilot(9999L));

        var result = Plan(new[] { Tick(PilotA, At(14, 0)) }, new[] { saved }, merge: false);

        var plan = result.Plans.Should().ContainSingle().Subject;
        plan.Exclusions.Should().BeEmpty("it cannot double count anything of ours");
        plan.Events.Should().HaveCount(1);
    }

    // ---- Argument validation ---------------------------------------------------

    [Fact]
    public void Null_arguments_throw()
    {
        var events = new[] { Tick(PilotA, At(14, 0)) };

        var a = () => DailySessionPlanner.Plan(null!, Array.Empty<SessionSpan>(), NoRoles, Names, RangeStart, RangeStop, Idle, false);
        var b = () => DailySessionPlanner.Plan(events, null!, NoRoles, Names, RangeStart, RangeStop, Idle, false);
        var c = () => DailySessionPlanner.Plan(events, Array.Empty<SessionSpan>(), null!, Names, RangeStart, RangeStop, Idle, false);
        var d = () => DailySessionPlanner.Plan(events, Array.Empty<SessionSpan>(), NoRoles, null!, RangeStart, RangeStop, Idle, false);

        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
        d.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_inverted_range_throws()
    {
        var act = () => DailySessionPlanner.Plan(
            Array.Empty<MiningEvent>(), Array.Empty<SessionSpan>(), NoRoles, Names, RangeStop, RangeStart, Idle, false);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_non_positive_idle_throws()
    {
        var act = () => DailySessionPlanner.Plan(
            Array.Empty<MiningEvent>(), Array.Empty<SessionSpan>(), NoRoles, Names, RangeStart, RangeStop, TimeSpan.Zero, false);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void No_events_plans_nothing()
    {
        var result = Plan(Array.Empty<MiningEvent>());

        result.Plans.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
    }
}
