namespace EveDashboard.App.Wpf.Reporting.HaulValue;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Plan 55: production <see cref="IHaulValueWindow"/> implementation. Holds a
/// single <see cref="HaulValueWindow"/> for the app lifetime; subsequent
/// <see cref="Show"/> calls re-show / bring-to-front the same instance.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EveDashboard.App.Wpf.Reporting.Lifetime.LifetimeTotalsWindowAdapter"/>
/// (service-locator resolve of the singleton window, lazy construction, Owner =
/// current MainWindow, <see cref="Window.Show"/> not <see cref="Window.ShowDialog"/>),
/// and like it kicks a session-list refresh on every show so a session mined /
/// imported / deleted while the window was hidden appears in the picker.
/// </remarks>
public sealed class HaulValueWindowAdapter : IHaulValueWindow
{
    private readonly IServiceProvider _services;
    private HaulValueWindow? _window;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton <see cref="HaulValueWindow"/> on first call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public HaulValueWindowAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<HaulValueWindow>();
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

        // Refresh the session picker so a just-finished session is selectable.
        _ = _window.RefreshSessionsAsync();
    }
}
