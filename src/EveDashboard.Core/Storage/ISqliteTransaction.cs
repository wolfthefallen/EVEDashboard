namespace EveDashboard.Core.Storage;

using System.Data.Common;

/// <summary>
/// Handle to an in-progress transaction on an <see cref="ISqliteDatabase"/>,
/// passed to the body of <see cref="ISqliteDatabase.WithTransactionAsync"/>.
/// Exposes the same execute/query surface as <see cref="ISqliteDatabase"/>, but
/// its calls run on the connection that is already inside the held connection
/// lock and the open transaction — so they do NOT re-acquire the lock.
/// </summary>
/// <remarks>
/// Lifetime + threading constraints (load-bearing — violating them deadlocks or
/// corrupts the connection):
/// <list type="bullet">
///   <item><description>The handle is valid ONLY for the duration of the <c>body</c> delegate. Do not capture it and call it after <see cref="ISqliteDatabase.WithTransactionAsync"/> returns.</description></item>
///   <item><description>Call it only from the body's own thread/await chain. Do NOT fan transaction work out to other threads — the connection lock guarantees a single writer, and the transaction is bound to this one connection.</description></item>
///   <item><description>Inside the body, use THIS handle — never the outer <see cref="ISqliteDatabase"/>. The outer methods re-acquire the lock the transaction already holds and would self-deadlock.</description></item>
/// </list>
/// </remarks>
public interface ISqliteTransaction
{
    /// <summary>Execute a single non-query inside the transaction. See <see cref="ISqliteDatabase.ExecuteNonQueryAsync"/>.</summary>
    /// <param name="sql">A single SQL statement, optionally with named parameters (<c>$param</c>).</param>
    /// <param name="parameters">Parameter name → value map (include the leading <c>$</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rows affected.</returns>
    Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>Execute a multi-statement script inside the transaction. See <see cref="ISqliteDatabase.ExecuteScriptAsync"/>.</summary>
    /// <param name="sqlText">One or more SQL statements separated by semicolons.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteScriptAsync(string sqlText, CancellationToken ct = default);

    /// <summary>Execute a SELECT inside the transaction. See <see cref="ISqliteDatabase.QueryAsync"/>.</summary>
    /// <typeparam name="T">Row projection type.</typeparam>
    /// <param name="sql">A single SELECT statement, optionally with named parameters.</param>
    /// <param name="mapper">Row projection — invoked once per row with a positioned reader.</param>
    /// <param name="parameters">Parameter name → value map (include the leading <c>$</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Materialised rows in source order.</returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        Func<DbDataReader, T> mapper,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>Execute a scalar SELECT inside the transaction. See <see cref="ISqliteDatabase.QueryScalarAsync"/>.</summary>
    /// <typeparam name="T">Scalar type.</typeparam>
    /// <param name="sql">A single SELECT statement, optionally with named parameters.</param>
    /// <param name="parameters">Parameter name → value map (include the leading <c>$</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scalar value, or <c>default(T)</c> if empty or <c>NULL</c>.</returns>
    Task<T?> QueryScalarAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);
}
