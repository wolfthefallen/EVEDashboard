namespace EveDashboard.App.Wpf.Services;

/// <summary>
/// Opens a URL in the user's default browser. Behind an interface so the
/// update-notification flow can be unit-tested without launching a process.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>Open <paramref name="url"/> in the default browser.</summary>
    /// <param name="url">Absolute URL to open.</param>
    void Open(string url);
}
