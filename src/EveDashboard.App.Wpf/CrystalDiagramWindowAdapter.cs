namespace EveDashboard.App.Wpf;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Production <see cref="ICrystalDiagramWindow"/> implementation. Holds a
/// single <see cref="CrystalDiagramWindow"/> instance for the app lifetime;
/// subsequent <see cref="Show"/> calls re-show / bring-to-front the same
/// instance so geometry survives in-session open/close cycles.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Reporting.ReportsWindowAdapter"/>'s shape exactly:
/// service-locator resolve of the singleton window via
/// <see cref="IServiceProvider"/>, lazy construction on first call,
/// <see cref="Window.Show"/> rather than <see cref="Window.ShowDialog"/>.
/// </remarks>
public sealed class CrystalDiagramWindowAdapter : ICrystalDiagramWindow
{
    private readonly IServiceProvider _services;
    private CrystalDiagramWindow? _window;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton <see cref="CrystalDiagramWindow"/> on first call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public CrystalDiagramWindowAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<CrystalDiagramWindow>();
            // Owner = current MainWindow so the diagram window minimizes /
            // restores together with the app and stays grouped in the taskbar.
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
