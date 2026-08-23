namespace EveDashboard.App.Wpf;

using EveDashboard.App.Wpf.Layout;
using EveDashboard.App.Wpf.Widgets.Session;

/// <summary>
/// Composition root for the MainWindow's bound state. Holds a single
/// <see cref="LayoutCoordinator"/> reference; the dashboard
/// flow binds to <see cref="Layout"/>'s <c>Items</c> / <c>HasHiddenWidgets</c>
/// / <c>HiddenItems</c>, and the chrome-bar buttons bind to
/// <see cref="Session"/> (a proxy that returns the coordinator's singleton
/// Session VM so all existing chrome-bar XAML keeps working unchanged).
/// </summary>
/// <remarks>
/// Trivial composition class; holds no logic. Widget lifecycle (Start /
/// Pause / Resume / Stop, the dirty-flag refresh, the per-session writer /
/// watcher orchestration, drag-and-drop reorder, hide/show) lives in the
/// underlying VMs and the coordinator. This VM only routes their public
/// surface to the View.
/// </remarks>
public sealed class MainWindowViewModel
{
    /// <summary>The dashboard layout coordinator. Bound by the MainWindow XAML for the unified flow + hidden-widgets popup.</summary>
    public LayoutCoordinator Layout { get; }

    /// <summary>Singleton Session widget VM proxy. Routes to <see cref="LayoutCoordinator.SessionViewModel"/> so the chrome-bar buttons (Start / Pause / Resume / Stop / New Session / etc) keep their existing <c>Session.&lt;Command&gt;</c> bindings unchanged.</summary>
    public SessionController Session => Layout.SessionViewModel;

    /// <summary>Construct the MainWindow VM.</summary>
    /// <param name="layout">The dashboard layout coordinator.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="layout"/> is null.</exception>
    public MainWindowViewModel(LayoutCoordinator layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
    }
}
