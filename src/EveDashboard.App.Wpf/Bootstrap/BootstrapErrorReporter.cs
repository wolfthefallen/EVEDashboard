namespace EveDashboard.App.Wpf.Bootstrap;

using System.Windows;

/// <summary>
/// Surfaces a fatal bootstrap-time configuration error via WPF
/// <see cref="MessageBox"/> and shuts the application down with exit code 1.
/// </summary>
/// <remarks>
/// Plan 16 ships this as the only bootstrap-failure UX for three branches:
/// <see cref="EveDashboard.Core.Configuration.BootstrapConfigResult.AutodetectFailed"/>,
/// thrown <see cref="EveDashboard.Core.Configuration.BootstrapConfigException"/>,
/// and "no characters detected at <c>gamelog_path</c>" surfaced by
/// <see cref="EveDashboard.Mining.Gamelogs.GamelogDiscoveryService"/>. Plan 17
/// supersedes by re-routing those branches to the in-app Settings dialog.
/// </remarks>
public static class BootstrapErrorReporter
{
    /// <summary>
    /// Show <paramref name="body"/> in an OK-button error <see cref="MessageBox"/>
    /// titled <paramref name="title"/>, then call
    /// <see cref="Application.Shutdown(int)"/> with exit code <c>1</c>.
    /// </summary>
    /// <param name="title">Dialog title — short and specific (e.g. "Cannot find your EVE gamelog folder").</param>
    /// <param name="body">Multi-line dialog body — explains the failure and points at the next user action.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public static void ReportAndShutdown(string title, string body)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Error);
        Application.Current?.Shutdown(1);
    }
}
