namespace EveDashboard.App.Wpf.About;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Plan 52: production <see cref="IAboutWindow"/> implementation. Holds a single
/// <see cref="AboutWindow"/> for the app lifetime; subsequent <see cref="Show"/>
/// calls re-show / bring-to-front the same instance so geometry survives
/// in-session open/close cycles.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EveDashboard.App.Wpf.CrystalDiagramWindowAdapter"/>'s shape
/// (service-locator resolve of the singleton window, lazy construction, Owner =
/// current MainWindow, <see cref="Window.Show"/> not <see cref="Window.ShowDialog"/>).
/// Unlike the Lifetime Totals adapter it does NOT refresh on Show: the About
/// content (version, SDE date, static text) is fixed for the session, loaded once
/// in the window's own <c>OnLoaded</c>.
/// </remarks>
public sealed class AboutWindowAdapter : IAboutWindow
{
    private readonly IServiceProvider _services;
    private AboutWindow? _window;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton <see cref="AboutWindow"/> on first call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public AboutWindowAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<AboutWindow>();
            // Owner = current MainWindow so the window minimizes / restores
            // together with the app and stays grouped in the taskbar.
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !ReferenceEquals(owner, _window))
            {
                _window.Owner = owner;
            }
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }
        else
        {
            _window.Activate();
        }
    }
}
