namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Windows.Controls;

/// <summary>
/// Code-behind for the live widget body's "Refined output" section (plan 43).
/// Pure view; all state lives on the bound <see cref="StatConfigurableWidgetViewModel"/>.
/// </summary>
public partial class RefinedOutputSectionView : UserControl
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public RefinedOutputSectionView()
    {
        InitializeComponent();
    }
}
