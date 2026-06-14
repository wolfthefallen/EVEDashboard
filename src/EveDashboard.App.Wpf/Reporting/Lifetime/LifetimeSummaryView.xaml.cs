namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.Windows.Input;
using EveDashboard.App.Wpf.Reporting.Compare;
using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Plan 46: code-behind for the empty-state lifetime rollup widget. Static
/// columns declared in XAML; the only behaviour is forwarding wheel-scroll past
/// the refined-output DataGrid to the Reports outer ScrollViewer, shared with the
/// compare/per-ore widgets via <see cref="DataGridWheelForwarder"/>.
/// </summary>
public partial class LifetimeSummaryView : WidgetBase
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public LifetimeSummaryView()
    {
        InitializeComponent();
    }

    /// <summary>Forwards wheel-scroll past the refined-output DataGrid to the Reports outer ScrollViewer at boundary / no-content.</summary>
    private void RefinedGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
