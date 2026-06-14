namespace EveDashboard.App.Wpf.Reporting.Widgets;

using System.Windows;
using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>Code-behind for the PerOreBreakdown widget. The wheel-forwarding handler delegates to <see cref="DataGridWheelForwarder"/> (shared with the compare-mode views); plan 47 wires column-width persistence via <see cref="DataGridColumnWidthBinder"/>.</summary>
public partial class PerOreBreakdownView : WidgetBase
{
    private DataGridColumnWidthBinder? _widthBinder;

    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public PerOreBreakdownView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Plan 47: restore + persist the ore-table column widths (per type, key "ore").
        if (_widthBinder is null && DataContext is IColumnWidthStore store)
        {
            _widthBinder = await DataGridColumnWidthBinder.AttachAsync(OreGrid, "ore", store);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widthBinder?.Detach();
        _widthBinder = null;
    }

    /// <summary>
    /// Forward mouse-wheel events past the DataGrid's internal ScrollViewer to
    /// the parent (the Reports window's outer ScrollViewer) when the inner
    /// viewer is at its scroll boundary in the wheel direction OR has nothing
    /// scrollable. Implementation is shared via <see cref="DataGridWheelForwarder"/>.
    /// </summary>
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
