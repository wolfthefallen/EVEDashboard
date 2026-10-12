using EveDashboard.Core.Sessions.Historical;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Sessions.Historical;

public class ActivityClustersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    private static DateTimeOffset M(int minutes) => T0.AddMinutes(minutes);

    private static IReadOnlyList<(int Start, int Stop)> Minutes(IReadOnlyList<TimeInterval> intervals) =>
        intervals.Select(i => ((int)(i.Start - T0).TotalMinutes, (int)(i.Stop - T0).TotalMinutes)).ToList();

    [Fact]
    public void No_events_yields_no_clusters()
    {
        ActivityClusters.FromEventTimes(Array.Empty<DateTimeOffset>(), Idle).Should().BeEmpty();
    }

    [Fact]
    public void One_event_runs_from_itself_until_idle_later()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10) }, Idle);

        Minutes(clusters).Should().Equal((10, 15));
    }

    [Fact]
    public void Events_closer_together_than_idle_stay_one_cluster()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(12), M(14) }, Idle);

        Minutes(clusters).Should().Equal((10, 19));
    }

    [Fact]
    public void A_gap_longer_than_idle_starts_a_new_cluster()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(12), M(40), M(42) }, Idle);

        Minutes(clusters).Should().Equal((10, 17), (40, 47));
    }

    [Fact]
    public void A_gap_exactly_idle_does_not_split()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(15) }, Idle);

        Minutes(clusters).Should().Equal((10, 20));
    }

    [Fact]
    public void Unsorted_input_is_ordered_first()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(42), M(10), M(40), M(12) }, Idle);

        Minutes(clusters).Should().Equal((10, 17), (40, 47));
    }

    [Fact]
    public void Duplicate_timestamps_are_harmless()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(10), M(12) }, Idle);

        Minutes(clusters).Should().Equal((10, 17));
    }

    [Fact]
    public void ClipStop_caps_the_final_cluster()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(12) }, Idle, clipStop: M(14));

        Minutes(clusters).Should().Equal((10, 14));
    }

    [Fact]
    public void ClipStop_leaves_a_final_cluster_that_already_ends_earlier_alone()
    {
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10) }, Idle, clipStop: M(60));

        Minutes(clusters).Should().Equal((10, 15));
    }

    [Fact]
    public void ClipStop_never_truncates_an_earlier_cluster()
    {
        // The second cluster's first event is more than idle after the first
        // cluster's last, so the first cluster's end always precedes it; the
        // ceiling only ever applies to the tail.
        var clusters = ActivityClusters.FromEventTimes(new[] { M(10), M(40) }, Idle, clipStop: M(42));

        Minutes(clusters).Should().Equal((10, 15), (40, 42));
    }

    [Fact]
    public void Null_events_throws()
    {
        var act = () => ActivityClusters.FromEventTimes(null!, Idle);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_idle_throws(int minutes)
    {
        var act = () => ActivityClusters.FromEventTimes(new[] { T0 }, TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentException>();
    }
}
