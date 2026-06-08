namespace EveDashboard.Core.Storage;

/// <summary>
/// Kind of row-level change reported by <see cref="ISqliteDatabase.RowChanged"/>.
/// Mirrors SQLite's native <c>sqlite3_update_hook</c> event types but lives in
/// Core so consumers do not have to reference <c>Microsoft.Data.Sqlite</c>.
/// </summary>
public enum SqliteRowChangeKind
{
    /// <summary>A new row was INSERTed.</summary>
    Insert,

    /// <summary>An existing row was UPDATEd.</summary>
    Update,

    /// <summary>A row was DELETEd.</summary>
    Delete,
}
