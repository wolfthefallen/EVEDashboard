namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;
using EveDashboard.App.Wpf.Bootstrap;
using EveDashboard.Core.Configuration;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Production <see cref="ISettingsDialog"/> implementation. Resolves a fresh
/// <see cref="SettingsDialog"/> from the DI container per <see cref="Show"/>
/// call, parents it on the current <see cref="Application.MainWindow"/>, and
/// shows it modally.
/// </summary>
/// <remarks>
/// <para>
/// This adapter takes <see cref="IServiceProvider"/>
/// (a deviation from <see cref="ConfirmDialogAdapter"/>'s parameterless
/// shape) because <see cref="SettingsDialog"/> has a transitive dependency
/// graph (VM, sub-dialog, repos) and hand-wiring it in the adapter would
/// duplicate the DI registration logic. This is a localized service-locator
/// pattern at a UI boundary, not a general convention.
/// </para>
/// <para>
/// In bootstrap-failure mode (<c>failure</c> non-null) the adapter
/// constructs the <see cref="SettingsDialogViewModel"/> by hand instead of via
/// DI, falling back to <see cref="NoOpSettingsRepository"/> /
/// <see cref="NoOpMarketStationCatalog"/> / <see cref="NoOpAddStationDialog"/>
/// for services not yet registered in the supplied <see cref="IServiceProvider"/>.
/// This means the same adapter works against both the main DI container (for
/// chrome-button calls after chain step 10) and the bootstrap mini-container
/// (for failure calls before chain step 10) — see
/// <c>App.ShowBootstrapFailureDialog</c>.
/// </para>
/// </remarks>
public sealed class SettingsDialogAdapter : ISettingsDialog
{
    private readonly IServiceProvider _services;

    /// <summary>Construct the adapter.</summary>
    /// <param name="services">DI container used to resolve a fresh <see cref="SettingsDialog"/> per call.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public SettingsDialogAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public void Show(BootstrapFailureContext? failure = null)
    {
        // Capture MainWindow BEFORE constructing the dialog. WPF auto-promotes
        // the first instantiated Window to Application.MainWindow; in
        // bootstrap-failure mode no other Window has been shown yet, so a
        // post-construction read of Application.MainWindow returns the dialog
        // itself, and `Owner = self` throws ArgumentException. Capturing first
        // preserves the pre-existing MainWindow (if any), and we only set
        // Owner when there is a real candidate that is not the dialog.
        var ownerCandidate = Application.Current?.MainWindow;

        SettingsDialog dialog;
        if (failure is null)
        {
            // Normal mode: full DI graph as before. SettingsDialog is registered
            // transient by ServiceRegistration.
            dialog = _services.GetRequiredService<SettingsDialog>();
        }
        else
        {
            // Bootstrap-failure mode: build the VM by hand with the failure
            // context. Settings/stations/addStationDialog services may not be
            // registered in the supplied container (they depend on the session
            // SQLite, which is opened by chain step 3 — after the failure
            // points at chain steps 2 / 9). Fall back to throwing stubs; the
            // VM's IsBootstrapFailureMode gate suppresses every reachable call
            // to them.
            var vm = new SettingsDialogViewModel(
                settings:         _services.GetService<ISettingsRepository>() ?? NoOpSettingsRepository.Instance,
                stations:         _services.GetService<IMarketStationCatalog>() ?? NoOpMarketStationCatalog.Instance,
                config:           _services.GetRequiredService<IBootstrapConfigLoader>(),
                addStationDialog: _services.GetService<IAddStationDialog>() ?? NoOpAddStationDialog.Instance,
                errorReporter:    _services.GetRequiredService<IBootstrapErrorReporter>(),
                loggerFactory:    _services.GetRequiredService<ILoggerFactory>(),
                bootstrapFailure: failure);
            var log = _services.GetRequiredService<ILogger<SettingsDialog>>();
            dialog = new SettingsDialog(vm, log);
        }

        if (ownerCandidate is not null && !ReferenceEquals(ownerCandidate, dialog))
        {
            dialog.Owner = ownerCandidate;
        }
        dialog.ShowDialog();
    }
}
