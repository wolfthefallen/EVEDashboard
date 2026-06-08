namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Widgets.Session;
using FluentAssertions;

/// <summary>
/// Plan 37 — pure-logic tests for the internal idle-decision helper
/// <see cref="SessionController.ShouldAutoPause"/>. Exposed via
/// <c>InternalsVisibleTo</c> so the test project can drive it without
/// instantiating a SessionController.
/// </summary>
public class SessionControllerIdleDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Returns_false_when_lastQualifying_is_null()
    {
        SessionController.ShouldAutoPause(null, Now, thresholdMinutes: 5)
            .Should().BeFalse("a session with no qualifying event yet must never auto-pause");
    }

    [Fact]
    public void Returns_false_when_now_minus_last_below_threshold()
    {
        var last = Now - TimeSpan.FromMinutes(4);
        SessionController.ShouldAutoPause(last, Now, thresholdMinutes: 5).Should().BeFalse();
    }

    [Fact]
    public void Returns_true_when_now_minus_last_equals_threshold()
    {
        var last = Now - TimeSpan.FromMinutes(5);
        SessionController.ShouldAutoPause(last, Now, thresholdMinutes: 5)
            .Should().BeTrue("the threshold is inclusive (>=)");
    }

    [Fact]
    public void Returns_true_when_now_minus_last_above_threshold()
    {
        var last = Now - TimeSpan.FromMinutes(7);
        SessionController.ShouldAutoPause(last, Now, thresholdMinutes: 5).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void Threshold_is_interpreted_as_minutes(int thresholdMinutes)
    {
        // Sanity check: a span just under thresholdMinutes is below; just over is above.
        var underLast = Now - TimeSpan.FromMinutes(thresholdMinutes).Subtract(TimeSpan.FromSeconds(1));
        var overLast  = Now - TimeSpan.FromMinutes(thresholdMinutes).Add(TimeSpan.FromSeconds(1));

        SessionController.ShouldAutoPause(underLast, Now, thresholdMinutes).Should().BeFalse();
        SessionController.ShouldAutoPause(overLast,  Now, thresholdMinutes).Should().BeTrue();
    }

    [Fact]
    public void Returns_false_when_lastQualifying_is_in_the_future()
    {
        // Defensive: clock skew or a buggy reset. The decision should never
        // auto-pause for a "negative elapsed" interval.
        var future = Now + TimeSpan.FromMinutes(2);
        SessionController.ShouldAutoPause(future, Now, thresholdMinutes: 5).Should().BeFalse();
    }
}
