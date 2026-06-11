namespace EveDashboard.Core.Tests.Sessions;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for <see cref="PilotSorter"/> — the pure reorder over the discovered-pilot
/// list. The baseline is treated as already "newest first", so
/// <see cref="PilotSortOrder.LastActiveNewest"/> is the baseline unchanged,
/// <see cref="PilotSortOrder.LastActiveOldest"/> is the baseline reversed, and the
/// Name orders sort case-insensitively. The input list must never be mutated.
/// </summary>
public class PilotSorterTests
{
    /// <summary>
    /// A 3-pilot baseline whose names are deliberately NOT alphabetical, so each
    /// sort order yields a distinguishable name sequence.
    /// </summary>
    private static IReadOnlyList<Character> Baseline() => new[]
    {
        new Character(1, "Charlie", @"C:\logs\charlie.txt"),
        new Character(2, "alice",   @"C:\logs\alice.txt"),
        new Character(3, "Bob",     @"C:\logs\bob.txt"),
    };

    [Fact]
    public void LastActiveNewest_returns_baseline_unchanged()
    {
        PilotSorter.Sort(Baseline(), PilotSortOrder.LastActiveNewest)
            .Select(c => c.Name)
            .Should().Equal("Charlie", "alice", "Bob");
    }

    [Fact]
    public void LastActiveOldest_returns_baseline_reversed()
    {
        PilotSorter.Sort(Baseline(), PilotSortOrder.LastActiveOldest)
            .Select(c => c.Name)
            .Should().Equal("Bob", "alice", "Charlie");
    }

    [Fact]
    public void NameAsc_sorts_case_insensitively_ascending()
    {
        PilotSorter.Sort(Baseline(), PilotSortOrder.NameAsc)
            .Select(c => c.Name)
            .Should().Equal("alice", "Bob", "Charlie");
    }

    [Fact]
    public void NameDesc_sorts_case_insensitively_descending()
    {
        PilotSorter.Sort(Baseline(), PilotSortOrder.NameDesc)
            .Select(c => c.Name)
            .Should().Equal("Charlie", "Bob", "alice");
    }

    [Fact]
    public void Sort_does_not_mutate_the_input_list()
    {
        var baseline = Baseline();

        // A sort that would visibly differ if it reordered in place.
        PilotSorter.Sort(baseline, PilotSortOrder.NameAsc);

        baseline.Select(c => c.Name).Should().Equal("Charlie", "alice", "Bob");
    }

    [Fact]
    public void Sort_throws_on_null_baseline()
    {
        var act = () => PilotSorter.Sort(null!, PilotSortOrder.LastActiveNewest);
        act.Should().Throw<ArgumentNullException>();
    }
}
