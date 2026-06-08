namespace EveDashboard.App.Wpf.Reporting.Compare;

using System.Windows.Input;
using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Plan 36: code-behind for the decluttered compare-mode widget. Static
/// columns (declared in XAML) — no dynamic column generation, no
/// DataContextChanged hook. Wheel-forwarding is shared with the single-mode
/// per-ore widget via <see cref="DataGridWheelForwarder"/>.
/// </summary>
public partial class CompareSessionView : WidgetBase
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public CompareSessionView()
    {
        InitializeComponent();
    }

    /// <summary>Forwards wheel-scroll past either DataGrid to the Reports outer ScrollViewer at boundary / no-content.</summary>
    private void CompareGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => DataGridWheelForwarder.Forward(sender, e);
}
