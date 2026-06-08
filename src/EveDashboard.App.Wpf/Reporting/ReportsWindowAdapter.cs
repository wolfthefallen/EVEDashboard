namespace EveDashboard.App.Wpf.Reporting;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Production <see cref="IReportsWindow"/> implementation. Holds a single
/// <see cref="ReportsWindow"/> instance for the app lifetime; subsequent
/// <see cref="Show"/> calls re-show / bring-to-front the same instance so
/// geometry + sidebar state survive in-session open/close cycles. Plan 25.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Dialogs.SettingsDialogAdapter"/>'s service-locator
/// pattern (resolving the Window via <see cref="IServiceProvider"/>) but uses
/// <see cref="Window.Show"/> instead of <see cref="Window.ShowDialog"/>. The
/// adapter constructs the window lazily on the first <see cref="Show"/> call.
/// </remarks>
public sealed class ReportsWindowAdapter : IReportsWindow
{
    private readonly IServiceProvider _services;
    private ReportsWindow? _window;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton <see cref="ReportsWindow"/> on first call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public ReportsWindowAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            _window = _services.GetRequiredService<ReportsWindow>();
            // Owner = current MainWindow so the Reports window minimizes /
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
