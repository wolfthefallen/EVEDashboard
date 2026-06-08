namespace EveDashboard.Core.Storage;

using System.Data.Common;

/// <summary>
/// Read/write access to a single SQLite database. Implementations encapsulate
/// the underlying <c>Microsoft.Data.Sqlite.SqliteConnection</c>; no raw
/// connection handle escapes this interface.
/// </summary>
/// <remarks>
/// Lifetime is the consumer's responsibility — register as singleton when the
/// connection is long-lived (e.g. a read-mostly SDE cache) or as scoped/transient
/// when the consumer needs its own connection per scope.
/// </remarks>
public interface ISqliteDatabase : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Execute a multi-statement SQL script. Used for schema initialisation
    /// (CREATE TABLE, indexes) and for ingesting external SQL dumps.
    /// </summary>
    /// <param name="sqlText">One or more SQL statements separated by semicolons.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="System.Data.Common.DbException">
    /// Rethrown after logging at <c>Error</c> if any statement is rejected.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if the database has been disposed.</exception>
    Task ExecuteScriptAsync(string sqlText, CancellationToken ct = default);

    /// <summary>
    /// Execute a single non-query (INSERT / UPDATE / DELETE / DDL).
    /// Returns the number of rows affected, as reported by SQLite.
    /// </summary>
    /// <param name="sql">A single SQL statement, optionally with named parameters (<c>$param</c>).</param>
    /// <param name="parameters">Parameter name → value map. Names should include the leading <c>$</c> sigil.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rows affected.</returns>
    /// <exception cref="System.Data.Common.DbException">Rethrown after logging on SQL error.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the database has been disposed.</exception>
    Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a SELECT and project each row through <paramref name="mapper"/>.
    /// Returns the materialised rows in source order.
    /// </summary>
    /// <typeparam name="T">Row projection type.</typeparam>
    /// <param name="sql">A single SELECT statement, optionally with named parameters.</param>
    /// <param name="mapper">Row projection — invoked once per row with a positioned reader.</param>
    /// <param name="parameters">Parameter name → value map. Names should include the leading <c>$</c> sigil.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Materialised rows in source order.</returns>
    /// <exception cref="System.Data.Common.DbException">Rethrown after logging on SQL error.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the database has been disposed.</exception>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        Func<DbDataReader, T> mapper,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a SELECT returning a single scalar value (or <c>default</c>
    /// if the result set is empty or the value is <c>NULL</c>).
    /// </summary>
    /// <typeparam name="T">Scalar type. Supports <see cref="int"/>, <see cref="long"/>, <see cref="string"/> and other types convertible via <see cref="Convert.ChangeType(object, Type)"/>.</typeparam>
    /// <param name="sql">A single SELECT statement, optionally with named parameters.</param>
    /// <param name="parameters">Parameter name → value map. Names should include the leading <c>$</c> sigil.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scalar value, or <c>default(T)</c> if the result set is empty or <c>NULL</c>.</returns>
    /// <exception cref="System.Data.Common.DbException">Rethrown after logging on SQL error.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the database has been disposed.</exception>
    /// <remarks>
    /// Plan 05 only commits tests for <see cref="int"/>, <see cref="long"/>, and <see cref="string"/>
    /// scalars. Future consumers needing <see cref="DateTimeOffset"/>, <see cref="Guid"/>, or
    /// <c>byte[]</c> scalars should add explicit handling and tests.
    /// </remarks>
    Task<T?> QueryScalarAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Raised on every INSERT / UPDATE / DELETE through the underlying SQLite
    /// connection. Consumers (e.g. plan 12's session-event notifier) filter on
    /// <see cref="SqliteRowChangedEventArgs.Table"/> and translate to higher-level
    /// events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backed natively by <c>sqlite3_update_hook</c> via
    /// <c>Microsoft.Data.Sqlite.SqliteConnection.Update</c>. The hook fires only
    /// on writes performed through THIS connection - other connections to the
    /// same database file do not trigger it. With the SQL-substrate architecture
    /// (see <c>MEMORY.md -&gt; project_sql_substrate</c>) this is by design: the
    /// singleton write connection is the truth signal.
    /// </para>
    /// <para>
    /// The event argument carries only the table name, the affected
    /// <c>rowid</c>, and the change kind - not column values. Consumers that
    /// need column values must query the row themselves.
    /// </para>
    /// </remarks>
    event EventHandler<SqliteRowChangedEventArgs>? RowChanged;
}
