namespace EveDashboard.Persistence.Storage;

using System.Data.Common;
using EveDashboard.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;

/// <summary>
/// Thin <see cref="ISqliteDatabase"/> implementation backed by
/// <see cref="SqliteConnection"/>. Sealed by design — substitute via
/// <see cref="ISqliteDatabase"/> for testing, not by inheritance.
/// </summary>
public sealed class SqliteDatabase : ISqliteDatabase
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteDatabase> _log;

    /// <summary>
    /// Strong-rooted callback for <c>sqlite3_update_hook</c>. SQLite stores the
    /// callback by raw pointer; without keeping a managed reference alive for
    /// the lifetime of the connection, the GC could collect the delegate and
    /// the native callback would crash on the next write.
    /// </summary>
    private readonly strdelegate_update _hookCallback;

    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<SqliteRowChangedEventArgs>? RowChanged;

    /// <summary>
    /// Open a SQLite database using <paramref name="connectionString"/>. The
    /// connection is opened immediately and remains open until disposal.
    /// </summary>
    /// <param name="connectionString">
    /// Standard SQLite connection string. Examples:
    /// <c>"Data Source=C:\\path\\to\\file.db"</c>,
    /// <c>"Data Source=:memory:"</c>.
    /// </param>
    /// <param name="log">Logger for connection lifecycle and SQL errors.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is <c>null</c>.</exception>
    public SqliteDatabase(string connectionString, ILogger<SqliteDatabase> log)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // Hook sqlite3_update_hook so writes through this connection raise
        // RowChanged. The hook fires synchronously on the writing thread.
        // Microsoft.Data.Sqlite does not expose this as a managed event;
        // SQLitePCL.raw is the only path. The package is already in our
        // dependency closure (transitive through Microsoft.Data.Sqlite).
        _hookCallback = OnSqliteUpdate;
        raw.sqlite3_update_hook(_connection.Handle, _hookCallback, null);

        _log.LogDebug("SQLite connection opened: {DataSource}", _connection.DataSource);
    }

    /// <inheritdoc/>
    public async Task ExecuteScriptAsync(string sqlText, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sqlText;
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            _log.LogError(ex, "ExecuteScriptAsync failed: {Message}", ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await using var cmd = BuildCommand(sql, parameters);
        try
        {
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            _log.LogError(ex, "ExecuteNonQueryAsync failed: {Sql}", sql);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        Func<DbDataReader, T> mapper,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mapper);

        await using var cmd = BuildCommand(sql, parameters);
        var rows = new List<T>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(mapper(reader));
            }
            return rows;
        }
        catch (SqliteException ex)
        {
            _log.LogError(ex, "QueryAsync failed: {Sql}", sql);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<T?> QueryScalarAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await using var cmd = BuildCommand(sql, parameters);
        try
        {
            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (raw is null || raw is DBNull) return default;
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch (SqliteException ex)
        {
            _log.LogError(ex, "QueryScalarAsync failed: {Sql}", sql);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        UnhookUpdate();
        _connection.Dispose();
        _disposed = true;
        _log.LogDebug("SQLite connection disposed");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        UnhookUpdate();
        await _connection.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        _log.LogDebug("SQLite connection disposed (async)");
    }

    /// <summary>
    /// Detach the update hook by passing a null callback. Strictly redundant
    /// when the connection is about to be disposed, but matches the symmetric
    /// hook/unhook pattern reviewers expect.
    /// </summary>
    private void UnhookUpdate()
    {
        if (_connection.State != System.Data.ConnectionState.Closed)
        {
            raw.sqlite3_update_hook(_connection.Handle, (strdelegate_update?)null, null);
        }
    }

    /// <summary>
    /// Native <c>sqlite3_update_hook</c> callback. Translates the SQLite
    /// integer change-type constant to <see cref="SqliteRowChangeKind"/>
    /// and forwards to <see cref="RowChanged"/>.
    /// </summary>
    /// <param name="userData">Always null - we do not pass user state.</param>
    /// <param name="type">SQLite's <c>SQLITE_INSERT</c> / <c>SQLITE_UPDATE</c> / <c>SQLITE_DELETE</c>.</param>
    /// <param name="database">SQLite database name (e.g. <c>"main"</c>); not forwarded.</param>
    /// <param name="table">SQLite table name for the changed row.</param>
    /// <param name="rowid">The row's <c>rowid</c>.</param>
    private void OnSqliteUpdate(object userData, int type, string database, string table, long rowid)
    {
        SqliteRowChangeKind kind;
        switch (type)
        {
            case raw.SQLITE_INSERT: kind = SqliteRowChangeKind.Insert; break;
            case raw.SQLITE_UPDATE: kind = SqliteRowChangeKind.Update; break;
            case raw.SQLITE_DELETE: kind = SqliteRowChangeKind.Delete; break;
            default: return; // future SQLite event types are silently ignored.
        }

        var handler = RowChanged;
        if (handler is null) return;

        handler(this, new SqliteRowChangedEventArgs(table, rowid, kind));
    }

    private SqliteCommand BuildCommand(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }
        return cmd;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SqliteDatabase));
    }
}
