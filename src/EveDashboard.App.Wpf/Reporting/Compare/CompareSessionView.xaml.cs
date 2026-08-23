namespace EveDashboard.App.Wpf.Reporting.Compare;

using System.Windows;
using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Code-behind for the decluttered compare-mode widget. Static
/// columns (declared in XAML) — no dynamic column generation, no
/// DataContextChanged hook. Wheel-forwarding is shared with the single-mode
/// per-ore widget via <see cref="DataGridWheelForwarder"/>. Column-width
/// persistence is wired on all three grids (per type, keys
/// <c>compare.refined</c> / <c>compare.cumulative</c> / <c>compare.rates</c>).
/// </summary>
public partial class CompareSessionView : WidgetBase
{
    private readonly List<DataGridColumnWidthBinder> _widthBinders = new();

    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public CompareSessionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_widthBinders.Count != 0 || DataContext is not IColumnWidthStore store) return;
        _widthBinders.Add(await DataGridColumnWidthBinder.AttachAsync(RefinedGrid, "compare.refined", store));
        _widthBinders.Add(await DataGridColumnWidthBinder.AttachAsync(CumulativeGrid, "compare.cumulative", store));
        _widthBinders.Add(await DataGridColumnWidthBinder.AttachAsync(RatesGrid, "compare.rates", store));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (var binder in _widthBinders)
        {
            binder.Detach();
        }
        _widthBinders.Clear();
    }

    /// <summary>Forwards wheel-scroll past either DataGrid to the Reports outer ScrollViewer at boundary / no-content.</summary>
    private void CompareGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
