namespace EveDashboard.Core.Configuration;

/// <summary>
/// Reads and writes the application's bootstrap configuration: the small
/// set of settings the app must know before SQLite can be opened. v1
/// schema is exactly one key: <c>gamelog_path</c>. All other user-facing
/// settings live in the SQL <c>setting</c> table (see plan 14).
/// </summary>
/// <remarks>
/// <para>
/// The configuration file is at <c>%APPDATA%\EveDashboard\config.yaml</c>
/// on Windows. If absent, the loader autodetects the gamelog path under
/// the user's Documents folder.
/// </para>
/// <para>
/// Single-threaded usage assumed: <see cref="LoadAsync"/> runs once at
/// app startup before the UI opens; <see cref="SaveAsync"/> runs on the
/// UI thread from the Settings UI (plan 17) on user-edit-and-confirm.
/// </para>
/// </remarks>
public interface IBootstrapConfigLoader
{
    /// <summary>
    /// Read <c>config.yaml</c> if present, or autodetect the gamelog path
    /// if absent. Returns one of three closed cases on
    /// <see cref="BootstrapConfigResult"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="BootstrapConfigResult.Loaded"/> if the file existed and
    /// parsed cleanly. <see cref="BootstrapConfigResult.AutodetectUsed"/>
    /// if the file was missing and the autodetect path's directory exists.
    /// <see cref="BootstrapConfigResult.AutodetectFailed"/> if the file
    /// was missing and the autodetect path's directory does not exist
    /// (loader also logs <c>Error</c>).
    /// </returns>
    /// <exception cref="BootstrapConfigException">Thrown if <c>config.yaml</c> is present but malformed, missing the <c>gamelog_path</c> key, or has an empty / whitespace-only value.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled.</exception>
    Task<BootstrapConfigResult> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist the user's chosen gamelog path to <c>config.yaml</c>.
    /// Atomic via temp-file + rename; the parent directory is created
    /// if missing. Overwrites any existing file.
    /// </summary>
    /// <param name="gamelogPath">The path to persist. Stored verbatim; env-var expansion happens only on read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="gamelogPath"/> is null.</exception>
    /// <exception cref="System.IO.IOException">Thrown if the file or its parent directory cannot be written.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled.</exception>
    Task SaveAsync(string gamelogPath, CancellationToken ct = default);
}
