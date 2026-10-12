namespace EveDashboard.App.Wpf.Dialogs;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Production <see cref="IHistoricalSessionDialog"/>: resets the singleton
/// view-model for a fresh open, resolves a transient window, and shows it
/// modally over the active window (normally Reports).
/// </summary>
public sealed class HistoricalSessionDialogAdapter : IHistoricalSessionDialog
{
    private readonly IServiceProvider _services;

    /// <summary>Construct the adapter over the DI container.</summary>
    /// <param name="services">Service provider used to resolve the view-model and the window.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is null.</exception>
    public HistoricalSessionDialogAdapter(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public HistoricalBatchOutcome? Show(IReadOnlyList<string>? preselectedSessionIds)
    {
        var vm = _services.GetRequiredService<HistoricalSessionDialogViewModel>();
        vm.ResetForOpen(preselectedSessionIds);
        var dialog = _services.GetRequiredService<HistoricalSessionDialog>();
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();
        return vm.Outcome;
    }
}
