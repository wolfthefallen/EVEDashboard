# EVE Dashboard — Technical & Architecture Reference

A native-Windows, real-time mining-activity dashboard for EVE Online.

The app reads the game's local gamelog files as they are written, surfaces live per-pilot mining stats inside one resizable window, and computes ISK valuations from refined-output prices fetched from the public GoonMetrics API. Built in **C# / .NET 8 with WPF**.

This is the technical and architecture reference for the codebase. For the end-user overview and download, see the [project README](../README.md).

---

## Table of contents

1. [What this is (and what it isn't)](#what-this-is-and-what-it-isnt)
2. [Quick start](#quick-start)
3. [Tech stack](#tech-stack)
4. [Solution layout](#solution-layout)
5. [Runtime architecture](#runtime-architecture)
6. [Process flows](#process-flows)
7. [Data stores](#data-stores)
8. [User-facing surface](#user-facing-surface)
9. [Configuration](#configuration)
10. [Testing](#testing)
11. [EVE Online ToS posture](#eve-online-tos-posture)

---

## What this is (and what it isn't)

**This is:**

- A read-only observer of the EVE client. Every input it consumes is a file the game writes (gamelogs) or a public, un-authenticated HTTP API (GoonMetrics for prices, everef.net for the Static Data Export).
- A live dashboard. Mining ticks land on screen ~1 second after EVE writes them.
- A session-oriented tool. Mining work is grouped into named sessions (start / pause / resume / stop / new); stats roll up per-pilot and per-session.
- An ISK calculator. For each pilot, mined units are reduced to refined materials, multiplied by current Jita (or other configured station) prices, and totalled.

**This is NOT, and will not become:**

- A bot. It never sends keystrokes, mouse clicks, or any other input to the EVE client.
- A memory reader. It never reads or writes the EVE process memory.
- A market trading tool. It never places, cancels, or modifies market orders.
- A killboard / intel feed.
- An anything-that-modifies-the-EVE-client tool.

See [EVE Online ToS posture](#eve-online-tos-posture) below for the hard line.

---

## Quick start

Prerequisites: Windows 10 / 11, .NET 8 SDK, EVE has been launched at least once on this machine (so a Gamelogs folder exists).

```powershell
# Build everything
dotnet build

# Run the unit-test suite
dotnet test

# Launch the WPF app from source
dotnet run --project src/EveDashboard.App.Wpf

# Publish a single self-contained Windows EXE
dotnet publish src/EveDashboard.App.Wpf -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

On first launch the app:

1. Auto-detects your EVE gamelog folder under `Documents\EVE\logs\Gamelogs\`. If the path is not where it expects, a one-shot bootstrap-failure dialog opens; pointing it at the right folder and clicking **Apply** retries the startup chain in place.
2. Materialises a fresh SQLite database under `%APPDATA%\EveDashboard\` and seeds default settings.
3. Extracts an embedded SDE fallback so the dashboard runs offline.
4. Tries (best-effort) to upgrade the SDE from [data.everef.net](https://data.everef.net/reference-data/) — failure is logged and the shipped fallback is used.
5. Discovers your characters by scanning gamelog filenames, opens the main window, and waits for you to click **New Session** (which opens the session-configuration dialog with its **Start** button).

---

## Tech stack

| Concern | Choice |
| --- | --- |
| Language / runtime | C# / .NET 8 |
| UI | WPF (single conventional window — not an overlay, not always-on-top) |
| MVVM | [`CommunityToolkit.Mvvm`](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| Drag-and-drop | [`gong-wpf-dragdrop`](https://github.com/punker76/gong-wpf-dragdrop) |
| Persistence | SQLite via `Microsoft.Data.Sqlite` (two databases — see [Data stores](#data-stores)) |
| YAML config | `YamlDotNet` |
| Logging | `Microsoft.Extensions.Logging` over `Serilog` (file + console) |
| HTTP | BCL `HttpClient` |
| Archive (SDE) | `SharpCompress` for `.tar.xz` |
| JSON | BCL `System.Text.Json` |
| Excel export | `ClosedXML` |
| Notification sounds | `NAudio` (auto-pause / auto-resume WAVs with a volume setting) |
| Testing | xUnit + FluentAssertions |

Solution-wide compiler flags (in `Directory.Build.props`):
`Nullable=enable`, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `LangVersion=latest`. CS1591 (missing XML doc on public API) is therefore a build error.

---

## Solution layout

```
src/
  EveDashboard.Core/           Pure domain types + interfaces. No I/O.
  EveDashboard.Mining/         Gamelog discovery, parsing, real-time tail watcher.
  EveDashboard.MarketData/     GoonMetrics + ESI HTTP clients, price-source router,
                               price fetcher, ISK calculator.
  EveDashboard.Sde/            Static Data Export download / convert / lookup.
  EveDashboard.Persistence/    SQLite wrapper + YAML loader + Serilog setup.
  EveDashboard.Stats/          Session schema + migrations, event writer, stats query
                               layer, settings, market-station catalog, fleets.
  EveDashboard.Reporting/      Saved-session queries (summary, timeline, ore / refined
                               breakdowns, compare, lifetime).
  EveDashboard.SessionIO/      JSON / Excel session export, JSON import, session delete.
  EveDashboard.Updates/        GitHub release-check client for the launch update check.
  EveDashboard.App.Wpf/        WPF UI. Bootstraps and composes everything.
tests/
  EveDashboard.Core.Tests/
  EveDashboard.Mining.Tests/
  EveDashboard.MarketData.Tests/
  EveDashboard.Persistence.Tests/
  EveDashboard.Sde.Tests/
  EveDashboard.Stats.Tests/
  EveDashboard.Reporting.Tests/
  EveDashboard.SessionIO.Tests/
  EveDashboard.Updates.Tests/
  EveDashboard.App.Wpf.Tests/
  _shared/                  CapturingLogger test helper linked into the test projects.
  manual/                   Single canonical manual-tests.md (all scenarios, workflow order).
docs/                       This document + schema references for the SQLite databases.
```

**Dependency direction is one-way:** UI -> (Mining / MarketData / Sde / Persistence / Stats / Reporting / SessionIO / Updates) -> Core. `EveDashboard.Core` is the leaf — it depends on nothing else in the solution and contains no I/O. Within the middle layer, Stats depends on Persistence, Reporting on Stats + Persistence, and SessionIO on Reporting + Stats; Mining, MarketData, Sde, Updates, and Persistence each reference only Core. WPF dispatcher use is confined to `EveDashboard.App.Wpf`; lower layers communicate upward via events, observable collections, or async tasks.

```
                              +-----------------------+
                              |  EveDashboard.App.Wpf    |  (WPF UI, DI composition root)
                              +-----+------+----+-----+
                                    |      |    |
              +---------------------+      |    +--------------+
              v                            v                   v
   +----------------------+     +-------------------+    +-----------------+
   |  EveDashboard.Stats     |     | EveDashboard.Mining    |    | EveDashboard.Sde   |
   | (schema, writer,     |     | (discovery,       |    | (download,      |
   |  queries, settings)  |     |  parser, watcher) |    |  convert, lookup)
   +----------+-----------+     +---------+---------+    +--------+--------+
              |                           |                       |
              |         +-----------------+                       |
              |         |    +------------------------+           |
              |         |    | EveDashboard.MarketData   |           |
              |         |    | (HTTP client, fetcher, |           |
              |         |    |  ISK calculator)       |           |
              |         |    +-----------+------------+           |
              |         |                |                        |
              v         v                v                        v
        +-------------------------------------------------------------+
        |                        EveDashboard.Core                       |
        |  Domain records, interfaces (ISdeLookup, ISettingsRepo,     |
        |  IIskCalculator, ...), session/active-session contracts,    |
        |  AppDataPaths, layout descriptors. NO I/O.                  |
        +-------------------------------------------------------------+

        +-------------------------------------------------------------+
        |                   EveDashboard.Persistence                     |
        |  SqliteDatabase wrapper, Serilog setup, YAML loader.        |
        |  Implementation detail; consumed by Stats, Reporting, App.  |
        +-------------------------------------------------------------+

        (Not drawn: Reporting -> Stats + Persistence; SessionIO -> Reporting
         + Stats; Updates -> Core only. All are consumed by App.Wpf.)
```

---

## Runtime architecture

### App startup chain

`App.OnStartup` runs a deterministic bootstrap chain. Every step is null-aware against an instance field, so the chain is re-runnable in place when the bootstrap-failure dialog asks the user to fix a path and **Apply**.

```
App.OnStartup
   |
   v
[0] One-time data-folder rename                   %APPDATA%\EveMiningDashboard -> %APPDATA%\EveDashboard
       |   (runs before logging so the new folder is not created first;
       |    failure -> MessageBox + shutdown, old data left in place)
[1] Configure Serilog logging                     -> %APPDATA%\EveDashboard\logs\
       |   then install the app-wide unhandled-exception handlers and merge the
       |   Dark theme dictionary so any dialog shown before step 10 has brushes
       v
RunBootstrapChainAsync   (steps 2-10; re-runnable in place on Apply)
[2] Load YAML bootstrap config                    -> %APPDATA%\EveDashboard\config.yaml
       |   (autodetects Documents\EVE\logs\Gamelogs if the file is absent)
       |   on failure: open bootstrap-failure dialog and wait
       v
[3] Open session SQLite                           -> %APPDATA%\EveDashboard\session.sqlite
       |   one-time migration of a pre-sha256 (integer session_id) store —
       |   backed up first, announced, startup aborted on failure — then
       |   ensure schema + run outstanding migrations
[4] Open SDE SQLite + extract embedded fallback   -> %APPDATA%\EveDashboard\sde\eve.sqlite
       |   (fallback extracted only when the file is absent; existing store
       |    is forward-migrated in place)
[5] Try SDE update from everef.net                (best-effort; failure is non-fatal)
[6] Load SDE lookup tables into memory            (ore/gas type resolution)
[7] Build settings repository + market-station catalog; seed defaults
[8] Construct parser + active-session context + writer + realtime watcher (NOT started)
[8.5] Construct GoonMetrics client + ESI client + price-source router,
      price fetcher, ISK calculator
[8.6] Construct GitHub release-check client
[9] Discover characters from gamelog filenames
       |   on zero characters: open bootstrap-failure dialog and wait
       v
[10] Build the DI service provider, load the user theme, restore widget layout,
     show MainWindow, then fire-and-forget the launch update check
```

### MainWindow composition

The window itself is intentionally minimal. The chrome row holds buttons only: the session controls on the left (which ones are visible depends on `Session.Mode`) and the secondary-window / popup buttons on the right. Everything else is widgets in a `WrapPanel` flow (one `ScrollViewer` + one `ItemsControl`).

```
+------------------------------------------------------------------------------------------+
| [New Session] / [Pause] [Stop] / [Continue] [Stop]                                        |
|        [Hidden widgets v] [Haul Value] [Crystal Diagram] [Reports] [Settings] [About]     |   chrome (Row 0)
+------------------------------------------------------------------------------------------+
|                                                                                          |
|  +-------------------+  +-------------------+  +-------------------+                     |
|  | SessionStats      |  | Pilot: Aurelia    |  | Pilot: Khepri     |                     |
|  | (name / time /    |  | (units, m3, ISK,  |  |                   |                     |
|  |  stats / total ISK)|  |  rate, last tick) |  |                   |                     |
|  +-------------------+  +-------------------+  +-------------------+                     |
|                                                                                          |
|  +-------------------+                                                                   |
|  | Pilot: Vela       |    ...wrap as needed...                                           |
|  +-------------------+                                                                   |
|                                                                                          |
+------------------------------------------------------------------------------------------+
                                                                         (Row 1: WrapPanel)
```

Chrome buttons by mode: Config or Stopped -> **New Session** (opens the modal `SessionConfigDialog`, which hosts the fleet / pilot list, the session-name editor, and the **Start** button); Running -> **Pause** + **Stop**; Paused -> **Continue** + **Stop**. The session-configuration UI is a dialog, not a widget tile.

- Each tile is a `WidgetBase`-derived `UserControl` driven by a `ViewModel`.
- Tiles are draggable. Order persists to the `setting` table in session SQLite.
- Each tile carries an `x` button to hide it; hidden tiles are recoverable from the **Hidden widgets** popup.
- Each tile carries a cog button to reorder, hide, or change the visual emphasis of individual stat rows. Per-widget config (which stats appear and how) persists to `setting` too.
- The window is a single conventional Windows window. It is **not** always-on-top, **not** transparent, and **not** chromeless. It is sized and positioned with normal OS window management (FancyZones works well for this).

### Session lifecycle state machine

Session controls live in the chrome row and are owned by `SessionController`, which owns the four-mode `SessionWidgetMode` state machine (Config / Running / Paused / Stopped). `Mode` drives chrome-button visibility; events drive state transitions. Each lifecycle transition (Start / Pause / Resume / Stop) is one SQL transaction.

```
                                   +----------------------------+
                                   |          Config            |
                                   | (no active session row;    |
                                   |  "New Session" opens the   |
                                   |  config dialog: fleet      |
                                   |  roster + roles + Start)   |
                                   +-------------+--------------+
                                                 | Start (lands in Paused:
                                                 |  "Start-as-paused")
                                                 v
   +------------------+  Pause / auto-pause  +-----------------------------+
   |     Paused       | <------------------- |           Running           |
   | (writer drops    |                      | (FSW tails gamelogs;        |
   |  events; 1 Hz    |  Resume / auto-resume|  events INSERT to SQL;      |
   |  timer stopped)  | -------------------> |  1 Hz stats refresh)        |
   +-------+----------+                      +--------------+--------------+
           |                                                |
           | Stop                                           | Stop
           |                                                |
           +----------------------+-------------------------+
                                  v
                      +---------------------------+
                      |         Stopped           |
                      | (final stats frozen on    |
                      |  screen; "New Session"    |
                      |  button visible)          |
                      +-------------+-------------+
                                    | New Session
                                    v
                                (back to Config)
```

- **Auto-pause:** while Running, the 1 Hz tick checks the wall-clock time of the last Tick/Crit; once `auto_pause_idle_minutes` have elapsed with none, the controller runs the same Running -> Paused transition as the Pause button and raises a sound request (the auto-pause WAV).
- **Auto-resume:** while Paused, the writer's drop path invokes a controller callback when a Tick/Crit arrives; the controller marshals to the dispatcher, runs Paused -> Running, and raises the auto-resume sound request.
- Pause and Resume leave the watcher and writer running; Pause only flips the `IActiveSessionContext` state, which the writer reads on every write to decide whether to drop.
- App-launch with an open `session_time` row in the database lands directly in **Paused** (same mode value, same `[Continue] [Stop]` chrome) so the user explicitly decides whether to resume the prior session or stop it. There is no automatic resume at launch.

---

## Process flows

### Live mining tick: from EVE log line to widget

This is the hot path. Every tick goes through it.

```
EVE client appends a line to <gamelogs>\YYYYMMDD_HHMMSS_<charId>.txt
        |
        v
FileSystemWatcher fires (Changed/Created)
        |
        v  RealtimeLogWatcher (EveDashboard.Mining)
        |  - Tracks per-file read offset in memory
        |  - Reads new bytes; splits on newline, trims the trailing \r,
        |    decodes each line as UTF-8
        |
        v
GamelogLineParser (EveDashboard.Mining)
        |  - Cheap "(mining)" pre-filter
        |  - Parses the leading [YYYY.MM.DD HH:MM:SS] timestamp
        |  - Tries Crit -> Tick -> Residue regex shapes
        |  - Resolves product name -> type_id via ISdeLookup
        |  - Emits MiningEvent (Kind, EveTimestamp, CharacterId, ProductName,
        |    TypeId, Amount)
        |
        v
SessionEventWriter (EveDashboard.Stats)
        |  - Drop rules:
        |       no active session / not started -> drop
        |       state == Paused, Tick/Crit      -> invoke the auto-resume callback
        |                                          (single-flight); enqueue only if
        |                                          the state is Running afterwards
        |       state == Paused, anything else  -> drop
        |       Tick/Crit with TypeId=null      -> drop (parser already warned)
        |       Residue with TypeId=null        -> INSERT row with NULL type_id/volume_m3
        |  - Bounded Channel<MiningEvent> (capacity 4096, wait-when-full) handed
        |    to a single background consumer
        |  - Consumer INSERTs into the `log` table; computes volume_m3 via
        |    ISdeLookup.portion_size at write time
        |
        v
SQLite update hook (Microsoft.Data.Sqlite)
        |  - Fires on every successful INSERT
        |  - Maps log_id -> SessionEventAdded event
        |
        v
SessionController.OnSessionEventAdded
        |  - Sets _statsDirty = true
        |
        v
1 Hz DispatcherTimer (UI thread)
        |  - If _statsDirty, queries SqliteSessionStatsRepository
        |  - Snapshot built from 5 SQL queries (existence, active duration,
        |    session aggregates, per-pilot aggregates, per-(pilot,type_id) units)
        |  - Per-pilot ISK is computed by IIskCalculator (see ISK flow below)
        |  - Snapshot pushed into bound ObservableProperty fields on
        |    SessionStatsViewModel + each PerPilotWidgetViewModel
        |
        v
WPF binding refresh re-renders the affected widget tiles
```

The parser and writer never share state directly — they communicate through the bounded channel and the SQLite update hook. SQLite is the live substrate, not just persistence: every parsed event is round-tripped through the database before any widget sees it.

### ISK valuation flow

Per pilot, per stats refresh, the calculator walks each mined `type_id` through the SDE's `type_materials` table at 100% efficiency, applies the user's configured refining yield, multiplies by the current price, and sums.

```
SqliteSessionStatsRepository.GetSessionStatsAsync
        |  (per-(pilot, type_id) units totals AND crit-only units totals
        |   from `log` in a single SQL query)
        v
IIskCalculator.ComputeAsync(typeIdToUnits, critTypeIdToUnits,
                            stationId, sessionId, allowLazyFetch,
                            yieldPct, priceMode)
        |
        |  Step 1 - Recursive reprocessing breakdown at 100% efficiency
        |     For each ore type_id, look up SDE type_materials and
        |     accumulate base materials (Tritanium, Pyerite, ...).
        |     A parallel crit-only accumulator runs in lockstep when the
        |     caller supplied a non-empty critTypeIdToUnits.
        |     Gas has no type_materials -> contributes nothing in v1.
        |
        |  Step 2 - Apply user's refining yield (50.0 .. 90.6 %)
        |     adjustedQty = floor(rawQty * yieldPct/100)
        |
        |  Step 3 - Lazy fetch GAPS only in ONE round-trip
        |     (allowLazyFetch is true only for the LIVE session; Reports /
        |      saved sessions read what the session already captured)
        |     IMarketPriceFetcher.EnsurePricesAsync(materialTypeIds, stationId,
        |                                           sessionId, priceMode)
        |       |
        |       v
        |     MarketPriceFetcher
        |       - Skip any material whose latest captured price is usable
        |         for the active mode
        |       - Fetch a gap at most once per 15 min per (session, type)
        |         via an in-memory attempt map (no sentinel rows)
        |       - On a gap: HTTP GET through the price-source router
        |         (GoonMetrics or ESI per the `price_source` setting)
        |       - INSERT-only into `market_data`, tagged with the session_id
        |       - On transport/parse failure: log Warning, return what we have
        |     (The fetcher also prefetches every refined material
        |      unconditionally on session start / resume / station change.)
        |
        |  Step 4 - For each material:
        |     price = SELECT isk_buy, isk_sell FROM market_data
        |             WHERE session_id = ? AND type_id = ?
        |             ORDER BY fetched_at DESC LIMIT 1
        |     (session-bound and station-blind: station_id is fetch provenance,
        |      never a read filter, so a saved session's ISK does not change
        |      when the selected station changes)
        |     totalIsk += adjustedQty * price
        |     critIsk  += adjustedCritQty * price   (when crit subset is present)
        |     Missing price -> 0 contribution + Warning log
        |
        v
IskCalculationResult { TotalIsk, CritIsk, IskByInputType }
```

`IIskCalculator` exposes two overloads: the single-dict
`ComputeAsync(typeIdToUnits, ...)` and the two-dict
`ComputeAsync(typeIdToUnits, critTypeIdToUnits, ...)`. The repository
calls the two-dict overload so it derives Total ISK + Crit ISK
in one pass per pilot (shared price cache, exact math, single round-trip
through `EnsurePricesAsync`). `IskByInputType` reflects the FULL units and
drives the Reports per-ore breakdown widget; `CritIsk` powers the
`Crit ISK %` stat on the live + saved-single + compare SessionStats widgets.

Configurable from the Settings dialog: refining yield percentage (50.0 - 90.6), price source (GoonMetrics / ESI), price mode (buy / sell / split), and the station to price against. The four NPC empire hubs (Jita 60003760 by default, Amarr, Dodixie, Rens) are re-seeded on every launch; C-J6MT 1049588174021 is seeded once and stays removed if the user deletes it. Custom stations can be added.

### SDE refresh flow (once per launch)

```
EverefSdeUpdater.CheckAndUpdateAsync (at startup)
        |
        v
(sweep any orphan tmp folders left by a prior crash)
        |
        v
HEAD https://data.everef.net/reference-data/reference-data-latest.tar.xz
        |
        | If Last-Modified AND ETag both match local meta -> NoUpdateNeeded
        |
        v
Conditional GET (If-None-Match) of the .tar.xz
        |
        | 304 Not Modified -> NoUpdateNeeded
        |
        v
Save to <sde dir>\tmp\<guid>\everef.tar.xz; decompress xz + extract tar
(SharpCompress) into <sde dir>\tmp\<guid>\extracted
        |
        v
BEGIN TRANSACTION on the live eve.sqlite
        |
        v
EverefSdeFormatConverter.ImportToOpenTransactionAsync
        |  - Reads the extracted JSON and rewrites meta, categories, groups,
        |    types, type_materials inside the open transaction
        |
        v
Validation inside the same transaction
        |  - SELECT COUNT(*) FROM types must be >= 10,000
        |  - meta.last_modified / meta.etag updated from the GET response
        |  - meta.build_time must be non-empty
        |  - any failure -> ROLLBACK (the previous snapshot is untouched)
        |
        v
COMMIT, then delete the tmp folder (best-effort)
        |
        v
Raise SdeUpdater.Updated (each subscriber invoked in isolation)
        |
        v
SqliteSdeLookup re-reads its in-memory caches
```

Failure modes (network down, malformed payload, validation fail) are non-fatal: the result is reported as `Unreachable`, a Warning is logged, the transaction is rolled back, and the existing SDE keeps serving lookups.

---

## Data stores

Two SQLite files under `%APPDATA%\EveDashboard\`:

### `session.sqlite` — sessions, events, settings, prices

```
+---------------------------------------------+
| session                                     |
|---------------------------------------------|
| session_id     TEXT PK (sha256 content hash |<------+
|                of name + first start_time)  |       |
| name           TEXT NOT NULL                |       |
| notes          TEXT                         |       |
| archived       INTEGER NOT NULL DEFAULT 0   |       |
| merged_into    TEXT (combined-into id; no FK)|       |
+---------------------------------------------+       |
                                                       | FK
+---------------------------------------------+       |
| session_time                                |       |
|---------------------------------------------|       |
| session_time_id INTEGER PK AUTOINCREMENT    |       |
| session_id      TEXT NOT NULL  -------------+-------+
| start_time      TEXT (ISO-8601 UTC)         |       |
| stop_time       TEXT (NULL = open period)   |       |
| is_pause        INTEGER 0|1                 |       |
+---------------------------------------------+       |
                                                       |
+---------------------------------------------+       |
| pilot                                       |       |
|---------------------------------------------|       |
| pilot_id INTEGER PK   (= EVE character id)  |<--+   |
| name     TEXT NOT NULL                      |   |   |
+---------------------------------------------+   |   |
                                                   |   |
+---------------------------------------------+   |   |
| session_pilot                               |   |   |
|---------------------------------------------|   |   |
| session_id TEXT    NOT NULL ----------------+---+---+
| pilot_id   INTEGER NOT NULL ----------------+---+
| role       TEXT NOT NULL DEFAULT 'Miner'    |
|            CHECK IN ('Miner','Booster',     |
|                      'Cyno','Combat')       |
| PRIMARY KEY (session_id, pilot_id)          |
+---------------------------------------------+
                                                       |
+---------------------------------------------+       |
| log                                         |       |
|---------------------------------------------|       |
| log_id        INTEGER PK AUTOINCREMENT      |       |
| session_id    TEXT NOT NULL  ---------------+-------+
| pilot_id      INTEGER NOT NULL              |
| eve_timestamp TEXT (ISO-8601 UTC)           |
| event_kind    TEXT  CHECK IN                |
|              ('Tick','Crit','Residue',      |
|               'Depleted')                   |
| type_id       INTEGER (NULL on Residue)     |
| units         INTEGER                       |
| volume_m3     REAL    (NULL on Residue)     |
| role          TEXT NOT NULL DEFAULT 'Miner' |
|               (same CHECK as session_pilot) |
+---------------------------------------------+

+---------------------------------------------+   +-------------------------------+
| setting                                     |   | market_stations               |
|---------------------------------------------|   |-------------------------------|
| key   TEXT PK                               |   | station_id   INTEGER PK       |<-+
| value TEXT NOT NULL                         |   | system_name  TEXT NOT NULL    |  |
+---------------------------------------------+   | station_name TEXT NOT NULL    |  |
   keys: see "Setting keys" under                 | region_id    INTEGER          |  |
         Configuration below                      | region_name  TEXT             |  |
                                                  +-------------------------------+  |
                                                                                     |
+---------------------------------------------+                                      |
| market_data                                 |                                      |
|---------------------------------------------|                                      |
| marketdata_id INTEGER PK AUTOINCREMENT      |                                      |
| type_id       INTEGER NOT NULL              |                                      |
| station_id    INTEGER NOT NULL  ------------+--------------------------------------+
| session_id    TEXT NOT NULL (no FK; the     |
|               session that captured it)     |
| fetched_at    TEXT (ISO-8601 UTC)           |
| isk_buy       REAL  (NULL allowed)          |
| isk_sell      REAL  (NULL allowed)          |
+---------------------------------------------+
   IDX: (session_id, type_id, fetched_at DESC)  -- station_id is provenance only,
                                                   never a read filter

+---------------------------------------------+   +-------------------------------+
| fleet                                       |   | fleet_pilot                   |
|---------------------------------------------|   |-------------------------------|
| fleet_id   TEXT PK (sha256 of trimmed name  |<--| fleet_id   TEXT NOT NULL      |
|            + sorted "pilotId:role" list)    |   | pilot_id   INTEGER NOT NULL   |--> pilot
| name       TEXT NOT NULL (UNIQUE index)     |   | role       TEXT (same CHECK)  |
| created_at TEXT NOT NULL                    |   | created_at TEXT NOT NULL      |
+---------------------------------------------+   | PRIMARY KEY (fleet_id,        |
                                                  |              pilot_id)        |
                                                  +-------------------------------+
```

The schema is versioned through the `schema_version` setting (currently `3`). A fresh store is created at the latest shape and stamped; an existing store runs only the migrations newer than its recorded version at startup (v1 -> v2 added `fleet` / `fleet_pilot`; v2 -> v3 added `session.archived` + `session.merged_into` with a pre-checked `ALTER TABLE ... ADD COLUMN`). Archived sessions (`archived = 1`) are omitted from every session listing and from Lifetime Totals; they are the sources of a combined session, kept rather than deleted so no mining data is lost. Separately, a store from before content-addressed session ids (integer `session_id`) is rewritten in place once, after a backup, before the schema initialiser runs.

Full reference: [session-schema.md](session-schema.md), [market-schema.md](market-schema.md).

### `sde\eve.sqlite` — EVE Static Data Export

The subset of the everef.net snapshot the dashboard actually reads, in five tables: `meta` (build time + the conditional-GET `last_modified` / `etag` tags), `categories`, `groups`, `types` (name, group, `volume`, `portion_size` for `volume_m3`, the `is_ore` flag, `icon_id`), and `type_materials` (recursive reprocessing). Everything else is intentionally skipped to keep the file small. An embedded fallback (`src/EveDashboard.Sde/Data/sde-fallback.sqlite`) is extracted only when `eve.sqlite` is absent, so the app boots without network and an upgrading user keeps their existing store (which is forward-migrated in place at startup).

Full reference: [sde-schema.md](sde-schema.md).

---

## User-facing surface

### Widgets

| Widget | Source | Stats / controls |
| --- | --- | --- |
| `SessionStats` | `Widgets/Session/SessionStatsView.xaml` | Stat rows: Session (name), Total time, m³/sec, ISK/h, Total ISK, Total m³, Crit m³, Crit count, Crit chance, Crit ISK %; plus a refined-output section. |
| `PerPilotWidget` | `Widgets/Pilot/PerPilotWidgetView.xaml` | Stat rows: Pilot, Last event, m³/sec, ISK/h, Total ISK, Total m³, Crit m³, Crit count, Crit chance; plus an optional refined-output section (the "Show refined output on pilot widgets" setting). One per active pilot. |

Session configuration is not a widget: the **New Session** chrome button opens the modal `Dialogs/SessionConfigDialog.xaml`, which holds the session-name template (`{{$DATE}}`, `{{$TIME}}`, `{{$DATETIME}}`, `{{$CHARS}}`, plus the window tokens `{{$STARTDATE}}` / `{{$ENDDATE}}` / `{{$YYYY}}` that render as `16Aug - 19Aug 2026`; in a live session start and end both resolve to the start time), the saved-fleet list (create / delete / make default, with per-pilot roles), and the **Start** button.

Every widget uses the shared `WidgetBase` chrome (close button + cog). The cog opens a popup that lets the user reorder stat rows by drag-and-drop, hide individual rows, and change emphasis (None / Header0 / Header1 / Header2 / Hero) per row.

### Settings dialog

Opens from the **Settings** chrome button. Fields, grouped as in the dialog:

- **Display** — Theme (Light / Dark)
- **Pricing** — Refining yield % (50.0 - 90.6, clamped); Price mode (Buy / Sell / Split); Price source (GoonMetrics / ESI)
- **Markets** — Selected station (drop-down from `market_stations`); Stations list with **Add station** / **Remove station** (deferred until **Apply**)
- **Auto-pause** — Idle minutes before auto-pause (1 - 60); "Play sounds on auto-pause / auto-resume"; Volume (0 - 100)
- **Refined output** — "Auto-show refined materials as they are mined"; "Show refined output on pilot widgets"
- **Logs** — Gamelog folder (with **Browse...**)

**Apply** persists everything atomically; the theme reapplies live. **Close** dismisses the dialog.

### Bootstrap-failure dialog

If the gamelog path is missing or the YAML config is malformed, the same Settings dialog opens in **bootstrap-failure mode** before the main window is ever created: only the gamelog-folder field is editable, and Apply re-runs the startup chain in place. Cancel exits the app cleanly.

### Reports window — Import / Export sessions

The Reports window provides round-trippable JSON export + import and human-readable Excel export of saved sessions:

- **Right-click a session in the sidebar** -> **Export to JSON...** writes a single-session `.json` file mirroring the SQLite tables, with a `_manifest` block (format / schema_version / app_version / exported_at / session_id / session_name) and arrays for `session`, `session_time`, `pilot`, `session_pilot`, `log`, `market_stations`, `market_data`. Settings are deliberately not exported.
- **Right-click a session in the sidebar** -> **Export to Excel...** writes a single-session `.xlsx` workbook with a Summary tab first (session totals + per-pilot + per-ore + Total-Refined-Output blocks), then one tab per database table, then one tab per pilot (their ore breakdown + a refined-output table). The refined-output blocks carry quantity + ISK per material, reused from `SessionStats.RefinedOutput` (no separate export aggregation). Excel export is one-way; there is no Excel import path.
- **Import Session...** button at the top of the Reports window reads a `.json` file produced by Export to JSON. The import is atomic; the session keeps its content-addressed `session_id` (a file from an older integer-id export gets one recomputed from name + first `start_time`), and a session already present in the store is skipped as a no-op; auto-incremented ids (`session_time_id`, `log_id`, `marketdata_id`) are regenerated; existing pilots / market_stations are preserved on collision (Warning logged if names differ); `_manifest.schema_version` is the export-*format* version (decoupled from the database `schema_version`); a file written by a newer export format than this app understands fails the import with a clear message, while an equal-or-older format is accepted.
- **Build from history...** button (Reports chrome) and **Combine selected...** (sidebar row context menu, needs two or more ticked rows) open the modal `HistoricalSessionDialog`. Date-range mode: the user enters a start/stop (EVE time), picks a fleet, names the session (default `{{$STARTDATE}} - {{$ENDDATE}} {{$YYYY}} history`, e.g. `16Aug - 19Aug 2026 history`), and clicks *Check overlaps* - the dialog's single action button, which turns into *Build* once the current inputs have been checked (editing an input turns it back); combine mode pre-fills the window from the ticked sessions' spans and pre-ticks them (no gamelog parsing). The pipeline is: `ISessionSpanQuery` (non-archived sessions whose span intersects the window, pilots + roles) -> `OverlapAnalyzer.Analyze` (a session is relevant only if it shares a pilot with the roster; *full* = all its pilots are in the roster, *partial* = it adds pilots) -> the user ticks which overlaps to combine; a ticked partial match prompts to create a union fleet (`OverlapAnalyzer.ProposeUnionRoster` + `IFleetRepository.CreateFleetAsync`; declining leaves those sessions out) -> `OverlapAnalyzer.ComputeExclusions` turns every unticked overlap into per-pilot exclusion windows (a pilot belongs to one session at a time, so nothing is counted twice) -> `IHistoricalGamelogReader.ReadAsync` (date-range mode only; scans the Gamelogs folder AND its `old\` subfolder, de-duplicated by filename, through the live `GamelogLineParser`, keeping events inside the window) -> `IHistoricalSessionBuilder.BuildAsync` (one `BEGIN IMMEDIATE` transaction: new `session` / `session_pilot` rows with the roster's roles, the merged sources' `log` rows copied verbatim, parsed events written with the live writer's rules minus anything inside an exclusion or a merged source's span, a synthesised `session_time` timeline, then `archived = 1` + `merged_into` on the sources) -> `IHistoricalMarketPricer.PriceAsync` (the session's own `market_data` rows - see market-schema.md). The dialog refuses to open while a session is Running or Paused. The `session_time` timeline mirrors what a live session would have recorded: merged sources' rows govern their own spans (active intervals unioned so two concurrent sources cannot double count), and every uncovered gap is paused until its first Tick/Crit, active from there, paused again after a gap longer than `auto_pause_idle_minutes` (starting idle-minutes after the last event, exactly when the live auto-pause would fire), and after the last event + idle. Deleting a combined session un-archives its sources (the undo for a combine). The sidebar's **Show archived** checkbox lists archived sessions tagged "(archived)" for inspection / export / delete; Lifetime Totals never include them.
- **Right-click a session in the sidebar** -> **Delete...** removes the session and its FK-cascading rows (`session_time`, `session_pilot`, `log`) atomically after a confirm dialog. Pilots, `market_data`, `market_stations`, and settings are left alone: `pilot` is reachable only through the `session_pilot` join, `market_data.session_id` is a plain TEXT column rather than an enforced FK (orphan price rows are harmless), and `market_stations` / `setting` are user-global.
- A status row at the bottom of the Reports window surfaces `Imported: ...` / `Exported: ...` / `Deleted: ...` / `Import failed: ...` / `Export failed: ...` / `Delete failed: ...` after each action.
- The sidebar also carries a checkbox column: ticking more than one session switches the content area from single-session view to **Compare** mode. A **Lifetime Totals** button next to **Import Session...** opens the lifetime-totals window.

---

## Configuration

### `%APPDATA%\EveDashboard\config.yaml`

Created on first launch (if a `Documents\EVE\logs\Gamelogs` folder exists at autodetect time it is filled in for you):

```yaml
gamelog_path: C:\Users\<you>\Documents\EVE\logs\Gamelogs
```

Environment variables in `gamelog_path` (e.g. `%USERPROFILE%`) are expanded.

### Setting keys (in the SQLite `setting` table)

Keys with a listed default are seeded on every launch (`INSERT OR IGNORE`, so a user's value is never overwritten); the rest are absent until first written and the reader treats the missing row as "use the built-in default".

| Key | Type | Notes |
| --- | --- | --- |
| `schema_version` | int as text | Currently `3` (v2 added the `fleet` / `fleet_pilot` tables; v3 added `session.archived` / `session.merged_into`). Stamped by the schema initialiser, which also runs any outstanding migrations on an older store. |
| `theme` | string | `light` / `dark`. Default `dark`. |
| `refining_yield_percent` | float as text | Default `90.6`. Clamped to [50.0, 90.6]. |
| `price_mode` | string | `buy` / `sell` / `split`. Default `buy`. |
| `price_source` | string | `goonmetrics` / `esi`. Default `goonmetrics`. Which market service the fetcher calls. |
| `selected_station_id` | int as text | Default `60003760` (Jita IV - Moon 4 - Caldari Navy Assembly Plant). A stored id that is no longer in `market_stations` also falls back to Jita. |
| `auto_pause_idle_minutes` | int as text | Default `5`. Clamped to [1, 60]. Minutes without a mining event before the session auto-pauses. |
| `auto_pause_sound_enabled` | `true` / `false` | Default `true`. |
| `auto_pause_sound_volume` | int as text | Default `65` (percent). Clamped to [0, 100]. |
| `auto_show_refined_materials` | `true` / `false` | Default `true`. |
| `show_pilot_refined_output` | `true` / `false` | Default `true`. |
| `refined_materials_visibility` / `refined_materials_visibility.<scope>` | JSON | Per-widget material visibility overrides (`type_id` -> bool). Not seeded. |
| `pilot_sort_order` | string | `LastActiveNewest` (default) / `LastActiveOldest` / `NameAsc` / `NameDesc`. |
| `default_fleet_id` | string | Fleet id (sha256 content hash) to preselect at Start. Absent when none. |
| `crystal_diagram_selected_anomaly` | string | Last anomaly picked in the Crystal Diagram window. |
| `window_geometry` | JSON | Main window pos/size. Not seeded (OS default placement). |
| `reports_window_geometry`, `crystal_diagram_window_geometry`, `lifetime_totals_window_geometry`, `about_window_geometry` | JSON | Same, per secondary window. |
| `reports_sidebar_width` | float as text | Reports window sidebar width in pixels. |
| `widget_layout` | JSON | Order + hidden flag for each widget. Not seeded (default layout). |
| `session_stats_config` | JSON | Stat-row order / visibility / emphasis for the SessionStats widget. |
| `per_pilot_stats_config.<pilot_id>` | JSON | Same, per pilot. |
| `reports_column_widths.<grid_key>` | JSON | Reports DataGrid column widths per grid type (column index -> pixel width); only user-resized columns are stored. |
| `reports_text_size.<widget_type>` | JSON | Reports table-widget text emphasis per widget type (element key -> `StatEmphasis`); only elements that deviate from their default are stored. |
| `non_sticky_station_defaults_seeded` | `1` | Sentinel written by the market-station catalog once the one-time C-J6MT seed has run, so a user who removes that station does not get it back. |

### Logs

`%APPDATA%\EveDashboard\logs\EveDashboard_NNN.log` (the highest NNN is the current file; Serilog size-rolling of the configured `EveDashboard.log` name) — file sink that rolls on size (10 MB per file, current + 1 backup retained) plus a console sink (visible when run from a terminal). Both sinks share one minimum level, Information by default.

---

## Testing

### Automated

```powershell
dotnet test
```

Unit tests live under `tests/EveDashboard.<Project>.Tests/`. Conventions:

- xUnit + FluentAssertions.
- Parser tests drive real anonymized EVE log lines from `tests/EveDashboard.Mining.Tests/testdata/Gamelogs/` — log shapes are never invented.
- `WidgetStats`, `LayoutCoordinator`, `IskCalculator`, etc. each have dedicated test fixtures.
- `EveDashboard.App.Wpf.Tests` exercises ViewModels and DI composition; XAML rendering itself is covered by manual tests.

### Manual

For features that exercise the actual EVE client, real timing, or live UI behaviour (session control flow, drag-to-reorder, the bootstrap-failure dialog re-route, the live mining tick path, etc.) there is a single canonical manual-test document at [../tests/manual/manual-tests.md](../tests/manual/manual-tests.md), organised in workflow order. Each scenario has Setup / Steps / Expected results / Cleanup sections.

---

## Release pipeline

Releases are cut by GitHub Actions (`.github/workflows/ci.yml`), not by hand:

- **Every push to `master` and every pull request against `master`** runs `dotnet build` + `dotnet test` on a `windows-latest` runner (WPF needs Windows).
- **On a push to `master`** the `release` job reads `<Version>` from `Directory.Build.props`. If the tag `v<Version>` does not exist yet, it runs the canonical single-file publish (`dotnet publish src/EveDashboard.App.Wpf -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`), zips a copy, and creates the GitHub release `v<Version>` (tag + title, marked latest, target = the pushed commit) with two assets: `EVEDashboard.exe` and `EVEDashboard.zip` — the same shape every earlier release had, and what the launch update check (`GitHubReleaseClient`) reads from `releases/latest`. If the tag already exists the job only builds and tests (so docs-only commits never re-release).
- **Release notes** are the `## v<Version>` section of `CHANGELOG.md`; when it is missing, GitHub's auto-generated notes are used and the job summary says so.

To ship: bump `<Version>`, write its `CHANGELOG.md` section, merge to `master`. The in-app About window and the update check both read the same `<Version>`, so the three always agree.

## EVE Online ToS posture

EVE Online's ToS permits third-party tools that read files the game writes and that call the public ESI / public market APIs. It forbids automation that interacts with the client and any reading or modifying of the game's process.

| Allowed (this app does this) | Forbidden (this app never does this) |
| --- | --- |
| Read files under `Documents\EVE\logs\Gamelogs\` | Read or write the EVE process memory |
| Call the public GoonMetrics market API | Modify the EVE client or its files |
| Call the everef.net SDE archive | Send keystrokes / clicks to the EVE window |
| Static Data Export lookups | Network-protocol reverse engineering |
| Persist its own session state to its own SQLite files | Anything that "plays the game" for the user |

If you are extending the app, the lowest-level rule is simple: if a feature would require any of the right-hand column above, stop.
