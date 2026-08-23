namespace EveDashboard.Stats.Schema;

using System.Globalization;
using System.Linq;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;

/// <summary>
/// One-time, in-place migration of a legacy integer-id session database to the
/// content-addressed (<see cref="SessionId"/>) schema: rewrites every
/// <c>session_id</c> from an autoincrement integer to its sha256 hex id, and
/// stamps each <c>market_data</c> row with the session that owns it (by
/// fetched-at containment) — or the <see cref="SessionId.Unassociated"/>
/// sentinel when no single session can claim it.
/// </summary>
/// <remarks>
/// <para>
/// Detection (<see cref="IsLegacyAsync"/>) is structural, not
/// <c>schema_version</c>-gated: a DB is legacy iff the <c>session</c> table
/// exists AND its <c>session_id</c> column is declared <c>INTEGER</c>. A fresh
/// DB (no <c>session</c> table yet) is NOT legacy — the normal
/// <see cref="SchemaInitializer.EnsureSchemaAsync"/> creates the new schema.
/// </para>
/// <para>
/// The caller (the WPF bootstrap) is responsible for taking a file backup and
/// showing the user a message box BEFORE invoking <see cref="MigrateAsync"/>.
/// The migration itself runs in a single <c>BEGIN IMMEDIATE</c> transaction and
/// rolls back on any error. <c>foreign_keys</c> is turned OFF around the
/// table rebuild (it cannot be toggled inside a transaction).
/// </para>
/// <para>
/// Timestamp comparison for the association step is done on parsed
/// <see cref="DateTimeOffset"/> values, NOT lexical strings, so it is immune to
/// the <c>+00:00</c>-vs-<c>Z</c> / fractional-digit format drift seen in real
/// <c>market_data.fetched_at</c> values. Indexes are recreated by the
/// <see cref="SchemaInitializer.EnsureSchemaAsync"/> call the caller runs
/// immediately afterwards (its <c>CREATE INDEX IF NOT EXISTS</c> statements).
/// </para>
/// </remarks>
public static class LegacySessionIdMigration
{
    /// <summary>
    /// True if <paramref name="db"/> is a legacy integer-id database that needs the
    /// sha256-id migration. False for a fresh DB and for an already-migrated DB
    /// (idempotent — safe to call on every launch).
    /// </summary>
    /// <param name="db">The session database to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the <c>session</c> table exists and its <c>session_id</c> column is declared <c>INTEGER</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="db"/> is null.</exception>
    public static async Task<bool> IsLegacyAsync(ISqliteDatabase db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var sessionTableCount = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'session'",
            ct: ct).ConfigureAwait(false);
        if (sessionTableCount == 0)
        {
            return false; // Fresh DB — EnsureSchemaAsync will create the new schema.
        }

        var columns = await db.QueryAsync(
            "PRAGMA table_info(session)",
            static r => (Name: r.GetString(1), Type: r.GetString(2)),
            ct: ct).ConfigureAwait(false);

        var sessionIdColumn = columns.FirstOrDefault(
            c => string.Equals(c.Name, "session_id", StringComparison.OrdinalIgnoreCase));

