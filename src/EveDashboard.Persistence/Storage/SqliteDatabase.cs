namespace EveDashboard.Persistence.Storage;

using System.Data.Common;
using System.Globalization;
using EveDashboard.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;

/// <summary>
/// Thin <see cref="ISqliteDatabase"/> implementation backed by a single
/// <see cref="SqliteConnection"/>. Sealed by design — substitute via
/// <see cref="ISqliteDatabase"/> for testing, not by inheritance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safety.</b> <c>Microsoft.Data.Sqlite</c> connections are NOT
/// thread-safe, and this class shares one connection across the whole app
/// (the UI stat-refresh, the background event-writer consumer, the market
/// prefetch, ...). Every operation therefore runs under a single
/// <see cref="SemaphoreSlim"/> (<see cref="_gate"/>) so only one command ever
/// touches the connection at a time. Without this, concurrent access corrupts
/// the driver's internal state and throws (see BUGS.md Bug 1). The public
/// methods acquire the gate and delegate to the private un-gated
/// <c>*CoreAsync</c> workers.
/// </para>
/// <para>
/// <see cref="WithTransactionAsync{T}"/> holds the gate across a whole
/// <c>BEGIN..COMMIT</c> so transactions stay atomic. Inside a transaction body
/// the gate is already held by THIS async flow, so calls back into the public
/// methods (directly or via existing helper code) must NOT re-acquire it — an
/// <see cref="AsyncLocal{T}"/> flag (<see cref="_inTransaction"/>) makes those
/// re-entrant calls run un-gated. The <see cref="ISqliteTransaction"/> handle
/// passed to the body forwards to the same un-gated workers.
/// </para>
/// <para>
/// The <see cref="RowChanged"/> update hook fires synchronously inside a write,
/// i.e. while the gate is held — handlers must not call back into this database
/// (see the <see cref="RowChanged"/> remarks).
/// </para>
/// </remarks>
public sealed class SqliteDatabase : ISqliteDatabase
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteDatabase> _log;

    /// <summary>
    /// Serialises ALL access to the non-thread-safe <see cref="_connection"/>.
    /// Held by every public method for its single command, and by
    /// <see cref="WithTransactionAsync{T}"/> for a whole transaction.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// True for the duration of a <see cref="WithTransactionAsync{T}"/> body on
    /// the body's async flow. Public methods called re-entrantly while it is set
    /// run un-gated (the gate is already held by the transaction), so existing
    /// code that calls back into this database from inside a transaction body
    /// does not self-deadlock on the non-reentrant gate. Flows with
    /// <see cref="ExecutionContext"/> through awaits — so it must NOT be relied
    /// on across threads the body explicitly fans work out to.
    /// </summary>
    private readonly AsyncLocal<bool> _inTransaction = new();

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
        if (_inTransaction.Value)
        {
            await ExecuteScriptCoreAsync(sqlText, ct).ConfigureAwait(false);
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ExecuteScriptCoreAsync(sqlText, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_inTransaction.Value)
        {
            return await ExecuteNonQueryCoreAsync(sql, parameters, ct).ConfigureAwait(false);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteNonQueryCoreAsync(sql, parameters, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
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

        if (_inTransaction.Value)
        {
            return await QueryCoreAsync(sql, mapper, parameters, ct).ConfigureAwait(false);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await QueryCoreAsync(sql, mapper, parameters, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<T?> QueryScalarAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_inTransaction.Value)
        {
            return await QueryScalarCoreAsync<T>(sql, parameters, ct).ConfigureAwait(false);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await QueryScalarCoreAsync<T>(sql, parameters, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public Task WithTransactionAsync(
        Func<ISqliteTransaction, CancellationToken, Task> body,
        bool immediate = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        return WithTransactionAsync<object?>(
            async (tx, c) => { await body(tx, c).ConfigureAwait(false); return null; },
            immediate,
            ct);
    }

    /// <inheritdoc/>
    public async Task<T> WithTransactionAsync<T>(
        Func<ISqliteTransaction, CancellationToken, Task<T>> body,
        bool immediate = false,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(body);
        if (_inTransaction.Value)
        {
            // SQLite does not support nested BEGIN on one connection, and the
            // gate is already held — fail loud rather than deadlock or corrupt.
            throw new InvalidOperationException(
                "Nested WithTransactionAsync is not supported on a single SQLite connection.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        _inTransaction.Value = true;
        try
        {
            await ExecuteNonQueryCoreAsync(immediate ? "BEGIN IMMEDIATE" : "BEGIN TRANSACTION", null, ct)
                .ConfigureAwait(false);
            try
            {
                var result = await body(new Transaction(this), ct).ConfigureAwait(false);
                // Do not cancel the COMMIT/ROLLBACK themselves — a half-cancelled
                // transaction is worse than a completed one.
                await ExecuteNonQueryCoreAsync("COMMIT", null, CancellationToken.None).ConfigureAwait(false);
                return result;
            }
            catch
            {
                try
                {
                    await ExecuteNonQueryCoreAsync("ROLLBACK", null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    // Surface the rollback failure (do not swallow it), but the
                    // original exception is the one the caller needs — rethrow it.
                    _log.LogWarning(rollbackEx, "ROLLBACK failed after a transaction body error; rethrowing the original error.");
                }

                throw;
            }
        }
        finally
        {
            _inTransaction.Value = false;
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        UnhookUpdate();
        _connection.Dispose();
        _gate.Dispose();
        _disposed = true;
        _log.LogDebug("SQLite connection disposed");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        UnhookUpdate();
        await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
        _disposed = true;
        _log.LogDebug("SQLite connection disposed (async)");
    }

    // ---- private un-gated workers ---------------------------------------------
    // These do the real work on the connection and MUST be called with the gate
    // already held (by a public method, or by WithTransactionAsync, or via the
    // _inTransaction re-entrant path). They never acquire the gate themselves, so
    // a transaction can run several of them under a single acquisition.

    private async Task ExecuteScriptCoreAsync(string sqlText, CancellationToken ct)
    {
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

    private async Task<int> ExecuteNonQueryCoreAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct)
    {
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

    private async Task<IReadOnlyList<T>> QueryCoreAsync<T>(
        string sql,
        Func<DbDataReader, T> mapper,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct)
    {
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

    private async Task<T?> QueryScalarCoreAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct)
    {
        await using var cmd = BuildCommand(sql, parameters);
        try
        {
            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (raw is null || raw is DBNull) return default;
            // InvariantCulture: harmless for the current <long>/<string> callers, but
            // future-proofs a string<->number conversion (e.g. QueryScalarAsync<double>
            // against a TEXT column) against a comma-decimal locale mis-parsing the value.
            return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (SqliteException ex)
        {
            _log.LogError(ex, "QueryScalarAsync failed: {Sql}", sql);
            throw;
        }
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
    /// <remarks>
    /// Fires SYNCHRONOUSLY on the writing thread while the connection gate is
    /// held. <see cref="RowChanged"/> subscribers therefore run inside the lock
    /// and MUST NOT call back into this database (see <see cref="ISqliteDatabase.RowChanged"/>).
    /// </remarks>
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

    /// <summary>
    /// Un-gated <see cref="ISqliteTransaction"/> handle handed to a
    /// <see cref="WithTransactionAsync{T}"/> body. Forwards to the owning
    /// database's private <c>*CoreAsync</c> workers so the statements run on the
    /// connection inside the already-held gate + open transaction, without
    /// re-acquiring the gate.
    /// </summary>
    private sealed class Transaction : ISqliteTransaction
    {
        private readonly SqliteDatabase _db;

        public Transaction(SqliteDatabase db) => _db = db;

        public Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
            => _db.ExecuteNonQueryCoreAsync(sql, parameters, ct);

        public Task ExecuteScriptAsync(string sqlText, CancellationToken ct = default)
            => _db.ExecuteScriptCoreAsync(sqlText, ct);

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> mapper, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
            => _db.QueryCoreAsync(sql, mapper, parameters, ct);

        public Task<T?> QueryScalarAsync<T>(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
            => _db.QueryScalarCoreAsync<T>(sql, parameters, ct);
    }
}
