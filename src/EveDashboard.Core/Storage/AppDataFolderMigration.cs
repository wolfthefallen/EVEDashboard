namespace EveDashboard.Core.Storage;

/// <summary>
/// One-time move of the per-user data folder from the legacy
/// <c>%APPDATA%\EveMiningDashboard</c> to the current
/// <c>%APPDATA%\EveDashboard</c> (the plan-39 project rename). Runs once at
/// startup so an existing user keeps every session, the SDE cache, config, and
/// logs after the rename.
/// </summary>
/// <remarks>
/// Idempotent: a no-op once the new folder exists. The move is an atomic
/// same-volume <see cref="Directory.Move(string,string)"/> (both folders live
/// under <c>%APPDATA%</c>), so it carries <c>session.sqlite</c> (plus its
/// <c>-wal</c>/<c>-shm</c> siblings), <c>config.yaml</c>, <c>sde\</c>, and
/// <c>logs\</c> across in one operation. It MUST run before
/// <c>LoggingSetup.Configure</c>, which creates the new folder — otherwise the
/// guard below would see the new folder already present, skip the move, and
/// leave the old data stranded.
/// </remarks>
public static class AppDataFolderMigration
{
    /// <summary>The pre-plan-39 app-data folder name under <c>%APPDATA%</c>.</summary>
    private const string LegacyFolderName = "EveMiningDashboard";

    /// <summary>
    /// Move the legacy <c>EveMiningDashboard</c> data folder to the current
    /// <see cref="AppDataPaths.GetAppDataRoot"/> location if, and only if, the
    /// new folder does not yet exist and the legacy one does.
    /// </summary>
    /// <returns>
    /// <c>true</c> if a move was performed; <c>false</c> if there was nothing to
    /// do (a fresh install, or an already-migrated profile).
    /// </returns>
    /// <exception cref="IOException">
    /// Propagated from <see cref="Directory.Move(string,string)"/> if the move
    /// fails; the caller surfaces this as a fatal startup error so the user's
    /// data is never silently lost.
    /// </exception>
    public static bool MigrateIfNeeded()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyRoot = Path.Combine(appData, LegacyFolderName);
        var newRoot = AppDataPaths.GetAppDataRoot();
        if (Directory.Exists(newRoot) || !Directory.Exists(legacyRoot))
        {
            return false;
        }
        Directory.Move(legacyRoot, newRoot);
        return true;
    }
}
