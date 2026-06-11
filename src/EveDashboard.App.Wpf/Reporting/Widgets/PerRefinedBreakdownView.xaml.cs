namespace EveDashboard.App.Wpf.Reporting.Widgets;

using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>Code-behind for the PerRefinedBreakdown widget (plan 43). The wheel-forwarding handler delegates to <see cref="DataGridWheelForwarder"/> (shared with the other Reports DataGrid views).</summary>
public partial class PerRefinedBreakdownView : WidgetBase
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public PerRefinedBreakdownView()
    {
        InitializeComponent();
    }

    /// <summary>Forward mouse-wheel events past the DataGrid's internal ScrollViewer to the parent, mirroring <see cref="PerOreBreakdownView"/>.</summary>
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