        // A migrated DB declares session_id TEXT; a legacy one declares INTEGER.
        return sessionIdColumn.Name is not null
            && sessionIdColumn.Type.Contains("INT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rebuild the session/session_time/session_pilot/log/market_data tables
    /// with sha256 <c>session_id</c>s. Assumes <see cref="IsLegacyAsync"/>
    /// returned <c>true</c>. Atomic; rolls back on any error.
    /// </summary>
    /// <param name="db">The legacy session database to migrate in place.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="db"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a session has no <c>session_time</c> rows, or if two sessions hash to the same id (refusing to silently merge).</exception>
    public static async Task MigrateAsync(ISqliteDatabase db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        // foreign_keys must be OFF for the drop/rename rebuild, and the PRAGMA
        // cannot run inside a transaction.
        await db.ExecuteScriptAsync("PRAGMA foreign_keys = OFF;", ct).ConfigureAwait(false);

        await db.ExecuteScriptAsync("BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            // 1. Read sessions + their time rows.
            var sessions = await db.QueryAsync(
                "SELECT session_id, name FROM session",
                static r => (OldId: r.GetInt64(0), Name: r.GetString(1)),
                ct: ct).ConfigureAwait(false);

            var times = await db.QueryAsync(
                "SELECT session_time_id, session_id, start_time, stop_time FROM session_time",
                static r => (
                    SessionTimeId: r.GetInt64(0),
                    SessionOldId:  r.GetInt64(1),
                    StartTime:     r.GetString(2),
                    StopTime:      r.IsDBNull(3) ? null : r.GetString(3)),
                ct: ct).ConfigureAwait(false);

            // First start_time per session = the row with the smallest
            // session_time_id (the Start moment — matches the live + import recipe).
            var firstStartByOldId = times
                .GroupBy(t => t.SessionOldId)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.SessionTimeId).First().StartTime);

            // 2. Compute the old-id -> new-id (sha256) map, with a collision guard.
            var idMap = new Dictionary<long, string>(sessions.Count);
            var seenNewIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                if (!firstStartByOldId.TryGetValue(s.OldId, out var startIso))
                {
                    throw new InvalidOperationException(
                        $"Migration: session {s.OldId} ('{s.Name}') has no session_time rows; cannot compute its id.");
                }
                var newId = SessionId.Compute(s.Name, startIso);
                if (!seenNewIds.Add(newId))
                {
                    throw new InvalidOperationException(
                        $"Migration: two sessions hash to the same id ({newId}); refusing to merge. Restore the .bak backup and report this.");
                }
                idMap[s.OldId] = newId;
            }

