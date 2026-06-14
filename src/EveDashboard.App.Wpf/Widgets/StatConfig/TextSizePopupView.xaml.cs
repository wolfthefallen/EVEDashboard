namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Windows.Controls;

/// <summary>
/// Plan 47: code-behind for the Reports "Text size" popup view. Container has no
/// logic of its own — every interaction binds to <see cref="TextSizePopupViewModel"/>.
/// On <c>Loaded</c> we kick the popup VM's <c>LoadAsync</c> so the working-copy
/// rows are populated each time the popup opens (mirrors
/// <see cref="StatConfigPopupView"/>).
/// </summary>
public partial class TextSizePopupView : UserControl
{
    /// <summary>Construct the view; calls XAML <c>InitializeComponent</c>.</summary>
    public TextSizePopupView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is TextSizePopupViewModel vm)
            {
                _ = vm.LoadAsync();
            }
        };
    }
}
