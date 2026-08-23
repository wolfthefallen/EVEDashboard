namespace EveDashboard.App.Wpf.Reporting.Widgets;

using System.Windows;
using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>Code-behind for the PerRefinedBreakdown widget. The wheel-forwarding handler delegates to <see cref="DataGridWheelForwarder"/> (shared with the other Reports DataGrid views); column-width persistence is wired via <see cref="DataGridColumnWidthBinder"/>.</summary>
public partial class PerRefinedBreakdownView : WidgetBase
{
    private DataGridColumnWidthBinder? _widthBinder;

    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public PerRefinedBreakdownView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Restore + persist the refined-table column widths (per type, key "refined").
        if (_widthBinder is null && DataContext is IColumnWidthStore store)
        {
            _widthBinder = await DataGridColumnWidthBinder.AttachAsync(RefinedGrid, "refined", store);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widthBinder?.Detach();
        _widthBinder = null;
    }

    /// <summary>Forward mouse-wheel events past the DataGrid's internal ScrollViewer to the parent, mirroring <see cref="PerOreBreakdownView"/>.</summary>
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
