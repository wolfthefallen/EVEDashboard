# EVE Dashboard — Market Schema

The shape of the **market price data** persisted by the app at runtime. Companion to [`session-schema.md`](session-schema.md) (sessions / pilots / events / settings) and [`sde-schema.md`](sde-schema.md) (the EVE Static Data Export). Canonical reference for any code that fetches, reads, or writes market price rows.

**Source (selectable):** the `price_source` setting picks which service the fetcher calls (`SelectablePriceClient` re-reads the setting on every fetch), live-switchable via Settings -> Pricing:
- `goonmetrics` (default) — the third-party GoonMetrics API at `https://goonmetrics.apps.gnf.lt/api/price_data/?station_id=<id>&type_id=<csv>` (no auth).
- `esi` — official CCP data via the public ESI endpoint `GET https://esi.evetech.net/v1/markets/{region_id}/orders/?type_id=<id>&order_type=all` (no auth, ~300 req/min). The region is resolved from the selected station's `market_stations.region_id`; orders are filtered to the station's `location_id` and reduced to best buy (max) / best sell (min). NPC hub stations only (player structures need an authenticated scope). A station with no known `region_id` yields no ESI prices (values at 0), same as a GoonMetrics miss.

Both sources write identical `market_data` rows; switching source only changes where future fetches come from (existing rows are immutable history).

**Haul Value is the one path that bypasses both the router and this table.** `HaulValuationCalculator` calls a price client directly and never writes `market_data`, so an ad-hoc appraisal cannot pollute a session's price history. It is wired to the official-ESI client regardless of `price_source`, because GoonMetrics returns 404 for the non-Jita empire hubs and the multi-hub comparison needs all four.

**Why this shape:** ISK valuation needs prices for the type_materials reachable from each mined ore. Prices are persisted in the session SQLite database (NOT a transient cache) so reporting can review the prices that applied at any point in the past. There is **no time-based periodic auto-refresh** — prices fetch at session start (and on Resume, and on a mid-session pricing-station change), plus a lazy on-demand fill for a material with no usable price for the active price mode, throttled to at most once per 15 minutes per session. Gas is not priced yet (gas events are captured but not valued).

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
│ session_id    TEXT    NOT NULL          │            Owning session's sha256 id (or the 64-zero sentinel for unassociated migrated rows). Plain TEXT, NOT an enforced FK.
│ fetched_at    TEXT    NOT NULL          │            ISO-8601 UTC; the wall-clock time the price was fetched
│ isk_buy       REAL                      │            Nullable - either source may return only buy or only sell
│ isk_sell      REAL                      │            Nullable - either source may return only buy or only sell
│ IDX(session_id, type_id, fetched_at DESC)           │  Latest-price-for-type-in-session lookup (station-blind)
└─────────────────────────────────────────┘
```

**`session_id` column** on `market_data`: prices are **bound to the session that fetched them** (rows were originally global and joined on `fetched_at`; that let a later session's fetches re-price an older one). Every row carries the sha256 id of its owning session; the ISK lookup reads only that session's rows. This makes a saved session's ISK permanently reproducible — it can never be re-priced by a later session's fetches. Migrated rows that could not be associated with any session carry the 64-zero `Unassociated` sentinel and feed no session's ISK. `session_id` is a plain TEXT column, NOT an enforced foreign key (so deleting a session leaves harmless orphan price rows).

**`market_stations` is the user-configurable list** of stations the app may fetch prices from. v1 ISK calculation reads ONE station from this catalog (the id selected via `setting.selected_station_id`); the catalog itself can hold any number of rows. Default seeding behaviour and CRUD lifecycle are documented under *How the tables compose* below.

---

## SQL DDL (canonical)

```sql
-- market_stations - part of the canonical SchemaInitializer.SchemaDdl
-- (created on every launch via CREATE TABLE IF NOT EXISTS; no versioned migration entry).
CREATE TABLE market_stations (
    station_id   INTEGER PRIMARY KEY,
    system_name  TEXT    NOT NULL,
    station_name TEXT    NOT NULL,
    region_id    INTEGER,
    region_name  TEXT
);

