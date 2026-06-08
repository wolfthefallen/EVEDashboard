using EveDashboard.Core.Domain;
using FluentAssertions;
using Xunit;

namespace EveDashboard.Core.Tests.Domain;

public class MiningEventTests
{
    private static DateTimeOffset Ts(int hour, int minute, int second = 0) =>
        new(2026, 4, 29, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public void TwoEventsWithSameFields_AreEqual()
    {
        var a = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        var b = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void EventsDifferingByKind_AreNotEqual()
    {
        var tick = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        var crit = new MiningEvent(MiningEventKind.Crit, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        tick.Should().NotBe(crit);
    }

    [Fact]
    public void EventsDifferingByAmount_AreNotEqual()
    {
        var a = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        var b = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 101L);
        a.Should().NotBe(b);
    }

    [Fact]
    public void EventsDifferingByTimestamp_AreNotEqual()
    {
        var a = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", 1230L, 100L);
        var b = new MiningEvent(MiningEventKind.Tick, Ts(18, 31), 95465499L, "Veldspar", 1230L, 100L);
        a.Should().NotBe(b);
    }

    [Fact]
    public void WithExpression_AssignsResolvedTypeId_FromNull()
    {
        // Parser emits the event with TypeId = null when SDE lookup is unresolved;
        // a downstream component (e.g. retry-resolver) can promote it via `with`.
        var unresolved = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "Veldspar", null, 100L);
        var resolved = unresolved with { TypeId = 1230L };

        unresolved.TypeId.Should().BeNull();
        resolved.TypeId.Should().Be(1230L);
        // Original instance is unchanged (records are immutable).
        unresolved.TypeId.Should().BeNull();
    }

    [Fact]
    public void TypeIdZero_IsPreservedAsRealValue_NotNull()
    {
        // Guard: 0 is the real EVE type ID for the #System placeholder.
        // It must NOT be conflated with "unresolved" (which is null).
        var ev = new MiningEvent(MiningEventKind.Tick, Ts(18, 30), 95465499L, "#System", 0L, 100L);
        ev.TypeId.Should().NotBeNull();
        ev.TypeId.Should().Be(0L);
    }

    [Fact]
    public void DepletedEvent_WithZeroAmount_Constructs()
    {
        // No validation fires: the Depleted kind always carries Amount = 0.
        var ev = new MiningEvent(MiningEventKind.Depleted, Ts(18, 30), 95465499L, "Veldspar", 1230L, 0L);
        ev.Amount.Should().Be(0L);
        ev.Kind.Should().Be(MiningEventKind.Depleted);
    }

    [Fact]
    public void EveTimestamp_RoundTripsWithUtcOffsetPreserved()
    {
        var t = new DateTimeOffset(2026, 4, 29, 18, 30, 15, TimeSpan.Zero);
        var ev = new MiningEvent(MiningEventKind.Tick, t, 95465499L, "Veldspar", 1230L, 100L);
        ev.EveTimestamp.Offset.Should().Be(TimeSpan.Zero);
        ev.EveTimestamp.Should().Be(t);
    }
}
