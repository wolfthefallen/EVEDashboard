using EveDashboard.Core.Domain;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Domain;

public class PauseWindowTests
{
    private static DateTimeOffset Ts(int hour, int minute, int second = 0) =>
        new(2026, 4, 29, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public void EndBeforeStart_Throws()
    {
        var start = Ts(18, 30);
        var end = Ts(18, 29);
        Action act = () => _ = new PauseWindow(start, end);
        act.Should().Throw<ArgumentException>().WithParameterName("end");
    }

    [Fact]
    public void EndEqualsStart_IsAllowed_DurationZero()
    {
        var t = Ts(18, 30);
        var w = new PauseWindow(t, t);
        w.Duration.Should().Be(TimeSpan.Zero);
        w.Contains(t).Should().BeTrue();
    }

    [Fact]
    public void Contains_IsInclusiveOnBothEnds()
    {
        var start = Ts(18, 30);
        var end = Ts(18, 45);
        var w = new PauseWindow(start, end);

        w.Contains(start).Should().BeTrue();
        w.Contains(end).Should().BeTrue();
        w.Contains(start.AddSeconds(1)).Should().BeTrue();
        w.Contains(start.AddSeconds(-1)).Should().BeFalse();
        w.Contains(end.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Duration_IsEndMinusStart()
    {
        var w = new PauseWindow(Ts(18, 30), Ts(18, 45));
        w.Duration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void TwoPauseWindowsWithSameFields_AreEqual()
    {
        var a = new PauseWindow(Ts(18, 30), Ts(18, 45));
        var b = new PauseWindow(Ts(18, 30), Ts(18, 45));
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
