namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Plan 51: production <see cref="ILifetimeTotalsWindow"/> implementation. Holds
/// a single <see cref="LifetimeTotalsWindow"/> for the app lifetime; subsequent
/// <see cref="Show"/> calls re-show / bring-to-front the same instance so
/// geometry survives in-session open/close cycles.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EveDashboard.App.Wpf.CrystalDiagramWindowAdapter"/>'s shape
/// (service-locator resolve of the singleton window, lazy construction, Owner =
/// current MainWindow, <see cref="Window.Show"/> not <see cref="Window.ShowDialog"/>).
/// DEVIATION: after showing / activating, it kicks a refresh
/// (<c>_ = _window.RefreshAsync();</c>) so the all-time totals are current on
/// every open. The lifetime rollup is global and can change while the window is
/// hidden, and the window has no in-window action that would refresh it — unlike
/// Reports / Crystal, which refresh via their own user actions — so the adapter
/// drives it.
/// </remarks>
public sealed class LifetimeTotalsWindowAdapter : ILifetimeTotalsWindow
{
    private readonly IServiceProvider _services;
    private LifetimeTotalsWindow? _window;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton <see cref="LifetimeTotalsWindow"/> on first call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public LifetimeTotalsWindowAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<LifetimeTotalsWindow>();
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

        // Re-fetch the all-time totals so a hidden-then-reshown window reflects
        // sessions mined / imported / deleted while it was hidden. Fire-and-forget
        // on the UI thread (RefreshAsync ConfigureAwait(true)s back); failures are
        // swallowed-and-logged inside RefreshAsync.
        _ = _window.RefreshAsync();
    }
}
