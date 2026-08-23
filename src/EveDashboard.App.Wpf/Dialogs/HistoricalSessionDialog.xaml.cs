namespace EveDashboard.App.Wpf.Dialogs;

using System.ComponentModel;
using System.Data.Common;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Modal "Build session from history" window. Thin: hydrates the view-model on
/// Loaded and closes when the view-model asks to.
/// </summary>
public partial class HistoricalSessionDialog : Window
{
    private readonly HistoricalSessionDialogViewModel _vm;
    private readonly ILogger<HistoricalSessionDialog> _log;

    /// <summary>Construct the window over its (singleton) view-model.</summary>
    /// <param name="vm">View-model, already reset for this open by the adapter.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public HistoricalSessionDialog(HistoricalSessionDialogViewModel vm, ILogger<HistoricalSessionDialog> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _log = log;
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Hydrate once shown. The await is wrapped so a DB / IO failure never escapes
    /// this <c>async void</c> handler as an unhandled dispatcher exception: the
    /// dialog opens degraded (no fleets, nothing to build) with the error shown.
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.InitializeAsync();
        }
        catch (Exception ex) when (ex is DbException or IOException)
        {
            _log.LogWarning(ex, "Failed to initialise the Build-from-history dialog.");
            _vm.StatusMessage = "Could not load: " + ex.Message;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoricalSessionDialogViewModel.ShouldClose)) return;
        if (_vm.ShouldClose) Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }
}
