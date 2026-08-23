namespace EveDashboard.Sde.Schema;

using EveDashboard.Core.Storage;

/// <summary>
/// Creates the EVE SDE schema in a target SQLite database. Idempotent —
/// safe to run on an existing database (no-op if tables / indexes / pragmas
/// are already in place).
/// </summary>
/// <remarks>
/// The DDL in <c>SchemaDdl</c> is the single source of truth for the SDE
/// schema; both the fallback build and the runtime updater write against it.
/// </remarks>
public static class SchemaInitializer
{
    /// <summary>
    /// Run the full schema DDL: pragmas, tables, indexes, and any retroactive
    /// column additions on the <c>meta</c> table. Safe to call multiple times —
    /// fresh DDL uses <c>IF NOT EXISTS</c> on every <c>CREATE</c>; column-add
    /// migrations pre-check the column list and skip the <c>ALTER</c> when present.
    /// </summary>
    /// <param name="target">Database to initialise.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Assumed to run single-threaded at app startup before any concurrent
    /// access. Concurrent callers on the same database may race on the
    /// <c>ALTER TABLE</c> statements emitted by <see cref="SqliteSchemaExtensions.TryAddColumnAsync"/>.
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
        // the shared helper pre-checks pragma_table_info before the ALTER.
        await target.TryAddColumnAsync("meta", "last_modified", "TEXT", ct);
        await target.TryAddColumnAsync("meta", "etag",          "TEXT", ct);
    }

    /// <summary>
    /// The canonical SDE schema DDL: the single-row <c>meta</c> table plus
    /// <c>categories</c>, <c>groups</c>, <c>types</c>, and <c>type_materials</c>.
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