-- market_data: also part of SchemaDdl. Written by MarketPriceFetcher, read by IskCalculator.
-- The FK on station_id is intra-DB enforced (market_stations is in the same DB).
CREATE TABLE market_data (
    marketdata_id INTEGER PRIMARY KEY AUTOINCREMENT,
    type_id       INTEGER NOT NULL,
    station_id    INTEGER NOT NULL REFERENCES market_stations(station_id),
    session_id    TEXT    NOT NULL,   -- owning session's sha256 id (or the 64-zero Unassociated sentinel)
    fetched_at    TEXT    NOT NULL,
    isk_buy       REAL,
    isk_sell      REAL
);
-- The read is station-blind, so station_id is not in the index.
CREATE INDEX idx_market_data_lookup
    ON market_data(session_id, type_id, fetched_at DESC);
```

Database location: `session.sqlite` (alongside `session`, `session_time`, `pilot`, `session_pilot`, `log`, `setting`, `market_stations`). Both `market_stations` and `market_data` are created by `EveDashboard.Stats.Schema.SchemaInitializer.EnsureSchemaAsync` from the single `SchemaDdl` script. Pre-session-id databases (integer `session_id`) are upgraded in place by `LegacySessionIdMigration`, which rebuilds `market_data` and stamps each row with the session whose time window contains its `fetched_at`, or the `Unassociated` sentinel when no single session can claim it.

---

## How the tables compose

### `market_stations` — lifecycle

| Trigger | What gets written |
|---|---|
| `IMarketStationCatalog.EnsureSeedDefaultsAsync` on a fresh DB (called during app bootstrap in `App.xaml.cs`, right after the settings defaults are seeded) | INSERT the four empire hubs (Jita `60003760`, Amarr `60008494`, Dodixie `60011866`, Rens `60004588`) + INSERT C-J6MT (`1049588174021`) + INSERT sentinel `non_sticky_station_defaults_seeded = '1'` into `setting`. |
| `EnsureSeedDefaultsAsync` on a subsequent launch | `INSERT OR IGNORE` the four empire hubs (sticky defaults — re-seeded if the user deleted one; existing users get Amarr/Dodixie/Rens added on the next launch). C-J6MT skipped because the sentinel is present. |
| User adds a station via Settings UI | `IMarketStationCatalog.AddAsync` -> INSERT one row. |
| User edits a station via Settings UI | `IMarketStationCatalog.UpdateAsync` -> UPDATE the existing row. |
| User deletes a station via Settings UI | `IMarketStationCatalog.RemoveAsync` -> DELETE the row. C-J6MT will NOT be re-seeded by `EnsureSeedDefaultsAsync` because the sentinel persists. Jita WILL be re-seeded on next launch. |

**Default seeding policy:**
- **The four NPC empire trade hubs** are the baseline, re-seeded every launch via `INSERT OR IGNORE` (sticky) so the user always has working hub defaults — station ids / regions verified against ESI:
  - **Jita** (`60003760`) - Caldari, The Forge (`10000002`).
  - **Amarr** (`60008494`) - Amarr, Domain (`10000043`).
  - **Dodixie** (`60011866`) - Gallente, Sinq Laison (`10000032`).
  - **Rens** (`60004588`) - Minmatar, Heimatar (`10000030`).
- **C-J6MT** (`1049588174021`) is a player-owned nullsec station that can be destroyed in-game. Seeded once on first launch; never re-seeded after the sentinel is written. If the user removes it from the catalog, the user's choice persists.

The catalog cross-touches the `setting` table for the `non_sticky_station_defaults_seeded` sentinel; this is the only `setting` key written by `IMarketStationCatalog`.

### `market_data` — lifecycle

| Trigger | What gets written |
|---|---|
| User clicks Start, Resume, or restore-on-launch (session enters Running) | INSERT one row per refined-material `type_id` at the **selected** station (`selected_station_id`), each stamped with the active `session_id`. **Unconditional** — every refined material is (re-)fetched regardless of any already-captured price, so the session owns its prices from t0 and re-prices on Resume. ~35 type_materials per fetch. |
| User changes the selected pricing station mid-session (while Running) | Full **unconditional** re-fetch of all refined materials at the new station, stamped with the active `session_id` — identical to a session start, and **bypasses the 15-minute rule**. The new rows win the latest-row-per-type read, so the session re-prices to the new hub from that point. |
| Mining event lands and the (live) ISK calculator finds a material with no usable price for the active price mode | INSERT a row for that material at the selected station, stamped with the active `session_id`, on-demand — but **at most once per 15 minutes per (session, type)**: a still-unpriced material is retried only on that cadence (tracked by an in-memory attempt map, never a DB sentinel row), never every tick. A material that already has a usable price for the mode is never re-fetched. "Usable" means non-null and strictly positive on the side the mode reads; for `split`, either side being usable counts as covered. |
| Reports / any saved (non-live) session | **NO fetch, ever.** The calculator runs with `allowLazyFetch = false`; a missing price contributes 0. This is what keeps a saved session's ISK permanently stable. |
| The price source is unreachable (GoonMetrics or ESI) | NO row written; ISK calculation gracefully degrades for that item (returns 0, logs Warning). Does NOT crash the app. No retry/backoff. |
| Session ends / user clicks Stop | NO write. Existing rows persist (now permanently owned by that `session_id`). |
| App shutdown | NO write. Existing rows persist. |

There is **no UPDATE** path — every fetch creates a new row. The latest row for a `(session_id, type_id)` pair wins for that session's ISK (station-blind). Older rows within the same session stay around (e.g. a start prefetch plus a later lazy fetch); cross-session, each session only ever sees its own rows.

### Read patterns

```sql
-- Latest price for a type WITHIN one session (the ISK calculator's only
-- price query — live AND reports use this exact shape).
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

