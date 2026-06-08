namespace EveDashboard.Sde.Storage;

/// <summary>
/// Owns the on-disk location of the runtime SDE database and the first-launch
/// bootstrap from the shipped fallback. Does NOT open the SQLite — the
/// consumer constructs an <c>EveDashboard.Core.Storage.ISqliteDatabase</c> from
/// <see cref="DatabasePath"/> and registers it as a singleton.
/// </summary>
/// <remarks>
/// The runtime SDE is always available at <see cref="DatabasePath"/> after
/// <see cref="EnsureDatabaseExistsAsync"/> returns: either the file was already
/// present from a previous run, or it was just bootstrapped from the embedded
/// fallback resource shipped inside <c>EveDashboard.Sde.dll</c>.
/// </remarks>
public interface ISdeStorage
{
    /// <summary>
    /// Filesystem path of the runtime SDE SQLite database. Stable across
    /// the application lifetime; under <c>%APPDATA%\EveDashboard\sde\</c>
    /// on Windows.
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Ensure the SDE database file exists at <see cref="DatabasePath"/>. If
    /// missing, extracts the embedded fallback resource (shipped with
    /// <c>EveDashboard.Sde.dll</c>) to that location. Creates the parent directory
    /// if needed. Idempotent — returns immediately if the file already exists.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if a fallback copy was performed (i.e. first launch /
    /// recovery); <c>false</c> if the file already existed.
    /// </returns>
    /// <exception cref="System.IO.IOException">Thrown if the file system rejects the write (permissions, disk full, etc.).</exception>
    /// <exception cref="InvalidOperationException">Thrown if the embedded fallback resource cannot be located in the assembly.</exception>
    Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default);
}
