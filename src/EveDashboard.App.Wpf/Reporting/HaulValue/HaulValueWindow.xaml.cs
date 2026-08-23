namespace EveDashboard.App.Wpf.Reporting.HaulValue;

using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Modeless, singleton, hide-on-close Haul Value window. Prices a haul
/// (pasted item list or a saved session's refined output) across every station in
/// the market catalog and shows where it is worth the most.
/// </summary>
/// <remarks>
/// v1 does NOT persist window geometry (unlike the About / Lifetime windows) — it
/// opens at its XAML default size each time; geometry persistence is a noted
/// follow-on. Hide-on-close keeps the singleton + its inputs across open/close
/// cycles; the DI container disposes the window at app shutdown.
/// </remarks>
public partial class HaulValueWindow : Window
{
    private readonly HaulValueWindowViewModel _vm;
    private readonly ILogger<HaulValueWindow> _log;

    /// <summary>Construct the Haul Value window.</summary>
    /// <param name="vm">The window's view-model.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public HaulValueWindow(HaulValueWindowViewModel vm, ILogger<HaulValueWindow> log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _log = log;

        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>Reload the saved-session picker (called by the adapter on each show).</summary>
    public async Task RefreshSessionsAsync()
    {
        try
        {
            await _vm.RefreshSessionsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Haul Value session refresh failed.");
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshSessionsAsync().ConfigureAwait(true);
    }

    private async void ValueButton_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ValueHaulAsync().ConfigureAwait(true);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide instead of close so the singleton + its inputs survive in-session
        // open/close cycles. App shutdown disposes the window via the DI container.
        e.Cancel = true;
        Hide();
    }
}
