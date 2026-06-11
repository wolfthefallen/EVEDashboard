namespace EveDashboard.Stats.Schema;

/// <summary>
/// Registered session-DB migrations, ordered by <see cref="Migration.TargetVersion"/>.
/// A migration runs on an existing DB whose recorded <c>schema_version</c> is
/// below its <see cref="Migration.TargetVersion"/>; a fresh DB skips them (the
/// DDL in <see cref="SchemaInitializer"/> already emits the latest shape, so a
/// fresh DB is stamped straight to the latest version).
/// </summary>
internal static class Migrations
{
    /// <summary>
    /// The migration timeline. Each entry is additive and idempotent
    /// (<c>CREATE TABLE IF NOT EXISTS</c>), so re-running is safe.
    /// </summary>
    public static IReadOnlyList<Migration> All { get; } = new[]
    {
        // v1 -> v2 (plan 42): add the fleet + fleet_pilot tables. Additive only;
        // no existing user data is touched. The same DDL lives in
        // SchemaInitializer.SchemaDdl (which a fresh DB uses), so on an upgrade
        // these CREATE IF NOT EXISTS statements are a no-op after the DDL pass —
        // their job here is to carry the schema_version bump to 2.
        new Migration(
            2,
            "Add fleet + fleet_pilot tables (plan 42)",
            static (db, ct) => db.ExecuteScriptAsync(
                """
                CREATE TABLE IF NOT EXISTS fleet (
                    fleet_id   TEXT PRIMARY KEY,
                    name       TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_fleet_name ON fleet(name);

                CREATE TABLE IF NOT EXISTS fleet_pilot (
                    fleet_id   TEXT    NOT NULL REFERENCES fleet(fleet_id),
                    pilot_id   INTEGER NOT NULL REFERENCES pilot(pilot_id),
                    role       TEXT    NOT NULL DEFAULT 'Miner'
                                       CHECK (role IN ('Miner','Booster','Cyno','Combat')),
                    created_at TEXT    NOT NULL,
                    PRIMARY KEY (fleet_id, pilot_id)
                );
                CREATE INDEX IF NOT EXISTS idx_fleet_pilot_fleet ON fleet_pilot(fleet_id);
                """, ct)),
    };
}
