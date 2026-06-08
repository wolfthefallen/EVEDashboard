# EVE Dashboard — Market Schema

The shape of the **market price data** persisted by the app at runtime. Companion to [`session-schema.md`](session-schema.md) (sessions / pilots / events / settings) and [`sde-schema.md`](sde-schema.md) (the EVE Static Data Export). Canonical reference for any code that fetches, reads, or writes market price rows.

**Source:** the GoonMetrics API at `https://goonmetrics.apps.gnf.lt/api/price_data/?station_id=<id>&type_id=<csv>` (no auth, no documented rate limits per `MEMORY.md → project_pricing_model`).

**Why this shape:** v1 ISK valuation needs prices for the type_materials reachable from each mined ore. Per the user's 2026-05-02 alignment, prices are persisted in the session SQLite database (NOT a transient cache) so v2 reporting can review the prices that applied at any point in the past. v1 has no auto-refresh — prices fetch at session start (and lazily on-demand when a new type_material is mined that wasn't pre-fetched). v2 will add auto-refresh; v3 will add gas pricing.

---

## Visual diagram

```
┌─────────────────────────────────────────┐
│ market_stations                         │
│─────────────────────────────────────────│
│ station_id   INTEGER PK                 │◄────────┐  EVE station id (e.g. 60003760 = Jita IV-Moon-4)
│ system_name  TEXT    NOT NULL           │         │
│ station_name TEXT    NOT NULL           │         │  EVE station name without the system prefix
│ region_id    INTEGER                    │         │  Nullable - user-added stations may not know region
│ region_name  TEXT                       │         │  Nullable
└─────────────────────────────────────────┘         │
                                                     │ FK target
┌─────────────────────────────────────────┐         │ (intra-DB FK enforced)
│ market_data                             │         │
│─────────────────────────────────────────│         │
│ marketdata_id INTEGER PK AUTOINCREMENT  │         │
│ type_id       INTEGER NOT NULL          │         │  Informational FK to SDE types.type_id (cross-DB, not enforced)
│ station_id    INTEGER NOT NULL FK───────┼─────────┘  FK to market_stations.station_id (intra-DB, enforced)
│ session_id    TEXT    NOT NULL          │            Plan 38: owning session's sha256 id (or the 64-zero sentinel for unassociated migrated rows). Plain TEXT, NOT an enforced FK.
│ fetched_at    TEXT    NOT NULL          │            ISO-8601 UTC; the wall-clock time the price was fetched
│ isk_buy       REAL                      │            Nullable - GoonMetrics may return only buy or only sell
│ isk_sell      REAL                      │            Nullable - GoonMetrics may return only buy or only sell
│ IDX(session_id, type_id, fetched_at DESC)           │  Latest-price-for-type-in-session lookup (station-blind)
└─────────────────────────────────────────┘
```

**`session_id` column** on `market_data` (plan 38 — reverses the original round-7 1.1 = A "global rows" decision): prices are **bound to the session that fetched them**. Every row carries the sha256 id of its owning session; the ISK lookup reads only that session's rows. This makes a saved session's ISK permanently reproducible — it can never be re-priced by a later session's fetches (the bug this fixes). Migrated rows that could not be associated with any session carry the 64-zero `Unassociated` sentinel and feed no session's ISK. `session_id` is a plain TEXT column, NOT an enforced foreign key (so deleting a session leaves harmless orphan price rows).

**`market_stations` is the user-configurable list** of stations the app may fetch GoonMetrics prices from. v1 ISK calculation reads ONE station from this catalog (the id selected via `setting.selected_station_id`); the catalog itself can hold any number of rows. Default seeding behaviour and CRUD lifecycle are documented under *How the tables compose* below.

---

## SQL DDL (canonical)

```sql
-- Plan 14: market_stations - shipped as part of the canonical SchemaInitializer.SchemaDdl
-- (pre-release schema rebuild; no migration entry, schema_version stays at 1).
CREATE TABLE market_stations (
    station_id   INTEGER PRIMARY KEY,
    system_name  TEXT    NOT NULL,
    station_name TEXT    NOT NULL,
    region_id    INTEGER,
    region_name  TEXT
);

-- market_data: shipped by plan 18 (GoonMetrics fetcher + ISK pipeline).
-- The FK on station_id is intra-DB enforced (market_stations is in the same DB).
CREATE TABLE market_data (
    marketdata_id INTEGER PRIMARY KEY AUTOINCREMENT,
    type_id       INTEGER NOT NULL,
    station_id    INTEGER NOT NULL REFERENCES market_stations(station_id),
    session_id    TEXT    NOT NULL,   -- plan 38: owning session's sha256 id (or the 64-zero Unassociated sentinel)
    fetched_at    TEXT    NOT NULL,
    isk_buy       REAL,
    isk_sell      REAL
);
-- Plan 38 (amended): the read is station-blind, so station_id is not in the index.
CREATE INDEX idx_market_data_lookup
    ON market_data(session_id, type_id, fetched_at DESC);
```

