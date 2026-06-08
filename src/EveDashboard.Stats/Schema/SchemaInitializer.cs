namespace EveDashboard.Stats.Schema;

using System.Globalization;
using EveDashboard.Core.Storage;

/// <summary>
/// Creates the EVE Dashboard session schema in a target SQLite database.
/// Idempotent — safe to run on an existing database.
/// </summary>
/// <remarks>
/// Schema is the canonical version captured in <c>docs/session-schema.md</c>. If
/// you change the DDL here, update the doc in the same change.
/// </remarks>
public static class SchemaInitializer
{
    /// <summary>
    /// Run pragmas + DDL, then apply outstanding migrations and bump
    /// <c>schema_version</c> in the <c>setting</c> table.
    /// </summary>
    /// <param name="target">Database to initialise.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Assumed single-threaded at app startup before any concurrent access.
    /// Pragmas: <c>journal_mode = WAL</c> persists in the SQLite header;
    /// <c>foreign_keys = ON</c> is per-connection (the wrapper holds one
    /// connection). v1 ships zero migrations — the migration loop is plumbing
    /// for v2+.
    /// </remarks>
    public static async Task EnsureSchemaAsync(ISqliteDatabase target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        await target.ExecuteScriptAsync(
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            """, ct);

        await target.ExecuteScriptAsync(SchemaDdl, ct);

        // Migrations baseline — v1 ships zero migrations; framework is plumbing for v2+.
        var current = await GetCurrentSchemaVersionAsync(target, ct);
        foreach (var m in Migrations.All
                     .Where(m => m.TargetVersion > current)
                     .OrderBy(m => m.TargetVersion))
        {
            await m.Up(target, ct);
            await SetSchemaVersionAsync(target, m.TargetVersion, ct);
        }

        // First-launch: schema_version not yet present -> seed at 1.
        if (current == 0)
        {
            await SetSchemaVersionAsync(target, 1, ct);
        }
    }

    private static async Task<int> GetCurrentSchemaVersionAsync(ISqliteDatabase t, CancellationToken ct)
    {
        var raw = await t.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'schema_version'", ct: ct);
        return raw is null ? 0 : int.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static Task SetSchemaVersionAsync(ISqliteDatabase t, int v, CancellationToken ct) =>
        t.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ('schema_version', $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new Dictionary<string, object?> { ["$v"] = v.ToString(CultureInfo.InvariantCulture) },
            ct);

    /// <summary>
    /// The canonical session-schema DDL. Mirrors <c>docs/session-schema.md</c>.
    /// Every <c>CREATE</c> uses <c>IF NOT EXISTS</c> so the script is idempotent.
    /// </summary>
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS session (
            session_id TEXT PRIMARY KEY,
            name       TEXT NOT NULL,
            notes      TEXT
        );

        CREATE TABLE IF NOT EXISTS session_time (
            session_time_id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id      TEXT    NOT NULL REFERENCES session(session_id),
            start_time      TEXT    NOT NULL,
            stop_time       TEXT,
            is_pause        INTEGER NOT NULL CHECK (is_pause IN (0, 1))
        );
        CREATE INDEX IF NOT EXISTS idx_session_time_session_id ON session_time(session_id);

        CREATE TABLE IF NOT EXISTS pilot (
            pilot_id INTEGER PRIMARY KEY,
            name     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS session_pilot (
            session_id TEXT    NOT NULL REFERENCES session(session_id),
            pilot_id   INTEGER NOT NULL REFERENCES pilot(pilot_id),
            role       TEXT    NOT NULL DEFAULT 'Miner'
                               CHECK (role IN ('Miner','Booster','Cyno','Combat')),
            PRIMARY KEY (session_id, pilot_id)
        );
        -- Plan 38: sha256 ids are not chronological, so the old
        -- (pilot_id, session_id DESC) index no longer accelerates the
        -- default-to-last-role lookup (now ordered by session_time.start_time).
        -- Keep a plain per-pilot seek.
        CREATE INDEX IF NOT EXISTS idx_session_pilot_pilot
            ON session_pilot(pilot_id);

        CREATE TABLE IF NOT EXISTS log (
            log_id        INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id    TEXT    NOT NULL REFERENCES session(session_id),
            pilot_id      INTEGER NOT NULL REFERENCES pilot(pilot_id),
            eve_timestamp TEXT    NOT NULL,
            event_kind    TEXT    NOT NULL CHECK (event_kind IN ('Tick','Crit','Residue','Depleted')),
            type_id       INTEGER,
            units         INTEGER,
            volume_m3     REAL,
            role          TEXT    NOT NULL DEFAULT 'Miner'
                                  CHECK (role IN ('Miner','Booster','Cyno','Combat'))
        );
        CREATE INDEX IF NOT EXISTS idx_log_session_pilot  ON log(session_id, pilot_id);
        CREATE INDEX IF NOT EXISTS idx_log_session_typeid ON log(session_id, type_id);

        CREATE TABLE IF NOT EXISTS setting (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS market_stations (
            station_id   INTEGER PRIMARY KEY,
            system_name  TEXT    NOT NULL,
            station_name TEXT    NOT NULL,
            region_id    INTEGER,
            region_name  TEXT
        );

        CREATE TABLE IF NOT EXISTS market_data (
            marketdata_id INTEGER PRIMARY KEY AUTOINCREMENT,
            type_id       INTEGER NOT NULL,
            station_id    INTEGER NOT NULL REFERENCES market_stations(station_id),
            session_id    TEXT    NOT NULL,
            fetched_at    TEXT    NOT NULL,
            isk_buy       REAL,
            isk_sell      REAL
        );
        -- Plan 38 (amended): prices are session-bound and station-blind on read.
        -- The lookup is "latest price for (session_id, type_id)"; station_id is
        -- fetch-only provenance, never a read filter, so it is NOT in this index.
        CREATE INDEX IF NOT EXISTS idx_market_data_lookup
            ON market_data(session_id, type_id, fetched_at DESC);
        """;
}
