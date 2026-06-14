namespace EveDashboard.App.Wpf.Reporting;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Plan 47 (phase 1a-B): restores + persists one <see cref="DataGrid"/>'s column
/// widths for a grid TYPE (decision 2.1 = A / 3.1 = A). A view's code-behind
/// creates one binder per grid on <c>Loaded</c> and calls <see cref="Detach"/> on
/// <c>Unloaded</c>. On attach it restores the saved pixel widths onto the matching
/// columns; thereafter it watches each column's <see cref="DataGridColumn.Width"/>
/// for user resizes (debounced) and persists back ONLY the columns the user
/// actually sized (those whose width is a fixed <see cref="DataGridLengthUnitType.Pixel"/>),
/// so untouched <c>*</c> / <c>Auto</c> columns keep stretching.
/// </summary>
/// <remarks>
/// Capturing the column's <see cref="DataGridColumn.Width"/> (not its noisy
/// <c>ActualWidth</c>) means the change handler fires on a user drag (and on our
/// own restore, which is suppressed) but NOT on every layout pass. A short
/// debounce collapses the many Width updates during one drag-gesture into a
/// single save; <see cref="Detach"/> also takes a final snapshot so a resize made
/// right before the view unloads is not lost.
/// </remarks>
internal sealed class DataGridColumnWidthBinder
{
    private static readonly DependencyPropertyDescriptor WidthDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));

    private readonly DataGrid _grid;
    private readonly string _gridKey;
    private readonly IColumnWidthStore _store;
    private readonly DispatcherTimer _debounce;
    private readonly List<DataGridColumn> _watched = new();
    private bool _restoring;
    private bool _detached;

    private DataGridColumnWidthBinder(DataGrid grid, string gridKey, IColumnWidthStore store)
    {
        _grid = grid;
        _gridKey = gridKey;
        _store = store;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Persist();
        };
    }

    /// <summary>
    /// Restore the saved widths onto <paramref name="grid"/> for
    /// <paramref name="gridKey"/>, then start watching for user resizes.
    /// </summary>
    /// <param name="grid">The DataGrid to bind.</param>
    /// <param name="gridKey">Grid-type key (e.g. <c>"ore"</c>, <c>"compare.rates"</c>).</param>
    /// <param name="store">The VM that loads/saves widths.</param>
    /// <returns>The attached binder; call <see cref="Detach"/> on the view's <c>Unloaded</c>.</returns>
    public static async Task<DataGridColumnWidthBinder> AttachAsync(DataGrid grid, string gridKey, IColumnWidthStore store)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(gridKey);
        ArgumentNullException.ThrowIfNull(store);
        var binder = new DataGridColumnWidthBinder(grid, gridKey, store);
        await binder.RestoreAsync().ConfigureAwait(true);
        binder.WatchColumns();
        return binder;
    }

    private async Task RestoreAsync()
    {
        IReadOnlyDictionary<int, double>? saved = null;
        try
        {
            saved = await _store.LoadColumnWidthsAsync(_gridKey).ConfigureAwait(true);
        }
        catch
        {
            // Load failure is non-fatal — keep the XAML default widths.
        }

        if (saved is null || _detached) return;

        _restoring = true;
        try
        {
            foreach (var (index, px) in saved)
            {
                if (index >= 0 && index < _grid.Columns.Count && px > 0)
                {
                    _grid.Columns[index].Width = new DataGridLength(px);
                }
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    private void WatchColumns()
    {
        if (_detached) return;
        foreach (var column in _grid.Columns)
        {
            WidthDescriptor.AddValueChanged(column, OnColumnWidthChanged);
            _watched.Add(column);
        }
    }

    private void OnColumnWidthChanged(object? sender, EventArgs e)
    {
        // Ignore our own restore writes and any post-detach stragglers.
        if (_restoring || _detached) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Detach the watchers (no leaks) after a final snapshot. Idempotent.</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _debounce.Stop();
        Persist();
        foreach (var column in _watched)
        {
            WidthDescriptor.RemoveValueChanged(column, OnColumnWidthChanged);
        }
        _watched.Clear();
    }

    private void Persist()
    {
        try
        {
            var map = new Dictionary<int, double>();
            for (var i = 0; i < _grid.Columns.Count; i++)
            {
                var width = _grid.Columns[i].Width;
                if (width.UnitType == DataGridLengthUnitType.Pixel && width.Value > 0)
                {
                    map[i] = width.Value;
                }
            }
            _store.SaveColumnWidths(_gridKey, map);
        }
        catch
        {
            // Defensive: persistence is best-effort and must never break the UI.
        }
    }
}
