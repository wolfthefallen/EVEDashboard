namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Production <see cref="ISessionConfigDialog"/> implementation. Resolves a
/// fresh <see cref="SessionConfigDialog"/> from DI per call (transient
/// registration), parents it on the current MainWindow, resets the singleton
/// VM for a clean open, and calls <see cref="Window.ShowDialog"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="SettingsDialogAdapter"/>'s service-locator pattern: the
/// adapter takes <see cref="IServiceProvider"/> rather than wiring the dialog
/// graph by hand, so DI continues to own the VM + window construction.
/// </para>
/// <para>
/// Registration shape: <see cref="SessionConfigDialogViewModel"/> is registered
/// as a singleton (wraps the singleton <c>SessionController</c>; no dialog-
/// instance state) and <see cref="SessionConfigDialog"/> as transient. DI
/// passes the singleton VM into the transient dialog's ctor, so the VM the
/// adapter resets and the VM bound as the dialog's <c>DataContext</c> are the
/// same instance.
/// </para>
/// </remarks>
public sealed class SessionConfigDialogAdapter : ISessionConfigDialog
{
    private readonly IServiceProvider _services;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve the singleton VM and a fresh <see cref="SessionConfigDialog"/> per call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public SessionConfigDialogAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show()
    {
        // Reset the singleton VM BEFORE constructing the dialog so the dialog's
        // initial render reflects the cleared name + cleared create/confirm state.
        // The fleet list itself is (re)hydrated on the dialog's Loaded event
        // (plan 42), so the saved fleets and default selection are always fresh.
        var vm = _services.GetRequiredService<SessionConfigDialogViewModel>();
        vm.ResetForNewOpen();

        var dialog = _services.GetRequiredService<SessionConfigDialog>();
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();
    }
}
