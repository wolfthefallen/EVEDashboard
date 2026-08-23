namespace EveDashboard.Core.Storage;

/// <summary>
/// Centralised resolution of per-user application paths under <c>%APPDATA%</c>.
/// </summary>
/// <remarks>
/// Single source of truth for the literal folder name <c>EveDashboard</c>
/// under <see cref="Environment.SpecialFolder.ApplicationData"/>. Consumed by
/// <c>SessionStore</c>, <c>SdeStorage</c>, and
/// <c>LoggingSetup</c>. Lives in <c>EveDashboard.Core</c> per the
/// architecture mandate so siblings consume it without taking peer
/// dependencies on each other.
/// </remarks>
public static class AppDataPaths
{
    /// <summary>
    /// Application root folder name under <c>%APPDATA%</c>. Single source of truth.
    /// </summary>
    public const string AppFolderName = "EveDashboard";

    /// <summary>
    /// Returns the absolute path of the per-user app-data root,
    /// <c>%APPDATA%\EveDashboard</c> on Windows.
    /// </summary>
    /// <returns>
    /// Absolute path. The folder is NOT created — callers are responsible for
    /// <see cref="Directory.CreateDirectory(string)"/> before writing into it.
    /// </returns>
    public static string GetAppDataRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, AppFolderName);
    }
}
