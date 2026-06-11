namespace EveDashboard.App.Wpf.Services;

using System.Diagnostics;

/// <summary>
/// Production <see cref="IBrowserLauncher"/>. Launches the URL via the OS shell
/// (<see cref="ProcessStartInfo.UseShellExecute"/> = <c>true</c>), which routes
/// to the user's default browser.
/// </summary>
public sealed class BrowserLauncher : IBrowserLauncher
{
    /// <inheritdoc />
    public void Open(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
