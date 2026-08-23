using EveDashboard.Stats.Historical;
using FluentAssertions;

namespace EveDashboard.Stats.Tests.Historical;

public class SessionTimelineSynthesizerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    private static DateTimeOffset M(int minutes) => T0.AddMinutes(minutes);
    private static TimeInterval I(int start, int stop) => new(M(start), M(stop));

    private static IReadOnlyList<(int Start, int Stop, bool Pause)> Minutes(IReadOnlyList<SessionTimeRowSpec> rows) =>
        rows.Select(r => ((int)(r.Start - T0).TotalMinutes, (int)(r.Stop - T0).TotalMinutes, r.IsPause)).ToList();

    [Fact]
    public void Build_with_nothing_tracked_and_no_events_is_one_pause_over_the_whole_window()
    {
        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), Array.Empty<DateTimeOffset>(), Idle);

        Minutes(rows).Should().Equal((0, 60, true));
    }

    [Fact]
    public void Build_is_paused_until_the_first_event_active_through_idle_after_the_last_then_paused()
    {
        var events = new[] { M(10), M(12), M(14) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), events, Idle);

        Minutes(rows).Should().Equal((0, 10, true), (10, 19, false), (19, 60, true));
    }

    [Fact]
    public void Build_synthesises_a_pause_after_a_gap_longer_than_idle_starting_idle_after_the_last_event()
    {
        var events = new[] { M(10), M(12), M(30) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), events, Idle);

        Minutes(rows).Should().Equal((0, 10, true), (10, 17, false), (17, 30, true), (30, 35, false), (35, 60, true));
    }

    [Fact]
    public void Build_does_not_split_on_a_gap_equal_to_idle()
    {
        var events = new[] { M(10), M(15) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), events, Idle);

        Minutes(rows).Should().Equal((0, 10, true), (10, 20, false), (20, 60, true));
    }

    [Fact]
    public void Build_clips_the_trailing_active_run_at_the_window_stop()
    {
        var events = new[] { M(58) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), events, Idle);

        Minutes(rows).Should().Equal((0, 58, true), (58, 60, false));
    }

    [Fact]
    public void Build_uses_a_merged_source_rows_for_its_span_and_the_idle_rule_only_in_the_gaps()
    {
        var covered = new[] { I(20, 40) };
        var coveredActive = new[] { I(20, 30), I(35, 40) };       // the source paused 30..35
        var events = new[] { M(5), M(25), M(50) };                 // 25 lies inside the source span: ignored

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), covered, coveredActive, events, Idle);

        Minutes(rows).Should().Equal(
            (0, 5, true), (5, 10, false), (10, 20, true),
            (20, 30, false), (30, 35, true), (35, 40, false),
            (40, 50, true), (50, 55, false), (55, 60, true));
    }

    [Fact]
    public void Build_unions_the_active_time_of_two_concurrent_sources()
    {
        var covered = new[] { I(10, 30), I(20, 40) };
        var coveredActive = new[] { I(10, 30), I(20, 40) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), covered, coveredActive, Array.Empty<DateTimeOffset>(), Idle);

        Minutes(rows).Should().Equal((0, 10, true), (10, 40, false), (40, 60, true));
        rows.Where(r => !r.IsPause).Sum(r => (r.Stop - r.Start).TotalMinutes).Should().Be(30, "overlapping sources must not double count wall-clock time");
    }

    [Fact]
    public void Build_ignores_events_outside_the_window()
    {
        var events = new[] { M(-5), M(65) };

        var rows = SessionTimelineSynthesizer.Build(M(0), M(60), Array.Empty<TimeInterval>(), Array.Empty<TimeInterval>(), events, Idle);

        Minutes(rows).Should().Equal((0, 60, true));
    }

    [Fact]
    public void Build_rejects_bad_input()
    {
        var empty = Array.Empty<TimeInterval>();
        var a = () => SessionTimelineSynthesizer.Build(M(10), M(0), empty, empty, Array.Empty<DateTimeOffset>(), Idle);
        var b = () => SessionTimelineSynthesizer.Build(M(0), M(10), empty, empty, Array.Empty<DateTimeOffset>(), TimeSpan.Zero);
        var c = () => SessionTimelineSynthesizer.Build(M(0), M(10), null!, empty, Array.Empty<DateTimeOffset>(), Idle);

        a.Should().Throw<ArgumentException>();
        b.Should().Throw<ArgumentException>();
        c.Should().Throw<ArgumentNullException>();
    }
}
