namespace EveDashboard.App.Wpf.Widgets;

using System.Windows;
using System.Windows.Controls;

/// <summary>
/// Templated chrome host for every widget in the dashboard flow. Concrete
/// widgets (e.g. <see cref="Session.SessionStatsView"/>,
/// <see cref="Pilot.PerPilotWidgetView"/>) inherit from this and set
/// <see cref="ContentControl.Content"/> to project their body into the
/// inherited chrome.
/// </summary>
/// <remarks>
/// <para>
/// Templated <see cref="ContentControl"/> per plan-17 amendment 8.4 = B
/// (templated <see cref="ContentControl"/>). The default <see cref="Control.Template"/>
/// lives in <c>Themes/Generic.xaml</c> and is auto-discovered by WPF via the
/// assembly-level <see cref="ThemeInfoAttribute"/> in <c>AssemblyInfo.cs</c>.
/// The template renders a border + Padding=10 + a <c>LastUpdated</c> chrome line
/// (collapsed when null) + a <see cref="ContentPresenter"/> for the derived view's body.
/// </para>
/// <para>
/// Bound DataContext is conventionally a <see cref="WidgetBaseViewModel"/> so the
/// chrome's <c>{Binding LastUpdated}</c> resolves; concrete widgets typically
/// extend that VM and add their own <c>[ObservableProperty]</c> fields.
/// </para>
/// <para>
/// XAML-based derivation from another XAML+code-behind UserControl trips
/// CS0108 under the project's <c>TreatWarningsAsErrors=true</c> flag (each
/// <c>.xaml</c> generates its own <c>InitializeComponent</c> and the derived
/// one "hides" the base). Switching the base to a code-only templated
/// <see cref="ContentControl"/> eliminates the collision because the base
/// has no XAML file -> no generated <c>InitializeComponent</c> -> nothing to
/// hide.
/// </para>
/// </remarks>
public class WidgetBase : ContentControl
{
    /// <summary>
    /// Static ctor pins the default-style key to <c>typeof(WidgetBase)</c> so
    /// derived widgets pick up the WidgetBase chrome from <c>Themes/Generic.xaml</c>
    /// without each having to declare their own <c>Style</c>. Derived widgets
    /// that want a per-type override can supply a <c>Style TargetType="{x:Type ...}"
    /// BasedOn="{StaticResource {x:Type widgets:WidgetBase}}"</c>.
    /// </summary>
    static WidgetBase()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WidgetBase),
            new FrameworkPropertyMetadata(typeof(WidgetBase)));
    }
}
