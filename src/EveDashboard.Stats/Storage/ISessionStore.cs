namespace EveDashboard.Stats.Storage;

using EveDashboard.Core.Storage;

/// <summary>
/// Owns the on-disk location of the runtime session SQLite database and the
/// idempotent schema-initialisation entry point.
/// </summary>
/// <remarks>
/// Mirror of <c>EveDashboard.Sde.Storage.ISdeStorage</c>: the store does not open
/// the database itself — the consumer constructs an <see cref="ISqliteDatabase"/>
/// from <see cref="DatabasePath"/> and passes it into
/// <see cref="EnsureSchemaAsync(ISqliteDatabase, CancellationToken)"/>. This
/// keeps the store ignorant of the singleton-connection pattern that plan 12's
/// writer + notifier depend on; the wiring lives in <c>App.xaml.cs OnStartup</c>.
/// <para>
/// Filesystem prep is the caller's responsibility: before constructing the
/// SQLite wrapper against <see cref="DatabasePath"/>, the caller must ensure
/// the parent directory exists (<c>Directory.CreateDirectory(Path.GetDirectoryName(store.DatabasePath))</c>).
/// <c>SqliteConnection.Open()</c> creates the file but not the directory.
/// </para>
/// </remarks>
public interface ISessionStore
{
    /// <summary>
    /// Filesystem path of the runtime session SQLite database. Stable across
    /// the application lifetime; under <c>%APPDATA%\EveDashboard\</c>
    /// on Windows.
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Ensure the schema is initialised in <paramref name="target"/>. Safe to
    /// call repeatedly; idempotent. Pure schema operation — does not touch the
    /// filesystem path of <see cref="DatabasePath"/>; that is the caller's
    /// responsibility (see remarks on <see cref="ISessionStore"/>).
    /// </summary>
    /// <param name="target">An opened SQLite wrapper for <see cref="DatabasePath"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="target"/> is <c>null</c>.</exception>
    Task EnsureSchemaAsync(ISqliteDatabase target, CancellationToken ct = default);
}
