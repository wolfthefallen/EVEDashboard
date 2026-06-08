namespace EveDashboard.App.Wpf.Widgets.Pilot;

/// <summary>
/// Code-behind for <see cref="PerPilotWidgetView"/>. The View is a straight
/// data-binding over <see cref="PerPilotWidgetViewModel"/>; the inherited
/// <see cref="WidgetBase"/> chrome (border + padding + LastUpdated line)
/// hosts the content via the XAML <c>ContentTemplate</c>.
/// </summary>
public partial class PerPilotWidgetView : WidgetBase
{
    /// <summary>Construct the view. DataContext is supplied by the parent ItemsControl's ItemTemplate.</summary>
    public PerPilotWidgetView()
    {
        InitializeComponent();
    }
}