## Historical sessions - how a session built after the fact is priced

A session built from the Reports window's *Build from history* / *Combine selected* gets its OWN `market_data` rows (`HistoricalMarketPricer`), never touching another session's rows: (1) every row captured at the currently selected pricing station with a `fetched_at` inside the session's window is grouped by type and each side (`isk_buy`, `isk_sell`) is averaged over the rows where it is non-null; one row per averaged type is inserted for the new session with `fetched_at = window stop` (rows at other stations are ignored - hub prices are not comparable); (2) `MarketPriceFetcher.EnsurePricesAsync` then gap-fills every refined material that still lacks a usable price for the active price mode with a fresh fetch at today's prices, stamped with the new session id. The dialog tells the user which happened ("prices averaged from N window rows" / "valued with TODAY's prices" / the mixed case). Because the calculator reads the newest row per `(session_id, type_id)`, a material the gap-fill had to fetch counts as "today's price" even if an averaged row (lacking the needed side) was also written.

## Filtering and conventions

- **`type_id`** is the EVE inventory type id. The "common" type_materials prefetched at session start are the union of `type_materials.material_type_id` for every `is_ore = 1` type in the SDE (`ISdeLookup.GetRefinedMaterialTypeIds`, recomputed on each SDE load so new moon materials flow in automatically) — ~35 entries in v1 (8 standard minerals + 20 moon materials + 7 ice products).
- **`type_id` is NOT a foreign key** in the SQL sense. The `types` table lives in `sde.sqlite` (a different database file); SQLite does not enforce cross-database foreign keys. The relationship is informational. Application code is responsible for ensuring referenced type_ids exist in the SDE before inserting.
- **`station_id`** on `market_data` is an **intra-DB FK** to `market_stations.station_id` (the FK is intra-DB enforced). Defaults seeded into `market_stations`: the four empire hubs `60003760` (Jita / The Forge `10000002`), `60008494` (Amarr / Domain `10000043`), `60011866` (Dodixie / Sinq Laison `10000032`), `60004588` (Rens / Heimatar `10000030`), plus `1049588174021` (C-J6MT - "1st Taj Mahgoon", region "Insmother" / `10000009`). The `selected_station_id` setting key (default = Jita; also falls back to Jita if the stored id is not in `market_stations`) selects which station the fetcher pulls prices **from**; the ISK read does **not** use it — the read is station-blind (see *Read patterns*). `station_id` is kept on every row as provenance for a future "which hub did these prices come from" display.
- **`fetched_at`** is `TEXT` in ISO-8601 UTC, written as the .NET round-trip (`"O"`) format of a UTC `DateTimeOffset` — `YYYY-MM-DDTHH:MM:SS.fffffff+00:00` (seven fractional digits, `+00:00` offset rather than `Z`). Rows written by older builds may carry a `Z` suffix or fewer fractional digits; the ISK read only ever orders rows, and the legacy migration compares parsed timestamps rather than strings, so both shapes sort and associate correctly. Human-readable in the SQLite CLI.
- **`isk_buy` and `isk_sell` are REAL** (8-byte IEEE float, equivalent to C# `double`). The user's example value `100300200100.24` (≈10¹¹ ISK with 2 decimals) is well within `double`'s ~15-17 significant-digit precision. Either or both may be NULL when GoonMetrics doesn't return that side of the order book.
- **`isk_buy` semantics:** the highest buy order price (someone willing to pay this much per unit). The "buy" leg of `price_mode = "buy"`.
- **`isk_sell` semantics:** the lowest sell order price (someone offering at this much per unit). The "sell" leg of `price_mode = "sell"`. `price_mode = "split"` uses the midpoint when both sides are present, otherwise whichever single side exists (`PriceModeSelector.Pick`, shared by the ISK and Haul Value calculators).
- **No `currency` column.** Everything is ISK by definition.
- **No `volume` / `quantity` / `units_available` columns.** The app cares about price per unit, not market depth.

---

## What we deliberately skipped

- **~~No `session_id` FK~~ (reversed).** The original "global rows, join on fetched_at" model let a later session's prices re-price an older session. Each price row is now bound to its owning `session_id` instead. It is a plain TEXT column, not an enforced FK (deleting a session leaves harmless orphans).
- **No `station_id` read filter.** `station_id` is captured on every row as fetch provenance, but the ISK read keys on `(session_id, type_id)` only. A session is valued by the prices it captured, independent of the globally-selected station — so switching your fetch station never changes any saved session's historical ISK. A future feature can surface the captured station from the stored column.
- **No `currency` column.** ISK only.
- **No market-depth fields** (volume, order count, etc.). Nothing uses them today; they can be added if a future feature needs them.
- **No UPDATE path.** Every fetch is a new INSERT. Old rows are immutable history.
- **No automatic eviction / pruning.** Rows accumulate across the app's lifetime. At current fetch rates (~35-70 rows per session start, no auto-refresh) this is bounded by user activity. A retention policy may be added later.
- **No gas pricing.** Fullerite gas events are captured but not valued; the intended approach is raw market price plus a user-supplied decompression loss rate.
- **No *time-based periodic* auto-refresh.** Prices are captured at session start / Resume / a user-driven station change; the only automatic re-fetch is the gaps-only 15-minute retry for a material still missing a usable price for the active mode. There is no background scheduler polling every material on a timer. The 15-minute throttle is an in-memory attempt map (no new column, no sentinel row — consistent with the "no NULL-only sentinel rows" choice).
- **No tight retry / backoff on GoonMetrics failure.** If the API is unreachable, the affected material gets ISK = 0 with a Warning log. A still-unpriced material is retried only on the gaps-only 15-minute cadence (or on the next Start / Resume / station-change full re-fetch) — never in a tight loop and never with exponential backoff.
