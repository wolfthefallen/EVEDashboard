namespace EveDashboard.Sde.Schema;

using System.Data.Common;
using EveDashboard.Core.Storage;

/// <summary>
/// Creates the EVE SDE schema in a target SQLite database. Idempotent —
/// safe to run on an existing database (no-op if tables / indexes / pragmas
/// are already in place).
/// </summary>
/// <remarks>
/// Schema is the canonical version captured in <c>docs/sde-schema.md</c>. If
/// you change the DDL here, update the doc in the same change.
/// </remarks>
public static class SchemaInitializer
{
    /// <summary>
    /// Run the full schema DDL: pragmas, tables, indexes, and any retroactive
    /// column additions on the <c>meta</c> table. Safe to call multiple times —
    /// fresh DDL uses <c>IF NOT EXISTS</c> on every <c>CREATE</c>; column-add
    /// migrations swallow the <c>duplicate column</c> error from SQLite.
    /// </summary>
    /// <param name="target">Database to initialise.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Assumed to run single-threaded at app startup before any concurrent
    /// access. Concurrent callers on the same database may race on the
    /// <c>ALTER TABLE</c> statements emitted by <see cref="TryAddColumnAsync"/>.
    /// </remarks>
    public static async Task EnsureSchemaAsync(ISqliteDatabase target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Pragmas first. WAL persists in the SQLite header; foreign_keys must
        // be enabled per-connection (the wrapper holds a single connection).
        await target.ExecuteScriptAsync(
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            """, ct);

        await target.ExecuteScriptAsync(SchemaDdl, ct);

        // Retroactive column additions for databases created before these
        // columns existed (notably the v1-shape shipped fallback). Idempotent —
        // see TryAddColumnAsync for the duplicate-column handling.
        await TryAddColumnAsync(target, "meta", "last_modified", "TEXT", ct);
        await TryAddColumnAsync(target, "meta", "etag",          "TEXT", ct);
    }

    /// <summary>
    /// Add a column to an existing table if it is not already present. SQLite has
    /// no <c>ALTER TABLE … ADD COLUMN IF NOT EXISTS</c>, so we PRE-CHECK the column
    /// list via <c>pragma_table_info</c> and only <c>ALTER</c> when the column is
    /// absent. The defensive catch below covers only the rare concurrent-add race.
    /// </summary>
    /// <param name="target">Database to mutate.</param>
    /// <param name="table">Target table name (not parameterised in the ALTER — caller-controlled identifier).</param>
    /// <param name="column">Column name to add (not parameterised in the ALTER — caller-controlled identifier).</param>
    /// <param name="sqlType">SQLite column type (e.g. <c>TEXT</c>, <c>INTEGER</c>) — caller-controlled identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The pre-check matters because <see cref="EnsureSchemaAsync"/> runs on every
    /// launch: a blind <c>ALTER</c> on an already-migrated DB throws a
    /// <see cref="DbException"/> that <c>SqliteDatabase</c> logs at <c>Error</c>
    /// (with a stack trace) before rethrowing, which would spam the log for every
    /// existing user on every launch. The pre-check avoids the throw entirely on the
    /// common path. Detection in the fallback catch is by exception-message match:
    /// <c>Microsoft.Data.Sqlite</c> raises a <see cref="DbException"/> whose message
    /// contains <c>"duplicate column"</c> when the column already exists.
    /// </remarks>
    private static async Task TryAddColumnAsync(
        ISqliteDatabase target, string table, string column, string sqlType, CancellationToken ct)
    {
        // Pre-check: skip the ALTER (and its logged-then-rethrown exception) when the
        // column already exists. The pragma argument is bound as a parameter.
        var columns = await target.QueryAsync(
            "SELECT name FROM pragma_table_info($t)",
            r => r.GetString(0),
            new Dictionary<string, object?> { ["$t"] = table }, ct);
        if (columns.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase)))
        {
            return; // already present — EnsureSchemaAsync is idempotent, no-op, no log noise
        }

        try
        {
            await target.ExecuteScriptAsync($"ALTER TABLE {table} ADD COLUMN {column} {sqlType}", ct);
        }
        catch (DbException ex)
            when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Lost a concurrent-add race between the pre-check and the ALTER — fine.
        }
    }

    /// <summary>
    /// The canonical SDE schema DDL. Mirrors <c>docs/sde-schema.md</c>.
    /// </summary>
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS meta (
            id               INTEGER PRIMARY KEY CHECK (id = 1),
            build_time       TEXT NOT NULL,
            esi_sha256       TEXT,
            sde_sha256       TEXT,
            hoboleaks_sha256 TEXT,
            last_modified    TEXT,
            etag             TEXT
        );

        CREATE TABLE IF NOT EXISTS categories (
            category_id INTEGER PRIMARY KEY,
            name        TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS groups (
            group_id    INTEGER PRIMARY KEY,
            category_id INTEGER NOT NULL REFERENCES categories(category_id),
            name        TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_groups_category_id ON groups(category_id);

        CREATE TABLE IF NOT EXISTS types (
            type_id      INTEGER PRIMARY KEY,
            group_id     INTEGER NOT NULL REFERENCES groups(group_id),
            name         TEXT    NOT NULL,
            volume       REAL    NOT NULL,
            portion_size INTEGER NOT NULL,
            is_ore       INTEGER NOT NULL CHECK (is_ore IN (0, 1)),
            icon_id      INTEGER
        );
        CREATE INDEX IF NOT EXISTS idx_types_name     ON types(name);
        CREATE INDEX IF NOT EXISTS idx_types_group_id ON types(group_id);
        CREATE INDEX IF NOT EXISTS idx_types_is_ore   ON types(is_ore);

        CREATE TABLE IF NOT EXISTS type_materials (
            type_id          INTEGER NOT NULL REFERENCES types(type_id),
            material_type_id INTEGER NOT NULL REFERENCES types(type_id),
            quantity         INTEGER NOT NULL,
            PRIMARY KEY (type_id, material_type_id)
        );
        CREATE INDEX IF NOT EXISTS idx_type_materials_material_type_id ON type_materials(material_type_id);
        """;
}
