namespace EveDashboard.Sde.Conversion;

using EveDashboard.Core.Storage;

/// <summary>
/// Converts a folder of extracted everef.net reference-data JSON files into
/// rows in a target SQLite database. UPSERT semantics: works on an empty
/// database (fallback build) and on a populated one (runtime update).
/// </summary>
/// <remarks>
/// Plan 06 ships the conversion primitive (caller-owns-tx variant added by
/// plan 08). HTTP, decompression, conditional-GET cache validation, and the
/// in-place runtime update flow live in plan 08
/// (<see cref="EveDashboard.Sde.Updater.EverefSdeUpdater"/>).
/// DELETE-for-removed-rows is explicitly out of scope per plan 08 decision 8.3.
/// </remarks>
public interface ISdeFormatConverter
{
    /// <summary>
    /// Read everef.net JSON files from <paramref name="jsonFolderPath"/> and
    /// UPSERT their content into <paramref name="target"/>. Ensures schema,
    /// then wraps the entire import in a single SQLite transaction; rolls
    /// back on any error.
    /// </summary>
    /// <param name="jsonFolderPath">
    /// Filesystem folder containing extracted everef.net JSON files. Must
    /// contain (at minimum) <c>meta.json</c>, <c>categories.json</c>,
    /// <c>groups.json</c>, <c>types.json</c>. Other files in the folder are
    /// ignored.
    /// </param>
    /// <param name="target">
    /// Destination SQLite database. Schema is created if missing
    /// (<see cref="EveDashboard.Sde.Schema.SchemaInitializer"/>).
    /// </param>
    /// <param name="progress">Optional progress channel; null disables reporting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="jsonFolderPath"/> or <paramref name="target"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if <paramref name="jsonFolderPath"/> does not exist.</exception>
    /// <exception cref="FileNotFoundException">Thrown if a required JSON file is missing from the folder.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown on malformed JSON.</exception>
    /// <exception cref="System.Data.Common.DbException">Thrown on database errors (transaction rolled back before rethrow).</exception>
    Task ImportAsync(
        string jsonFolderPath,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Same import as <see cref="ImportAsync"/>, but the caller is responsible
    /// for opening and committing/rolling back the SQLite transaction. Allows
    /// the runtime updater (<see cref="EveDashboard.Sde.Updater.EverefSdeUpdater"/>)
    /// to interleave validation and meta-table writes inside the same atomic
    /// boundary as the import.
    /// </summary>
    /// <param name="jsonFolderPath">
    /// Filesystem folder containing extracted everef.net JSON files. Same
    /// requirements as <see cref="ImportAsync"/>.
    /// </param>
    /// <param name="target">
    /// Destination SQLite database. Caller MUST have ensured schema and
    /// opened a transaction before invoking — see <c>remarks</c>.
    /// </param>
    /// <param name="progress">Optional progress channel; null disables reporting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Caller is responsible for invoking
    /// <see cref="EveDashboard.Sde.Schema.SchemaInitializer.EnsureSchemaAsync"/>
    /// before opening their transaction. This method does NOT initialise
    /// schema and must not be called against an uninitialised database — the
    /// first <c>INSERT</c> would fail with a missing-table error. (Per plan 08
    /// decision 8.22: <c>PRAGMA journal_mode = WAL</c> cannot run inside an
    /// open transaction, so schema initialisation cannot be deferred to inside
    /// the inner import.)
    ///
    /// Caller must also have an active transaction on <paramref name="target"/>
    /// before invoking. The first DDL/DML inside the import will surface a
    /// driver-specific error if no transaction is open.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="jsonFolderPath"/> or <paramref name="target"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if <paramref name="jsonFolderPath"/> does not exist.</exception>
    /// <exception cref="FileNotFoundException">Thrown if a required JSON file is missing from the folder.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown on malformed JSON.</exception>
    /// <exception cref="System.Data.Common.DbException">Thrown on database errors (caller is responsible for rollback).</exception>
    Task ImportToOpenTransactionAsync(
        string jsonFolderPath,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
