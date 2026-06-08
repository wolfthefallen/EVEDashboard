namespace EveDashboard.Reporting.Compare;

/// <summary>
/// Tracks an ordered list of selected items, preserving insertion order across
/// add/remove deltas. Used by the Reports sidebar to keep "first Ctrl-clicked
/// = S1" semantics in compare mode.
/// </summary>
/// <remarks>
/// The tracker is purely a list-shape algorithm — no equality customisation,
/// no thread-safety, no INotifyPropertyChanged. Callers feed it the WPF
/// <c>SelectionChangedEventArgs.AddedItems</c> / <c>RemovedItems</c> deltas
/// and read back the current order via <see cref="SelectedInOrder"/>.
/// </remarks>
/// <typeparam name="T">Tracked item type. Must be non-null. Equality is whatever <typeparamref name="T"/> defines.</typeparam>
public sealed class SelectionOrderTracker<T> where T : notnull
{
    private readonly List<T> _order = new();

    /// <summary>The selected items in click order; updated on each <see cref="Apply"/> call.</summary>
    public IReadOnlyList<T> SelectedInOrder => _order;

    /// <summary>
    /// Apply a selection delta. Removes happen first (preserving relative
    /// order of unchanged items); then adds are appended in the order they
    /// appear in <paramref name="added"/>. Duplicate adds (an item already
    /// tracked) are no-ops, so the method is idempotent on re-applied deltas.
    /// </summary>
    /// <param name="added">Items newly selected this delta. Iteration order is preserved as append order.</param>
    /// <param name="removed">Items newly de-selected this delta.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="added"/> or <paramref name="removed"/> is null.</exception>
    public void Apply(IEnumerable<T> added, IEnumerable<T> removed)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);
        foreach (var r in removed) _order.Remove(r);
        foreach (var a in added)
        {
            if (!_order.Contains(a)) _order.Add(a);
        }
    }

    /// <summary>Reset the tracker to empty. Used when the entire selection is being cleared programmatically (e.g. after a session delete).</summary>
    public void Clear() => _order.Clear();
}
