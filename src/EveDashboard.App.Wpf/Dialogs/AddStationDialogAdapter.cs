namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Production <see cref="IAddStationDialog"/> implementation. Resolves a fresh
/// <see cref="AddStationDialog"/> from the DI container per <see cref="Show"/>
/// call, parents it on the current <see cref="Application.MainWindow"/> (or
/// the active modal if a Settings dialog is open above it), shows it modally,
/// and returns the dialog's <see cref="AddStationDialog.Result"/>.
/// </summary>
/// <remarks>
/// Per plan 22.1 decision 22.L: takes <see cref="IServiceProvider"/> so the
/// container can compose the dialog with its ViewModel rather than the adapter
/// hand-wiring construction.
/// </remarks>
public sealed class AddStationDialogAdapter : IAddStationDialog
{
    private readonly IServiceProvider _services;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve a fresh <see cref="AddStationDialog"/> per call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public AddStationDialogAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public MarketStation? Show()
    {
        var dialog = _services.GetRequiredService<AddStationDialog>();
        dialog.Owner = ResolveOwner();
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private static Window? ResolveOwner()
    {
        var app = Application.Current;
        if (app is null) return null;

        // Prefer the topmost active window (the parent Settings dialog) so the
        // sub-dialog is centered over its caller, not over the chrome MainWindow.
        foreach (Window w in app.Windows)
        {
            if (w.IsActive) return w;
        }
        return app.MainWindow;
    }
}
