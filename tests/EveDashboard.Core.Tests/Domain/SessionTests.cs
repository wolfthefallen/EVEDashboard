using EveDashboard.Core.Domain;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Domain;

public class SessionTests
{
    private static DateTimeOffset Ts(int hour, int minute, int second = 0) =>
        new(2026, 4, 29, hour, minute, second, TimeSpan.Zero);

    private static Session NewSession(
        DateTimeOffset start,
        DateTimeOffset? end = null,
        IReadOnlyList<PauseWindow>? pauses = null,
        DateTimeOffset? currentPauseStart = null,
        IReadOnlyList<Character>? characters = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "test-session",
            "test-notes",
            characters ?? Array.Empty<Character>(),
            start,
            end,
            pauses ?? Array.Empty<PauseWindow>(),
            currentPauseStart);

    [Fact]
    public void TwoSessionsWithSameFields_AreEqual()
    {
        var id = Guid.NewGuid();
        var chars = new[] { new Character(1L, "A", "p1") };
        var pauses = new[] { new PauseWindow(Ts(18, 35), Ts(18, 40)) };

        var a = new Session(id, "n", "notes", chars, Ts(18, 30), Ts(19, 30), pauses, null);
        var b = new Session(id, "n", "notes", chars, Ts(18, 30), Ts(19, 30), pauses, null);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ActiveDuration_NoPauses_NoEndTime_IsAsOfMinusStart()
    {
        var s = NewSession(start: Ts(18, 30));
        s.ActiveDuration(Ts(19, 30)).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ActiveDuration_AsOfBeforeStart_IsZero()
    {
        var s = NewSession(start: Ts(18, 30));
        s.ActiveDuration(Ts(18, 29)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ActiveDuration_SubtractsFullyElapsedClosedPause()
    {
        var s = NewSession(
            start: Ts(18, 30),
            pauses: new[] { new PauseWindow(Ts(18, 40), Ts(18, 50)) });
        // 60 min wall - 10 min pause = 50 min active.
        s.ActiveDuration(Ts(19, 30)).Should().Be(TimeSpan.FromMinutes(50));
    }

    [Fact]
    public void ActiveDuration_SubtractsOpenPause_FromCurrentPauseStart()
    {
        var s = NewSession(
            start: Ts(18, 30),
            currentPauseStart: Ts(18, 50));
        // From 18:30 to 19:00 wall = 30 min; open pause 18:50 to 19:00 = 10 min; active = 20 min.
        s.ActiveDuration(Ts(19, 0)).Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void ActiveDuration_OpenPauseStartedAfterEndTime_IsIgnored()
    {
        var s = NewSession(
            start: Ts(18, 30),
            end: Ts(19, 0),
            currentPauseStart: Ts(19, 30));
        // EndTime caps endRef at 19:00; the open pause started at 19:30 — past endRef, contributes nothing.
        // 30 min wall, no pause subtracted, active = 30 min.
        s.ActiveDuration(Ts(20, 0)).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ActiveDuration_ClampsToEndTime_WhenAsOfIsLater()
    {
        var s = NewSession(
            start: Ts(18, 30),
            end: Ts(19, 0));
        // EndTime caps endRef. asOf = 21:00 should not extend duration past EndTime.
        s.ActiveDuration(Ts(21, 0)).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ActiveDuration_ClosedPauseExtendingPastEndRef_IsClipped()
    {
        // Pause starts inside the active range but extends past asOf.
        var s = NewSession(
            start: Ts(18, 30),
            pauses: new[] { new PauseWindow(Ts(18, 50), Ts(19, 30)) });
        // asOf = 19:00. Pause clipped to (18:50 -> 19:00) = 10 min.
        // Wall: 30 min, minus 10 min pause = 20 min.
        s.ActiveDuration(Ts(19, 0)).Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void PauseFlow_RoundTrip_ViaWithExpressions()
    {
        // Start with no pauses, no open pause.
        var initial = NewSession(start: Ts(18, 30));
        initial.PauseWindows.Should().BeEmpty();
        initial.CurrentPauseStart.Should().BeNull();

        // Pause: set CurrentPauseStart.
        var pauseStart = Ts(18, 50);
        var paused = initial with { CurrentPauseStart = pauseStart };
        paused.CurrentPauseStart.Should().Be(pauseStart);
        paused.PauseWindows.Should().BeEmpty();

        // Resume: append the new completed PauseWindow, clear CurrentPauseStart.
        var resumeAt = Ts(19, 0);
        var resumed = paused with
        {
            PauseWindows = new List<PauseWindow>([..paused.PauseWindows, new PauseWindow(paused.CurrentPauseStart!.Value, resumeAt)]),
            CurrentPauseStart = null,
        };

        resumed.CurrentPauseStart.Should().BeNull();
        resumed.PauseWindows.Should().HaveCount(1);
        resumed.PauseWindows[0].Start.Should().Be(pauseStart);
        resumed.PauseWindows[0].End.Should().Be(resumeAt);

        // Original `paused` instance is unchanged (records are immutable).
        paused.PauseWindows.Should().BeEmpty();
        paused.CurrentPauseStart.Should().Be(pauseStart);
    }
}