Database location: `session.sqlite` (alongside `session`, `session_time`, `pilot`, `session_pilot`, `log`, `setting`, `market_stations`). `market_stations` is created by `EveDashboard.Stats.Schema.SchemaInitializer.EnsureSchemaAsync` (plan 14); `market_data` is also created by `SchemaInitializer.EnsureSchemaAsync` (plan 18 appended it to `SchemaDdl`).

---

## How the tables compose

### `market_stations` — lifecycle

| Trigger | What gets written |
|---|---|
| `IMarketStationCatalog.EnsureSeedDefaultsAsync` on a fresh DB (first launch via plan 16's bootstrap chain) | INSERT Jita (`60003760`) + INSERT C-J6MT (`1049588174021`) + INSERT sentinel `non_sticky_station_defaults_seeded = '1'` into `setting`. |
| `EnsureSeedDefaultsAsync` on a subsequent launch | `INSERT OR IGNORE` Jita (sticky default — re-seeded if the user deleted it). C-J6MT skipped because the sentinel is present. |
| User adds a station via Settings UI (plan 17) | `IMarketStationCatalog.AddAsync` -> INSERT one row. |
| User edits a station via Settings UI (plan 17) | `IMarketStationCatalog.UpdateAsync` -> UPDATE the existing row. |
| User deletes a station via Settings UI (plan 17) | `IMarketStationCatalog.RemoveAsync` -> DELETE the row. C-J6MT will NOT be re-seeded by `EnsureSeedDefaultsAsync` because the sentinel persists. Jita WILL be re-seeded on next launch. |

**Default seeding policy** (per plan 14 round-2 1.5):
- **Jita** (`60003760`) is the trade-hub baseline. Re-seeded every launch via `INSERT OR IGNORE` so the user always has a working default.
- **C-J6MT** (`1049588174021`) is a player-owned nullsec station that can be destroyed in-game. Seeded once on first launch; never re-seeded after the sentinel is written. If the user removes it from the catalog, the user's choice persists.

The catalog cross-touches the `setting` table for the `non_sticky_station_defaults_seeded` sentinel; this is the only `setting` key written by `IMarketStationCatalog`.

### `market_data` — lifecycle

| Trigger | What gets written |
|---|---|
| User clicks Start (session begins) | INSERT a row per `(type_id, station_id)` pair for every common ore type_material × every configured station, each stamped with the new session's `session_id`. Plan 38: this start fetch is **unconditional** — the 60s throttle is bypassed so the session owns accurate prices from t0. ~35 type_materials × N stations per start. |
| Mining event lands and the (live) ISK calculator finds no row for a type_material it needs | INSERT a row per `(type_id, station_id)` for the missing type_material, stamped with the active `session_id`, on-demand. Plan 38: the lazy throttle is session-scoped (`AND session_id = active`) so a back-to-back session is never starved by a previous session's "fresh" rows. |
| Reports / any saved (non-live) session | Plan 38: **NO fetch, ever.** The calculator runs with `allowLazyFetch = false`; a missing price contributes 0. This is what keeps a saved session's ISK permanently stable. |
| GoonMetrics is unreachable | NO row written; ISK calculation gracefully degrades for that item (returns 0, logs Warning). Does NOT crash the app. No retry/backoff. |
| Session ends / user clicks Stop | NO write. Existing rows persist (now permanently owned by that `session_id`). |
| App shutdown | NO write. Existing rows persist. |

There is **no UPDATE** path — every fetch creates a new row. Plan 38: the latest row for a `(session_id, type_id, station_id)` triple wins for that session's ISK. Older rows within the same session stay around (e.g. a start prefetch plus a later lazy fetch); cross-session, each session only ever sees its own rows.

### Read patterns

```sql
-- Plan 38 (amended): latest price for a type WITHIN one session (the ISK
-- calculator's only price query — live AND reports use this exact shape).
-- STATION-BLIND: station_id is NOT a filter. A session's ISK depends only on
-- the prices that session captured, never on which station is selected now.
SELECT isk_buy, isk_sell
FROM market_data
WHERE session_id = :sid AND type_id = :tid
ORDER BY fetched_at DESC
LIMIT 1;
```

Because prices are session-bound, "what prices applied during session X" is now a trivial `WHERE session_id = X` — no time-window join is needed, and the old `fetched_at <= :session_end` query is retired. A saved session always re-prices to the same total because it reads only its own rows and Reports never fetch new ones (a missing row contributes 0). The read also does **not** filter on `station_id`: `selected_station_id` only tells the fetcher which hub to query, so once a price is captured it belongs to its session and is read back regardless of the currently-selected station — changing your fetch station never alters a saved session's ISK.

---

## Filtering and conventions

- **`type_id`** is the EVE inventory type id. The "common" type_materials prefetched at session start are the union of `type_materials.material_type_id` for every ore in the SDE — ~35 entries in v1 (8 standard minerals + 20 moon materials + 7 ice products, per `MEMORY.md → project_pricing_model`).
- **`type_id` is NOT a foreign key** in the SQL sense. The `types` table lives in `sde.sqlite` (a different database file); SQLite does not enforce cross-database foreign keys. The relationship is informational. Application code is responsible for ensuring referenced type_ids exist in the SDE before inserting.
- **`station_id`** on `market_data` is an **intra-DB FK** to `market_stations.station_id` (the FK is intra-DB enforced). v1 defaults seeded into `market_stations`: `60003760` (Jita - "IV - Moon 4 - Caldari Navy Assembly Plant", region "The Forge" / `10000002`) and `1049588174021` (C-J6MT - "1st Taj Mahgoon", region "Insmother" / `10000009`). The `selected_station_id` setting key (default = Jita) selects which station the fetcher pulls prices **from**; plan 38 (amended) does **not** use it when reading prices for ISK — the read is station-blind (see *Read patterns*). `station_id` is kept on every row as provenance for a future "which hub did these prices come from" display.
- **`fetched_at`** is `TEXT` in ISO-8601 UTC (`YYYY-MM-DDTHH:MM:SS.fffZ`), matching the `eve_timestamp` convention in [`session-schema.md`](session-schema.md). Sorts lexicographically; human-readable in the SQLite CLI.
- **`isk_buy` and `isk_sell` are REAL** (8-byte IEEE float, equivalent to C# `double`). The user's example value `100300200100.24` (≈10¹¹ ISK with 2 decimals) is well within `double`'s ~15-17 significant-digit precision. Either or both may be NULL when GoonMetrics doesn't return that side of the order book.
- **`isk_buy` semantics:** the highest buy order price (someone willing to pay this much per unit). The "buy" leg of `price_mode = "buy"`.
- **`isk_sell` semantics:** the lowest sell order price (someone offering at this much per unit). The "sell" leg of `price_mode = "sell"`. `price_mode = "split"` averages buy and sell.
- **No `currency` column.** Everything is ISK by definition.
- **No `volume` / `quantity` / `units_available` columns.** The app cares about price per unit, not market depth.

---

## What we deliberately skipped

- **~~No `session_id` FK~~ (reversed by plan 38).** The original round-7 1.1 = A "global rows, join on fetched_at" model let a later session's prices re-price an older session (BUGS.md Bug 2). Plan 38 binds each price row to its owning `session_id` instead. It is a plain TEXT column, not an enforced FK (deleting a session leaves harmless orphans).
- **No `station_id` read filter (plan 38 amendment).** `station_id` is captured on every row as fetch provenance, but the ISK read keys on `(session_id, type_id)` only. A session is valued by the prices it captured, independent of the globally-selected station — so switching your fetch station never changes any saved session's historical ISK. A future feature can surface the captured station from the stored column.
- **No `currency` column.** ISK only.
- **No market-depth fields** (volume, order count, etc.). v1 doesn't use them; v2 reporting can add them if a future feature needs them.
- **No UPDATE path.** Every fetch is a new INSERT. Old rows are immutable history.
- **No automatic eviction / pruning.** Rows accumulate across the app's lifetime. At v1 fetch rates (~35-70 rows per session start, no auto-refresh) this is bounded by user activity. v2 may add a retention policy.
- **No gas pricing.** Fullerite gases land in v3 (raw market price + user-supplied decompression loss rate).
- **No periodic auto-refresh** in v1 (per round-7 user direction — sessions last 1-6 hours and the prices stay fresh enough). v2 plan adds the scheduler.
- **No retry / backoff on GoonMetrics failure.** If the API is unreachable, the affected type_material gets ISK = 0 with a Warning log; the user can manually trigger a refetch in v2 (or re-Start the session in v1). No retry loops, no exponential backoff.
