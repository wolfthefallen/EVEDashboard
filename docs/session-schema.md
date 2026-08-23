# EVE Dashboard — Session Schema

The shape of the SQLite database that holds **session / pilot / event / settings** data at runtime. Companion file to [`sde-schema.md`](sde-schema.md) (which covers the SDE store). Canonical reference for any code that reads, writes, or migrates the session store.

**Source:** the application itself. The session store has no upstream — every row originates from this app, written either at session-control time (Start / Pause / Resume / End / pilot selection) or by the live event writer as the parser produces events.

**Why this shape:** SQLite is the live substrate, not just persistence. Every parsed mining event lands in `log` immediately; the wrapper connection's `RowChanged` event (backed by `sqlite3_update_hook`, see [`SqliteDatabase`](../src/EveDashboard.Persistence/Storage/SqliteDatabase.cs)) fires; an in-process C# event notifies widget ViewModels; widgets re-query SQLite to refresh. Stats are always re-derivable from `log` and the current SDE.

---

## Visual diagram

```
┌─────────────────────────────────────────┐
│ session                                 │
│─────────────────────────────────────────│
│ session_id TEXT PK (sha256 hex)         │◄────────┐
│ name        TEXT NOT NULL               │         │
│ notes       TEXT                        │         │ FK
└─────────────────────────────────────────┘         │ (referenced from
                                                     │  session_time,
┌─────────────────────────────────────────┐         │  session_pilot,
│ session_time                            │         │  log)
│─────────────────────────────────────────│         │
│ session_time_id INTEGER PK AUTOINC      │         │
│ session_id      TEXT NOT NULL     FK────┼─────────┤
│ start_time      TEXT NOT NULL           │         │  ISO-8601 UTC string
│ stop_time       TEXT                    │         │  NULL = currently in this state
│ is_pause        INTEGER NOT NULL        │         │  0 = active period, 1 = pause
│ IDX(session_id)                         │         │
└─────────────────────────────────────────┘         │
                                                     │
┌─────────────────────────────────────────┐         │
│ pilot                                   │         │
│─────────────────────────────────────────│         │
│ pilot_id INTEGER PK                     │◄───┐    │  EVE character ID (NOT auto-inc)
│ name      TEXT NOT NULL                 │    │    │  display name from Listener: header
└─────────────────────────────────────────┘    │    │
                                                │    │
┌─────────────────────────────────────────┐    │    │
│ session_pilot                           │    │    │
│─────────────────────────────────────────│    │    │
│ session_id TEXT NOT NULL FK─────────────┼─────────┤
│ pilot_id   INTEGER NOT NULL FK──────────┼────┤    │
│ role       TEXT NOT NULL DEFAULT 'Miner'│    │    │   Miner / Booster / Cyno / Combat
│ PRIMARY KEY (session_id, pilot_id)      │    │    │
│ IDX(pilot_id)                           │    │    │   per-pilot seek (the default-to-last-role lookup it originally backed was removed when fleets arrived; index retained)
└─────────────────────────────────────────┘    │    │
                                                │    │
┌─────────────────────────────────────────┐    │    │
│ log                                     │    │    │
│─────────────────────────────────────────│    │    │
│ log_id        INTEGER PK AUTOINCREMENT  │    │    │
│ session_id    TEXT NOT NULL FK──────────┼─────────┘
│ pilot_id      INTEGER NOT NULL FK───────┼────┘
│ eve_timestamp TEXT NOT NULL             │           ISO-8601 UTC; from [YYYY.MM.DD HH:MM:SS] prefix
│ event_kind    TEXT NOT NULL             │           Tick / Crit / Residue / Depleted
│ type_id       INTEGER                   │           NULL for Residue (the log line carries no ore name)
│ units         INTEGER                   │           raw count from the log line (Residue included)
│ volume_m3     REAL                      │           NULL for Residue; denormalized = units * SDE volume_per_unit
│ role          TEXT NOT NULL DEFAULT     │           event-time role (Miner / Booster / Cyno / Combat)
│               'Miner'                   │
│ IDX(session_id, pilot_id)               │
│ IDX(session_id, type_id)                │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│ setting                                 │
│─────────────────────────────────────────│
│ key   TEXT PK                           │           e.g. "theme", "price_mode", "selected_station_id"
│ value TEXT NOT NULL                     │           JSON for complex values
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│ fleet                        (schema v2)│
│─────────────────────────────────────────│
│ fleet_id   TEXT PK (sha256 hex)         │◄───┐      content hash of name + sorted "pilotId:role"
│ name       TEXT NOT NULL  UNIQUE idx    │    │      trimmed; unique display name
│ created_at TEXT NOT NULL                │    │      ISO-8601 UTC
└─────────────────────────────────────────┘    │ FK
                                                │
┌─────────────────────────────────────────┐    │
│ fleet_pilot                  (schema v2)│    │
│─────────────────────────────────────────│    │
│ fleet_id   TEXT NOT NULL FK─────────────┼────┘
│ pilot_id   INTEGER NOT NULL FK──────────┼──► pilot(pilot_id)   name reused from pilot, not stored here
│ role       TEXT NOT NULL DEFAULT 'Miner'│      Miner / Booster / Cyno / Combat
│ created_at TEXT NOT NULL                │
│ PRIMARY KEY (fleet_id, pilot_id)        │
│ IDX(fleet_id)                           │
└─────────────────────────────────────────┘
```

