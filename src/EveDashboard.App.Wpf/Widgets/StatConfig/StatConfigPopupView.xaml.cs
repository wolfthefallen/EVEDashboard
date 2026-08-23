namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Windows.Controls;

/// <summary>
/// Code-behind for the stat-config popup view. Container has no logic
/// of its own — every interaction binds to <see cref="StatConfigPopupViewModel"/>.
/// On first <c>Loaded</c> we kick the popup VM's <c>LoadAsync</c> so the
/// working copies are populated when the popup opens.
/// </summary>
public partial class StatConfigPopupView : UserControl
{
    /// <summary>Construct the view; call <c>InitializeComponent</c> for XAML.</summary>
    public StatConfigPopupView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is StatConfigPopupViewModel vm)
            {
                _ = vm.LoadAsync();
            }
        };
    }
}
