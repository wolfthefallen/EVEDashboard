namespace EveDashboard.App.Wpf.Widgets.Session;

using EveDashboard.App.Wpf.Widgets;

/// <summary>
/// Code-behind for <see cref="SessionStatsView"/>. Inherits
/// <see cref="WidgetBase"/> so the chrome (border + Padding + LastUpdated
/// row + <c>x</c> close button) comes from <c>Themes/Generic.xaml</c>.
/// DataContext is set by the parent <c>ItemsControl</c>'s implicit
/// DataTemplate to a <see cref="SessionStatsViewModel"/>.
/// </summary>
public partial class SessionStatsView : WidgetBase
{
    /// <summary>Construct the view.</summary>
    public SessionStatsView()
    {
        InitializeComponent();
    }
}
