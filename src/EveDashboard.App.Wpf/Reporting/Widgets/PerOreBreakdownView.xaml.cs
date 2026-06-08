namespace EveDashboard.App.Wpf.Reporting.Widgets;

using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>Code-behind for the PerOreBreakdown widget. The wheel-forwarding handler delegates to <see cref="DataGridWheelForwarder"/> (shared with the compare-mode views).</summary>
public partial class PerOreBreakdownView : WidgetBase
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public PerOreBreakdownView()
    {
        InitializeComponent();
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
