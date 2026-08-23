namespace EveDashboard.App.Wpf.Layout;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Core.Layout;

/// <summary>
/// One renderable entry in <see cref="LayoutCoordinator.Items"/>.
/// Wraps a typed <see cref="EveDashboard.Core.Layout.WidgetDescriptor"/>, the
/// matching VM (Session or per-pilot), the user's preferred display name
/// (rendered in the chrome popup's hidden-widget list), and an INPC-bindable
/// <see cref="IsHidden"/> flag.
/// </summary>
/// <remarks>
/// <para>
/// Items always represent widgets that are currently *renderable* — Session
/// (always present) plus the per-pilot widgets in the active session. Saved
/// "ghost" descriptors for pilots not currently in any session are not
/// materialised as items; they live only in the persisted JSON until the
/// pilot reappears in a session.
/// </para>
/// <para>
/// <see cref="IsHidden"/> being <c>true</c> does NOT remove the item from
/// the collection — it just collapses the View. The underlying VM continues
/// receiving stat refreshes from the per-pilot
/// coordinator while hidden, so re-showing the widget is instant with live
/// data.
/// </para>
/// </remarks>
public sealed partial class WidgetItem : ObservableObject
{
    /// <summary>Persisted identity of this widget. Immutable for the item's lifetime.</summary>
    public WidgetDescriptor Descriptor { get; }

    /// <summary>The bound VM rendered into the dashboard for this item. Either a <c>SessionController</c> or a <c>PerPilotWidgetViewModel</c>.</summary>
    public WidgetBaseViewModel ViewModel { get; }

    /// <summary>Display name used by the chrome popup's hidden-widget list. <c>"Session"</c> for the singleton, the pilot name for a per-pilot item.</summary>
    public string DisplayName { get; }

    /// <summary>If true, the View is collapsed in the dashboard. The VM continues receiving stat refreshes while hidden.</summary>
    [ObservableProperty]
    private bool _isHidden;

    /// <summary>Construct a layout item.</summary>
    /// <param name="descriptor">The persisted identity (Session or Pilot).</param>
    /// <param name="viewModel">The widget VM rendered for this item.</param>
    /// <param name="displayName">Human-readable name for the popup.</param>
    /// <param name="isHidden">Initial hidden state restored from persistence.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public WidgetItem(
        WidgetDescriptor descriptor,
        WidgetBaseViewModel viewModel,
        string displayName,
        bool isHidden = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(displayName);
        Descriptor = descriptor;
        ViewModel = viewModel;
        DisplayName = displayName;
        _isHidden = isHidden;
    }
}
