namespace EveDashboard.Reporting.Tests.Compare;

using EveDashboard.Reporting.Compare;
using FluentAssertions;

public sealed class SelectionOrderTrackerTests
{
    [Fact]
    public void Apply_AddOnly_PreservesAddOrder()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(added: new[] { "a", "b", "c" }, removed: Array.Empty<string>());
        t.SelectedInOrder.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Apply_RemoveFromMiddle_PreservesRemainingOrder()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(new[] { "a", "b", "c", "d" }, Array.Empty<string>());
        t.Apply(Array.Empty<string>(), new[] { "b" });
        t.SelectedInOrder.Should().Equal("a", "c", "d");
    }

    [Fact]
    public void Apply_RemoveThenAddInOneCall_RemoveFirst()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(new[] { "a", "b" }, Array.Empty<string>());
        t.Apply(added: new[] { "c" }, removed: new[] { "a" });
        t.SelectedInOrder.Should().Equal("b", "c");
    }

    [Fact]
    public void Apply_EmptyDeltas_OrderUnchanged()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(new[] { "a", "b" }, Array.Empty<string>());
        t.Apply(Array.Empty<string>(), Array.Empty<string>());
        t.SelectedInOrder.Should().Equal("a", "b");
    }

    [Fact]
    public void Apply_DuplicateAdd_Idempotent()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(new[] { "a" }, Array.Empty<string>());
        t.Apply(new[] { "a" }, Array.Empty<string>());
        t.SelectedInOrder.Should().Equal("a");
    }

    [Fact]
    public void Clear_EmptiesTracker()
    {
        var t = new SelectionOrderTracker<string>();
        t.Apply(new[] { "a", "b", "c" }, Array.Empty<string>());
        t.Clear();
        t.SelectedInOrder.Should().BeEmpty();
    }

    [Fact]
    public void Apply_NullAdded_Throws()
    {
        var t = new SelectionOrderTracker<string>();
        Action act = () => t.Apply(added: null!, removed: Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_NullRemoved_Throws()
    {
        var t = new SelectionOrderTracker<string>();
        Action act = () => t.Apply(added: Array.Empty<string>(), removed: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