> *Additional market-domain tables (`market_stations`, `market_data`) live in this same `session.sqlite` file but are documented separately in [`market-schema.md`](market-schema.md) to keep the session/event side and the market side organisationally distinct. Both are emitted by the same canonical `SchemaDdl` in `SchemaInitializer` as the tables below.*

---

## SQL DDL (canonical — what the schema initialiser actually emits)

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE session (
    session_id  TEXT PRIMARY KEY,         -- SHA-256 hex of (name + 0x1F + first start_time); app-computed, NOT autoincrement
    name        TEXT NOT NULL,
    notes       TEXT,
    archived    INTEGER NOT NULL DEFAULT 0 CHECK (archived IN (0, 1)),  -- 1 = hidden from every listing / report (sources of a combined session are archived, never deleted)
    merged_into TEXT                      -- session_id of the combined session an archived source was folded into; NULL otherwise. Plain text, NOT an FK (foreign_keys is ON; an FK would block deleting the combined session)
);

CREATE TABLE session_time (
    session_time_id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT    NOT NULL REFERENCES session(session_id),
    start_time      TEXT    NOT NULL,
    stop_time       TEXT,
    is_pause        INTEGER NOT NULL CHECK (is_pause IN (0, 1))
);
CREATE INDEX idx_session_time_session_id ON session_time(session_id);

CREATE TABLE pilot (
    pilot_id INTEGER PRIMARY KEY,        -- EVE's character ID; NOT autoincrement
    name     TEXT    NOT NULL
);

