namespace EveDashboard.Core.Sessions;

using EveDashboard.Core.Domain;

/// <summary>
/// Pure sort over a discovered-pilot list for the fleet-builder picker
/// (plan 42). Stateless and I/O-free, so it is unit-testable in isolation.
/// </summary>
public static class PilotSorter
{
    /// <summary>
    /// Return <paramref name="baselineNewestFirst"/> reordered per
    /// <paramref name="order"/>.
    /// </summary>
    /// <param name="baselineNewestFirst">
    /// The discovery-order list — already gamelog-mtime descending, i.e.
    /// most-recently-active first. This is the same order the live pilot list
    /// uses, so <see cref="PilotSortOrder.LastActiveNewest"/> means exactly what
    /// it does there.
    /// </param>
    /// <param name="order">Desired sort order.</param>
    /// <returns>A new list in the requested order (the input is not mutated).</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="baselineNewestFirst"/> is null.</exception>
    public static IReadOnlyList<Character> Sort(
        IReadOnlyList<Character> baselineNewestFirst,
        PilotSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(baselineNewestFirst);

        return order switch
        {
            PilotSortOrder.LastActiveNewest => baselineNewestFirst.ToList(),
            PilotSortOrder.LastActiveOldest => baselineNewestFirst.Reverse().ToList(),
            PilotSortOrder.NameAsc => baselineNewestFirst
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            PilotSortOrder.NameDesc => baselineNewestFirst
                .OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown PilotSortOrder enum value."),
        };
    }
}
