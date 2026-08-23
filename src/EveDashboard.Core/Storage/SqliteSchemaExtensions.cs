namespace EveDashboard.Core.Storage;

using System.Data.Common;

/// <summary>
/// Schema-evolution helpers shared by every schema initialiser / migration
/// that talks to SQLite through <see cref="ISqliteDatabase"/>.
/// </summary>
/// <remarks>
/// Lives in Core because both the session store (Stats) and the SDE store
/// (Sde) need the same <c>ADD COLUMN IF NOT EXISTS</c> emulation and Core is
/// the only project both reference. The methods issue no I/O themselves — they
/// compose calls on the injected <see cref="ISqliteDatabase"/> abstraction.
/// </remarks>
public static class SqliteSchemaExtensions
{
    /// <summary>
    /// Add a column to an existing table if it is not already present. SQLite
    /// has no <c>ALTER TABLE ... ADD COLUMN IF NOT EXISTS</c>, so the column list
    /// is PRE-CHECKED via <c>pragma_table_info</c> and the <c>ALTER</c> runs only
    /// when the column is absent.
    /// </summary>
    /// <param name="db">Database to mutate.</param>
    /// <param name="table">Target table name. Not parameterised in the <c>ALTER</c> — a caller-controlled identifier, never user input.</param>
    /// <param name="column">Column name to add. Not parameterised in the <c>ALTER</c> — a caller-controlled identifier, never user input.</param>
    /// <param name="columnDdl">The column definition after the name, e.g. <c>TEXT</c> or <c>INTEGER NOT NULL DEFAULT 0</c>. Caller-controlled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the column was added; <c>false</c> if it already existed (no-op).</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="db"/> is null.</exception>
    /// <remarks>
    /// The pre-check matters because schema initialisers run on every launch: a
    /// blind <c>ALTER</c> on an already-migrated store throws a
    /// <see cref="DbException"/> that the database wrapper logs at <c>Error</c>
    /// (with a stack trace) before rethrowing — two ERROR lines per launch for
    /// every existing user. The pre-check avoids the throw entirely on the common
    /// path; the defensive catch below covers only the rare race where another
    /// connection adds the same column between the check and the <c>ALTER</c>
    /// (<c>Microsoft.Data.Sqlite</c> reports that as a <see cref="DbException"/>
    /// whose message contains <c>"duplicate column"</c>).
    /// </remarks>
    public static async Task<bool> TryAddColumnAsync(
        this ISqliteDatabase db, string table, string column, string columnDdl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var columns = await db.QueryAsync(
            "SELECT name FROM pragma_table_info($t)",
            static r => r.GetString(0),
            new Dictionary<string, object?> { ["$t"] = table },
            ct).ConfigureAwait(false);
        if (columns.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase)))
        {
            return false; // already present — idempotent no-op, no log noise
        }

        try
        {
            await db.ExecuteScriptAsync($"ALTER TABLE {table} ADD COLUMN {column} {columnDdl}", ct).ConfigureAwait(false);
            return true;
        }
        catch (DbException ex)
            when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            return false; // lost a concurrent-add race between the pre-check and the ALTER — the column exists
        }
    }
}
