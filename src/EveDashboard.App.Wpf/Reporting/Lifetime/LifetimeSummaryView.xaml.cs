namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.Windows;
using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Code-behind for the lifetime rollup widget hosted by the Lifetime Totals
/// window. Static columns declared in XAML; forwards wheel-scroll past the
/// refined-output DataGrid to the window's outer ScrollViewer, shared with the
/// compare/per-ore widgets via <see cref="DataGridWheelForwarder"/>. Column-width
/// persistence is wired on the refined table (per type, key <c>lifetime.refined</c>).
/// </summary>
public partial class LifetimeSummaryView : WidgetBase
{
    private DataGridColumnWidthBinder? _widthBinder;

    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public LifetimeSummaryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_widthBinder is null && DataContext is IColumnWidthStore store)
        {
            _widthBinder = await DataGridColumnWidthBinder.AttachAsync(RefinedGrid, "lifetime.refined", store);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widthBinder?.Detach();
        _widthBinder = null;
    }

    /// <summary>Forwards wheel-scroll past the refined-output DataGrid to the Reports outer ScrollViewer at boundary / no-content.</summary>
    private void RefinedGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