CREATE TABLE session_pilot (
    session_id TEXT    NOT NULL REFERENCES session(session_id),
    pilot_id   INTEGER NOT NULL REFERENCES pilot(pilot_id),
    role       TEXT    NOT NULL DEFAULT 'Miner'
                       CHECK (role IN ('Miner','Booster','Cyno','Combat')),
    PRIMARY KEY (session_id, pilot_id)
);
CREATE INDEX idx_session_pilot_pilot ON session_pilot(pilot_id);  -- per-pilot seek. The default-to-last-role lookup it originally backed was removed when fleets arrived (a fleet now supplies each pilot's role at Start); index retained as a cheap generic per-pilot seek.

CREATE TABLE log (
    log_id        INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id    TEXT    NOT NULL REFERENCES session(session_id),
    pilot_id      INTEGER NOT NULL REFERENCES pilot(pilot_id),
    eve_timestamp TEXT    NOT NULL,
    event_kind    TEXT    NOT NULL CHECK (event_kind IN ('Tick','Crit','Residue','Depleted')),
    type_id       INTEGER,
    units         INTEGER,
    volume_m3     REAL,
    role          TEXT    NOT NULL DEFAULT 'Miner'                                  -- event-time role
                          CHECK (role IN ('Miner','Booster','Cyno','Combat'))
);
CREATE INDEX idx_log_session_pilot  ON log(session_id, pilot_id);
CREATE INDEX idx_log_session_typeid ON log(session_id, type_id);

CREATE TABLE setting (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Schema v2: saved fleets. Shipped via the v1 -> v2 migration in
-- Migrations.All (additive, CREATE TABLE IF NOT EXISTS); also emitted by the
-- canonical SchemaDdl so a fresh DB creates them directly.
CREATE TABLE fleet (
    fleet_id   TEXT PRIMARY KEY,          -- sha256 hex of (trimmed name + 0x1F + sorted "pilotId:role" list); app-computed
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE UNIQUE INDEX idx_fleet_name ON fleet(name);   -- display names are unique

CREATE TABLE fleet_pilot (
    fleet_id   TEXT    NOT NULL REFERENCES fleet(fleet_id),
    pilot_id   INTEGER NOT NULL REFERENCES pilot(pilot_id),   -- name reused from the pilot table, not duplicated here
    role       TEXT    NOT NULL DEFAULT 'Miner'
                       CHECK (role IN ('Miner','Booster','Cyno','Combat')),
    created_at TEXT    NOT NULL,
    PRIMARY KEY (fleet_id, pilot_id)
);
CREATE INDEX idx_fleet_pilot_fleet ON fleet_pilot(fleet_id);
```

> *Schema versioning: `fleet` + `fleet_pilot` are the first real `schema_version` migration (v1 -> v2); v2 -> v3 adds `session.archived` + `session.merged_into` via a pre-checked `ALTER TABLE ... ADD COLUMN` (a store that already has the columns is a no-op; existing rows read `archived = 0`, `merged_into = NULL`). `schema_version` lives in the `setting` table; a DB with no `schema_version` row is treated as fresh and stamped at the latest version (the canonical `SchemaDdl` already emits these tables); an existing store runs only the migrations in `Migrations.All` whose target version is newer than its recorded value, each stamped as it completes — no existing row is touched. Separately, a database whose `session.session_id` column is still declared `INTEGER` (the pre-content-id shape) is detected structurally at startup and rebuilt in place by [`LegacySessionIdMigration`](../src/EveDashboard.Stats/Schema/LegacySessionIdMigration.cs): every `session_id` is rewritten to its sha256 id, and each `market_data` row is stamped with the one session whose time window contains its `fetched_at` (or the all-zeros `SessionId.Unassociated` sentinel when none or more than one does). That rebuild is one `BEGIN IMMEDIATE` transaction with `foreign_keys` OFF around it, and refuses to run (throws) if a session has no `session_time` row or two sessions hash to the same id.*

---

## How the tables compose

### Session lifecycle

| User action | What gets written |
|---|---|
| Start | A session is started from a **selected fleet** — a fleet is the only start path. The fleet's roster is resolved against a fresh gamelog-discovery snapshot; pilots with no current gamelog are skipped with a Warning (Start is blocked only if none are discoverable). Then compute `session_id = SessionId.Compute(expanded_name, now)` before any INSERT (no `last_insert_rowid`), INSERT into `session` `(session_id, name, notes=NULL)`; for each resolved pilot `INSERT OR IGNORE` into `pilot` `(pilot_id, name)` and INSERT into `session_pilot` with the role **taken straight from the fleet** (there is no default-to-last-role lookup); INSERT into `session_time` `(session_id, start_time=now, stop_time=NULL, is_pause=1)` — Start lands paused so the user can adjust pilot roles before mining counts. The same fleet-role dict is then seeded into the in-memory `ActiveSessionContext` map. All of this runs in one transaction (see [`SessionController.StartAsync`](../src/EveDashboard.App.Wpf/Widgets/Session/SessionController.cs)). |
| Pause | UPDATE the open `session_time` row (`stop_time = now`); INSERT new `session_time` `(session_id, start_time=now, stop_time=NULL, is_pause=1)` |
| Resume | UPDATE the open `session_time` row (`stop_time = now`); INSERT new `session_time` `(session_id, start_time=now, stop_time=NULL, is_pause=0)` |
| Stop | UPDATE the open `session_time` row (`stop_time = now`); no new row. The session is permanently ended — it cannot be re-Started. Clears the in-memory role map so the next session's roles come only from its fleet roster. |
| Right-click pilot widget → role | UPDATE `session_pilot.role` for that pilot in the active session; mirror the change into the in-memory `ActiveSessionContext` map, which raises `RolesChanged`. |

**Not features:** there is no Reset, Reset-this-character, or Recount-from-time-T action. There is no Reset button anywhere in the UI; a session's stats are only ever re-derived from its `log` rows.

### Historical sessions (built after the fact)

A session built from the Reports window's *Build from history* / *Combine selected* is an ordinary `session` row whose `log` rows came from two places - copied verbatim from the saved sessions the user combined (those sources get `archived = 1`, `merged_into = <new session_id>`, and are hidden everywhere) and parsed from the gamelog files for the untracked parts of the window (role = the fleet roster's role; `volume_m3` by the same SDE rule as live rows; a Tick/Crit whose ore the SDE does not know is dropped, exactly like the live writer). A pilot's time inside a session the user declined to combine is excluded from the parse so nothing is counted twice. Its `session_time` rows are a contiguous active/pause timeline: merged sources' own rows govern their spans, and every uncovered gap is paused until the first Tick/Crit, active from there, with a pause synthesised after any gap longer than `auto_pause_idle_minutes` (starting idle-minutes after the last event, when the live auto-pause would have fired). Its `session_id` is `SessionId.Compute(name, first start_time)` like any other session; an existing id is an error, never an overwrite. Deleting a combined session sets its sources back to `archived = 0`, `merged_into = NULL`.

### Active duration

```sql
-- Total active wall-clock for a session (in seconds)
SELECT
    SUM( (julianday(COALESCE(stop_time, CURRENT_TIMESTAMP)) - julianday(start_time)) * 86400 )
FROM session_time
WHERE session_id = :sid AND is_pause = 0;
```

The same query with `is_pause = 1` gives total paused time. "Currently running?" = `EXISTS (SELECT 1 FROM session_time WHERE session_id = :sid AND stop_time IS NULL AND is_pause = 0)`.

### Live event ingestion

The parser produces a `MiningEvent` for each recognized log line. The session-event writer:

1. Resolves `pilot_id` from the gamelog filename's character ID.
2. Resolves `volume_m3` from the SDE lookup (`MiningProduct.VolumePerUnitM3 * units`); for Depleted events it's NULL.
3. Reads the pilot's current role from `ActiveSessionContext.GetRole(pilotId)` (the in-memory map mirrors `session_pilot.role` and is seeded at session start / mutated on user role change).
4. INSERTs one row into `log` including the event-time `role`. (Before this point the writer drops `Tick`/`Crit` events whose `TypeId` is null, and — while the session is paused — drops `Residue`/`Depleted` outright and holds a `Tick`/`Crit` only long enough to auto-resume.)
5. The wrapper connection's `RowChanged` event fires natively (`sqlite3_update_hook`). The session-event writer, listening for INSERTs into `log`, raises a C# `SessionEventAdded(sessionId, logId, kind)` event, isolating each subscriber so one throwing handler cannot starve the rest.
6. Subscribed widget VMs re-query the stats layer for whatever they display.

### Stats queries (illustrative)

> **Note:** the illustrative queries below read raw `log` for clarity. **Production queries wrap every `log`-reading aggregate with the role-eligibility CTE in [`EveDashboard.Core.Storage.RoleEligibilityCte`](../src/EveDashboard.Core/Storage/RoleEligibilityCte.cs)** (selecting `FROM eligible_log` instead of `FROM log`). The CTE excludes Cyno + Combat rows always, and excludes Booster rows until that pilot has any row with `volume_m3 > 0`. See *Role-aware aggregate filter* below.

```sql
-- Total m3 mined this session
SELECT COALESCE(SUM(volume_m3), 0)
FROM log
WHERE session_id = :sid;

-- Per-character m3
SELECT pilot_id, COALESCE(SUM(volume_m3), 0)
FROM log
WHERE session_id = :sid
GROUP BY pilot_id;

-- Crit count + biggest crit (m3)
SELECT COUNT(*) AS crit_count, MAX(volume_m3) AS biggest_crit_m3
FROM log
WHERE session_id = :sid AND event_kind = 'Crit';

-- Asteroids depleted
SELECT COUNT(*) FROM log
WHERE session_id = :sid AND event_kind = 'Depleted';

-- Per-product breakdown (joins SDE for the name)
SELECT log.type_id, COALESCE(SUM(log.volume_m3), 0)
FROM log
WHERE log.session_id = :sid AND log.type_id IS NOT NULL
GROUP BY log.type_id;
```

ISK aggregates run the recursive reprocessing algorithm in [`IskCalculator`](../src/EveDashboard.MarketData/Pricing/IskCalculator.cs) against `SUM(units) GROUP BY type_id` for ore rows, then multiply by cached prices.

### Role-aware aggregate filter

The shared CTE is interpolated in front of every production `log`-reading SELECT:

```sql
WITH eligible_log AS (
    SELECT * FROM log
    WHERE session_id = $sid
      AND role NOT IN ('Cyno','Combat')
      AND (role <> 'Booster'
           OR pilot_id IN (
               SELECT pilot_id FROM log
               WHERE session_id = $sid AND role = 'Booster' AND volume_m3 > 0
           ))
)
SELECT ... FROM eligible_log ...
```

Filter rules:

1. **Cyno** rows → always excluded.
2. **Combat** rows → always excluded.
3. **Booster** rows → excluded unless the pilot has at least one Booster-tagged row with `volume_m3 > 0` in this session (the m³ gate flips live).
4. **Miner** rows → always included.

`session_pilot.role` is the **current** role used to render the per-pilot widget's border + DisplayName. `log.role` is the **event-time** role used for filtering — a pilot who switches role mid-session keeps their pre-switch rows under the old role, and post-switch rows under the new role. This is what makes the Booster m³ gate "re-evaluated each snapshot" rather than frozen.

Queries that read `session_time`, `session_pilot`, or `session` (not `log`) are unchanged — active duration, session timeline, pilot enumeration, etc.

---

## Filtering and conventions

- **`pilot_id` is the EVE character ID.** Not auto-incremented. This is the same ID written into the gamelog filename and used everywhere downstream.
- **All timestamps are TEXT in ISO-8601 UTC** (`YYYY-MM-DDTHH:MM:SS.fffZ`). This sorts lexicographically and is human-readable in the SQLite CLI. The `eve_timestamp` column carries EVE's own `[YYYY.MM.DD HH:MM:SS]` log line prefix, converted to ISO format at parse time.
- **`stop_time IS NULL`** on a `session_time` row means "currently in this state". A session is running iff its newest `session_time` row has `stop_time IS NULL` and `is_pause = 0`. It is paused iff that newest row has `stop_time IS NULL` and `is_pause = 1`. It has ended iff every row has `stop_time IS NOT NULL`.
- **`volume_m3` is denormalized** at write time from the SDE's `volume_per_unit` for the resolved type id. If a future SDE update changes a type's volume, historical rows keep their original m³ — which correctly reflects what was mined at the time, not what the same units would be valued today.
- **`event_kind` is TEXT** rather than an integer enum. Storage cost is negligible at session-data sizes; debuggability in the SQLite CLI wins.
- **`type_id` is nullable.** **`Tick` and `Crit` events have non-null `type_id`** resolved by the SDE lookup (an unresolved product name is logged at Warning by the parser — once per name per session — and the event is still emitted with `TypeId = null`; the session-event writer then drops any `Tick`/`Crit` whose `TypeId` is null before it reaches `log`). **`Residue` events legitimately carry NULL `type_id`** because the EVE residue line carries no ore name (`Additional N units depleted from asteroid as residue`); the parser emits `TypeId = null` and the writer stores `type_id = NULL`, `units = N`, and `volume_m3 = NULL`. **`Depleted`** is a declared `event_kind` value (both the CHECK constraint and the `MiningEventKind` enum carry it) but the parser never emits it — no real EVE log line for it has been captured — so no `Depleted` row is written today.
- **`role` is non-null TEXT** on both `session_pilot` and `log`, constrained to `'Miner'` / `'Booster'` / `'Cyno'` / `'Combat'`. `session_pilot.role` carries the **current** role of each pilot in the session; `log.role` carries the **event-time** role (the role this pilot held at the moment this event was INSERTed). The DEFAULT clause is `'Miner'` and is defensive — JSON exports written before the `role` field existed round-trip via custom `JsonConverter`s that default missing values to `"Miner"` (see [`LogRowJsonConverter`](../src/EveDashboard.SessionIO/Json/LogRowJsonConverter.cs)).
- **`setting.value` is TEXT** with JSON encoding for complex values. Seeded defaults come from `SqliteSettingsRepository.EnsureSeedDefaultsAsync`; every other key is absent until first written, and each getter treats a missing or malformed value as "use the default" (logging a Warning for malformed). The `setting` keys: `theme` (`"dark"` [default]/`"light"`, plain text), `price_mode` (`"buy"` [default]/`"sell"`/`"split"`, plain text), `price_source` (`"goonmetrics"` [default] / `"esi"`, plain text; which market service the fetcher calls), `refining_yield_percent` (a JSON number, seeded `90.6`), `selected_station_id` (a JSON number long, FK by convention to `market_stations.station_id`; seeded `60003760`, Jita), `auto_pause_idle_minutes` (integer `"1"`..`"60"`, default `"5"`; out-of-range values are clamped and a malformed value returns 5), `auto_pause_sound_enabled` (`"true"`/`"false"`, default `"true"`), `auto_pause_sound_volume` (integer percent `"0"`..`"100"`, default `"65"`; the playback volume of the auto-pause / auto-resume notification sounds; out-of-range values are clamped and a malformed value returns 65), `window_geometry` (a JSON `WindowGeometry` object round-tripped via `System.Text.Json`; absent = OS default placement), `reports_window_geometry` (same shape; absent = Reports window default 1100x700 centered), `crystal_diagram_window_geometry` (same shape; absent = Crystal Diagram default 900x650 centered), `lifetime_totals_window_geometry` (same shape; absent = Lifetime Totals default 700x640 centered), `about_window_geometry` (same shape; absent = About default 460x560 centered), `reports_sidebar_width` (a plain number; absent = Reports default 280 px), `reports_column_widths.<gridKey>` (a JSON object `{"<columnIndex>": <width>}` per Reports grid type; absent = the XAML default column widths), `reports_text_size.<widgetTypeKey>` (a JSON object `{"<elementKey>": "<StatEmphasis name>"}` per Reports widget type — emphasis is stored by enum NAME so the JSON survives member reordering; absent = all elements at `None`), `crystal_diagram_selected_anomaly` (plain text; absent = the "all" entry), `widget_layout` (a JSON object `{"items":[...]}` carrying the user's dashboard widget order + per-item hidden flag; per item shape is `{"kind":"session_stats","hidden":false}` or `{"kind":"pilot","pilot_id":<long>,"hidden":<bool>}`; absent/null on first launch = "use default layout"; ghost entries for inactive pilots are preserved indefinitely; an unknown `kind` — including the removed `"session_config"` kind from older layouts — fails to deserialise, is logged at Warning, and the layout falls back to default: a deliberate one-time layout reset in place of a migration for that shape change — see [`WidgetLayoutItemJsonConverter`](../src/EveDashboard.Core/Layout/WidgetLayoutItemJsonConverter.cs)), `schema_version` (a JSON number int, owned by the schema initialiser / migrations — see *Schema versioning* above; currently `3`), `session_stats_config` (a JSON array `[{"stat":"<key>","hidden":<bool>,"emphasis":"<level>"}, ...]` carrying the user's stat row order + hide flag + emphasis level for the singleton SessionStats widget; absent/null on first launch = "use default order with all visible at default emphasis"; a missing `emphasis` field defaults to `None` and the renderer falls back to the property's `[StatRow] DefaultEmphasis`, a missing `hidden` defaults to `false`, and a missing `stat` is malformed), `per_pilot_stats_config.<pilot_id>` (same JSON array shape as `session_stats_config`, but one row per pilot id keyed by the EVE character id; rows for pilots no longer in any active session are kept indefinitely so a re-selected pilot recovers its customisation), `non_sticky_station_defaults_seeded` (sentinel `"1"`, owned by `SqliteMarketStationCatalog`), `default_fleet_id` (the `fleet.fleet_id` of the default fleet auto-selected when the New Session window opens; the row is **absent** when there is no default, since `setting.value` is NOT NULL — clearing the default DELETEs the row), `pilot_sort_order` (the persisted fleet-builder pilot-picker sort; one of `"LastActiveNewest"` [default] / `"LastActiveOldest"` / `"NameAsc"` / `"NameDesc"`; absent or unrecognised = the default `LastActiveNewest`), `auto_show_refined_materials` (`"true"`/`"false"`, default `"true"`; whether a newly-produced refined material auto-appears in the live widgets' "Refined output" section), `refined_materials_visibility` and `refined_materials_visibility.<pilot_id>` (per-widget explicit refined-material visibility overrides; a JSON object `{"<materialTypeId>": true|false}`; absent = no overrides; a material absent from the map follows the auto-show default, a `false` entry is a sticky hide, a `true` an explicit show; the unsuffixed key is the Session widget, the `.<pilot_id>` suffix a per-pilot widget), `show_pilot_refined_output` (GitHub issue #1 — `"true"`/`"false"`, default `"true"`; whether the live "Refined output" section renders on the per-pilot widgets; the Session widget is never gated by it). Market stations are not stored in `setting`; they live in the normalised `market_stations` table. See [`market-schema.md`](market-schema.md).

---

## Portable export format

The app ships a JSON export/import path and an Excel export path. The canonical reference for the on-disk JSON shape is the `EveDashboard.SessionIO` project — see [`SessionExportEnvelope.cs`](../src/EveDashboard.SessionIO/Json/SessionExportEnvelope.cs) for the envelope (a `_manifest` block plus one array per table: `session`, `session_time`, `pilot`, `session_pilot`, `log`, `market_stations`, `market_data`) and [`SessionExportData.cs`](../src/EveDashboard.SessionIO/Tables/SessionExportData.cs) for the per-row records (one record per table here, with property names mapped to the SQLite column names via `JsonNamingPolicy.SnakeCaseLower`).

Highlights:

- One session per file; multi-session bundles are out of scope (the importer throws on a file with any other session-row count).
- `session_id` is the SHA-256 content id (see the top of this doc). It round-trips verbatim when it already looks like a sha256. **Legacy files** whose `session_id` is a bare integer (or absent) have it recomputed on import via `SessionId.Compute(name, first start_time)` (first = the `session_time` row with the smallest `session_time_id`). The still-autoincremented PKs (`session_time_id`, `log_id`, `marketdata_id`) are regenerated on import; FKs onto `session_id` are rewritten to the (declared or computed) sha256 id.
- Re-import dedup: if the destination DB already holds a session with the same `session_id`, the import is a **skip-with-warning no-op** (nothing written; `ImportResult.AlreadyPresent = true`). Re-importing your own backup is safe.
- EVE-side stable IDs (`pilot.pilot_id`, `market_stations.station_id`) are preserved; existing rows are skipped on collision (`INSERT OR IGNORE`) and a Warning is logged — and an `ImportResult.Warnings` entry recorded — if the imported display name differs.
- `_manifest.schema_version` carries the export-FORMAT version (the on-disk JSON shape), the constant `SessionExportFormat.CurrentVersion` (currently `1`), decoupled from the DB `schema_version`. Import rejects only a file whose format version is NEWER than this app supports (throws `SchemaVersionMismatchException`); an equal-or-older file imports. This is why the v1->v2 schema bump (fleet tables) does NOT break import of older exports — the export shape is unchanged, so the format version stays 1. The v2->v3 bump adds two OPTIONAL fields to each `session` row (`archived`, `merged_into`); a file without them imports as `archived = 0`, `merged_into = NULL`, and a file with them keeps them (an imported archived session stays hidden), so the format version still stays 1. The wire field name is `schema_version`.
- `market_data` rows in scope are exactly the session's own rows (`WHERE session_id = <this session>`) — prices are session-bound, so the session owns them directly. Imported price rows are stamped with the (declared or computed) session id, keeping overlapping imports isolated.
- `log.role` and `session_pilot.role` ride along on every row. **JSON files written before the `role` field existed (which lack it) reimport cleanly** — [`LogRowJsonConverter`](../src/EveDashboard.SessionIO/Json/LogRowJsonConverter.cs) and [`SessionPilotRowJsonConverter`](../src/EveDashboard.SessionIO/Json/SessionPilotRowJsonConverter.cs) default missing `role` to `"Miner"` at deserialise time, and tolerate a numeric or string `session_id` token (the per-row session_id is rewritten on import regardless).

Excel export is a one-way human-readable artifact (no import path): one tab per database table, a Summary tab first with name / start / stop / active+paused durations / totals + per-pilot + per-ore + Total-Refined-Output blocks, and one tab per pilot with their ore breakdown + refined-output table. The refined-output blocks (quantity + ISK per material) reuse the same `SessionStats.RefinedOutput` / `CharacterStats.RefinedOutput` the on-screen Lifetime Totals / compare tables use — no separate export aggregation.

The same `EveDashboard.SessionIO` project also ships a [`SessionTableDeleter`](../src/EveDashboard.SessionIO/Tables/SessionTableDeleter.cs) (Reports-window right-click -> Delete...). It removes the session row plus every row in `session_time`, `session_pilot`, and `log` for that session (child tables first, parent last, so `PRAGMA foreign_keys = ON` does not refuse the parent delete), inside one atomic `BEGIN IMMEDIATE`/`COMMIT`/`ROLLBACK` block. It deliberately does NOT touch `pilot` or `market_stations` (EVE-stable / user-catalog rows). Although `market_data` carries a `session_id`, the deleter does NOT remove those rows either — market_data accumulates forever by design, and the leftover rows simply become orphans (a `session_id` matching no `session` row). The orphans are harmless: `session_id` on `market_data` is a plain TEXT column (not an enforced FK) and no query reads a deleted session's prices.

---

## What we deliberately skipped

- **No separate `pause_window` table** — `session_time` with `is_pause` already carries that data.
- **No `pilot.is_active` flag** — "active" pilots are those in `session_pilot` for the currently-active session. There is no global "active" default; pilot selection is always per-session.
- **No `created_at` / `updated_at` audit columns** — the `session_time` rows already serve as the timeline; per-row write timestamps are not used by any v1 feature.
- **Market price tables** (`market_stations` + `market_data`) live in this same database file and are documented in [`market-schema.md`](market-schema.md).
- **No SDE tables here** — the SDE lives in its own SQLite file under `%APPDATA%\EveDashboard\sde\`. See [`sde-schema.md`](sde-schema.md).