            // 3. Per-session [earliest start, latest stop] window (parsed).
            var windows = times
                .GroupBy(t => t.SessionOldId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Start: g.Select(t => ParseTimestamp(t.StartTime)).Min(),
                        End:   g.Select(t => ParseTimestamp(t.StopTime ?? t.StartTime)).Max()));

            // 4. Associate each market_data row by fetched-at containment.
            var market = await db.QueryAsync(
                "SELECT marketdata_id, fetched_at FROM market_data",
                static r => (MarketDataId: r.GetInt64(0), FetchedAt: r.GetString(1)),
                ct: ct).ConfigureAwait(false);

            var marketOwner = new Dictionary<long, string>(market.Count);
            foreach (var row in market)
            {
                var fetched = ParseTimestamp(row.FetchedAt);
                string? owner = null;
                var matchCount = 0;
                foreach (var (oldId, window) in windows)
                {
                    if (fetched >= window.Start && fetched <= window.End)
                    {
                        matchCount++;
                        owner = idMap[oldId];
                        if (matchCount > 1) break;
                    }
                }
                if (matchCount == 1)
                {
                    marketOwner[row.MarketDataId] = owner!;
                }
                // matchCount 0 or >1 -> left out of _md_map -> COALESCEd to the sentinel below.
            }

            // 5. Create the new (TEXT-id) tables + temp id maps.
            await db.ExecuteScriptAsync(CreateNewTablesSql, ct).ConfigureAwait(false);

            foreach (var (oldId, newId) in idMap)
            {
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO _id_map (old_id, new_id) VALUES ($old, $new)",
                    new Dictionary<string, object?> { ["$old"] = oldId, ["$new"] = newId },
                    ct).ConfigureAwait(false);
            }
            foreach (var (marketDataId, sessionId) in marketOwner)
            {
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO _md_map (marketdata_id, session_id) VALUES ($mdid, $sid)",
                    new Dictionary<string, object?> { ["$mdid"] = marketDataId, ["$sid"] = sessionId },
                    ct).ConfigureAwait(false);
            }

            // 6. Copy rows (rewriting session_id), drop old, rename new.
            await db.ExecuteScriptAsync(CopyAndRenameSql, ct).ConfigureAwait(false);

            await db.ExecuteScriptAsync("COMMIT;", ct).ConfigureAwait(false);
        }
        catch
        {
            await db.ExecuteScriptAsync("ROLLBACK;", ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await db.ExecuteScriptAsync("PRAGMA foreign_keys = ON;", ct).ConfigureAwait(false);
        }
    }

    /// <summary>Parse an ISO-8601 timestamp to UTC, tolerant of <c>Z</c> / <c>+00:00</c> / fractional-digit drift.</summary>
    private static DateTimeOffset ParseTimestamp(string iso) =>
        DateTimeOffset.Parse(
            iso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private const string CreateNewTablesSql = """
        CREATE TABLE session_new (
            session_id TEXT PRIMARY KEY,
            name       TEXT NOT NULL,
            notes      TEXT
        );
        CREATE TABLE session_time_new (
            session_time_id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id      TEXT    NOT NULL REFERENCES session(session_id),
            start_time      TEXT    NOT NULL,
            stop_time       TEXT,
            is_pause        INTEGER NOT NULL CHECK (is_pause IN (0, 1))
        );
        CREATE TABLE session_pilot_new (
            session_id TEXT    NOT NULL REFERENCES session(session_id),
            pilot_id   INTEGER NOT NULL REFERENCES pilot(pilot_id),
            role       TEXT    NOT NULL DEFAULT 'Miner'
                               CHECK (role IN ('Miner','Booster','Cyno','Combat')),
            PRIMARY KEY (session_id, pilot_id)
        );
        CREATE TABLE log_new (
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
        CREATE TABLE market_data_new (
            marketdata_id INTEGER PRIMARY KEY AUTOINCREMENT,
            type_id       INTEGER NOT NULL,
            station_id    INTEGER NOT NULL REFERENCES market_stations(station_id),
            session_id    TEXT    NOT NULL,
            fetched_at    TEXT    NOT NULL,
            isk_buy       REAL,
            isk_sell      REAL
        );
        CREATE TEMP TABLE _id_map (old_id INTEGER PRIMARY KEY, new_id TEXT NOT NULL);
        CREATE TEMP TABLE _md_map (marketdata_id INTEGER PRIMARY KEY, session_id TEXT NOT NULL);
        """;

    // The sentinel is a fixed 64-char literal (no quotes), safe to inline.
    private static readonly string CopyAndRenameSql = $"""
        INSERT INTO session_new (session_id, name, notes)
            SELECT m.new_id, s.name, s.notes
            FROM session s JOIN _id_map m ON m.old_id = s.session_id;

        INSERT INTO session_time_new (session_time_id, session_id, start_time, stop_time, is_pause)
            SELECT st.session_time_id, m.new_id, st.start_time, st.stop_time, st.is_pause
            FROM session_time st JOIN _id_map m ON m.old_id = st.session_id;

        INSERT INTO session_pilot_new (session_id, pilot_id, role)
            SELECT m.new_id, sp.pilot_id, sp.role
            FROM session_pilot sp JOIN _id_map m ON m.old_id = sp.session_id;

        INSERT INTO log_new (log_id, session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role)
            SELECT l.log_id, m.new_id, l.pilot_id, l.eve_timestamp, l.event_kind, l.type_id, l.units, l.volume_m3, l.role
            FROM log l JOIN _id_map m ON m.old_id = l.session_id;

        INSERT INTO market_data_new (marketdata_id, type_id, station_id, session_id, fetched_at, isk_buy, isk_sell)
            SELECT md.marketdata_id, md.type_id, md.station_id,
                   COALESCE(mm.session_id, '{SessionId.Unassociated}'),
                   md.fetched_at, md.isk_buy, md.isk_sell
            FROM market_data md LEFT JOIN _md_map mm ON mm.marketdata_id = md.marketdata_id;

        DROP TABLE log;
        DROP TABLE session_pilot;
        DROP TABLE session_time;
        DROP TABLE market_data;
        DROP TABLE session;

        ALTER TABLE session_new       RENAME TO session;
        ALTER TABLE session_time_new  RENAME TO session_time;
        ALTER TABLE session_pilot_new RENAME TO session_pilot;
        ALTER TABLE log_new           RENAME TO log;
        ALTER TABLE market_data_new   RENAME TO market_data;

        DROP TABLE _id_map;
        DROP TABLE _md_map;
        """;
}
