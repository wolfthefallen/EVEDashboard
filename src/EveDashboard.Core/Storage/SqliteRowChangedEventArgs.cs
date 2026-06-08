namespace EveDashboard.Core.Storage;

/// <summary>
/// Payload for <see cref="ISqliteDatabase.RowChanged"/>. Identifies the row that
/// was inserted, updated, or deleted on the underlying SQLite connection.
/// </summary>
/// <remarks>
/// SQLite's native <c>sqlite3_update_hook</c> only reports the table name, the
/// affected row's <c>rowid</c>, and the change kind. It does NOT report the
/// row's column values; consumers that need them must query the row themselves.
/// </remarks>
public sealed class SqliteRowChangedEventArgs : EventArgs
{
    /// <summary>Construct a row-changed payload.</summary>
    /// <param name="table">SQLite table name for the changed row.</param>
    /// <param name="rowId">The row's <c>rowid</c> (INTEGER PRIMARY KEY value when one exists).</param>
    /// <param name="changeKind">Whether the row was inserted, updated, or deleted.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="table"/> is null.</exception>
    public SqliteRowChangedEventArgs(string table, long rowId, SqliteRowChangeKind changeKind)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        RowId = rowId;
        ChangeKind = changeKind;
    }

    /// <summary>SQLite table name for the changed row.</summary>
    public string Table { get; }

    /// <summary>The row's <c>rowid</c> (INTEGER PRIMARY KEY value when one exists).</summary>
    public long RowId { get; }

    /// <summary>Whether the row was inserted, updated, or deleted.</summary>
    public SqliteRowChangeKind ChangeKind { get; }
}
