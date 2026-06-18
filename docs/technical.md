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
5. Discovers your characters by scanning gamelog filenames, opens the main window, and waits for you to click **Start**.

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
| Testing | xUnit + FluentAssertions |

Solution-wide compiler flags (in `Directory.Build.props`):
`Nullable=enable`, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `LangVersion=latest`. CS1591 (missing XML doc on public API) is therefore a build error.

---

## Solution layout

```
src/
  EveDashboard.Core/           Pure domain types + interfaces. No I/O.
  EveDashboard.Mining/           Gamelog discovery, parsing, real-time tail watcher.
  EveDashboard.MarketData/     GoonMetrics HTTP client + ISK calculator.
  EveDashboard.Sde/            Static Data Export download / convert / lookup.
  EveDashboard.Persistence/    SQLite wrapper + YAML loader + Serilog setup.
  EveDashboard.Stats/          Session schema, event writer, stats query layer, settings.
  EveDashboard.Reporting/      Saved-session queries.
  EveDashboard.SessionIO/      JSON / Excel session export and JSON import.
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
  manual/                   Single canonical manual-tests.md (all scenarios, workflow order).
docs/                       Schema reference for the SQLite databases.
```

**Dependency direction is one-way:** UI -> (Stats / MarketData / Sde / Updates / Logs / Persistence) -> Core. `EveDashboard.Core` is the leaf — it depends on nothing else in the solution and contains no I/O. WPF dispatcher use is confined to `EveDashboard.App.Wpf`; lower layers communicate upward via events, observable collections, or async tasks.

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
        |  Implementation detail; consumed by Stats and the App.      |
        +-------------------------------------------------------------+
```

---

## Runtime architecture

### App startup chain

`App.OnStartup` runs a deterministic bootstrap chain. Every step is null-aware against an instance field, so the chain is re-runnable in place when the bootstrap-failure dialog asks the user to fix a path and **Apply**.

```
App.OnStartup
   |
   v
[1] Configure Serilog logging                     -> %APPDATA%\EveDashboard\logs\
[2] Load YAML bootstrap config                    -> %APPDATA%\EveDashboard\config.yaml
       |   (autodetects Documents\EVE\logs\Gamelogs if the file is absent)
       |   on failure: open bootstrap-failure dialog and wait
       v
[3] Open session SQLite + ensure schema           -> %APPDATA%\EveDashboard\session.sqlite
[4] Open SDE SQLite + extract embedded fallback   -> %APPDATA%\EveDashboard\sde\eve.sqlite
[5] Try SDE update from everef.net                (best-effort; failure is non-fatal)
[6] Load SDE lookup tables into memory            (ore/gas type resolution)
[7] Build settings repository + market-station catalog; seed defaults
[8] Construct realtime watcher + writer + active-session context (NOT started)
[8.5] Construct GoonMetrics client + price fetcher + ISK calculator
[9] Discover characters from gamelog filenames
       |   on zero characters: open bootstrap-failure dialog and wait
       v
[10] Build the DI service provider, load the user theme, restore widget layout,
     show MainWindow
```

### MainWindow composition

The window itself is intentionally minimal. The chrome row holds the session control buttons (Start / Pause / Resume / Stop / New Session) and a Settings button; everything else is widgets in a `WrapPanel` flow.

```
+---------------------------------------------------------------------------+
| [Start] [Pause] [Resume] [Stop] [New Session]   [Hidden widgets v] [Settings] |   chrome (Row 0)
+---------------------------------------------------------------------------+
|                                                                           |
|  +-------------------+  +-------------------+  +-------------------+      |
|  | SessionConfig     |  | SessionStats      |  | Pilot: Aurelia    |      |
|  | (pilots checklist)|  | (name / time /    |  | (units, m3, ISK,  |      |
|  | session-name      |  |  stats / total ISK)|  |  rate, last tick) |      |
|  | template          |  +-------------------+  +-------------------+      |
|  +-------------------+                                                    |
|                                                                           |
|  +-------------------+  +-------------------+                             |
|  | Pilot: Khepri     |  | Pilot: Vela       |    ...wrap as needed...     |
|  +-------------------+  +-------------------+                             |
|                                                                           |
+---------------------------------------------------------------------------+
                                                              (Row 1: WrapPanel)
```

- Each tile is a `WidgetBase`-derived `UserControl` driven by a `ViewModel`.
- Tiles are draggable. Order persists to the `setting` table in session SQLite.
- Each tile carries an `x` button to hide it; hidden tiles are recoverable from the **Hidden widgets** popup.
- Each tile carries a cog button to reorder, hide, or change the visual emphasis of individual stat rows. Per-widget config (which stats appear and how) persists to `setting` too.
- The window is a single conventional Windows window. It is **not** always-on-top, **not** transparent, and **not** chromeless. It is sized and positioned with normal OS window management (FancyZones works well for this).

### Session lifecycle state machine

Session controls live in the chrome row and are owned by `SessionController`. `Mode` drives chrome-button visibility; events drive state transitions.

```
                                   +----------------------------+
                                   |          Config            |
                                   | (no active session row;    |
                                   |  pilot list visible;       |
                                   |  "Start" button)           |
                                   +-------------+--------------+
                                                 | Start
                                                 v
   +------------------+  Pause   +-------------------------------+
   |     Paused       | <------- |            Running            |
   | (writer drops    |          | (FSW tails gamelogs;          |
   |  events;         | Resume   |  events INSERT to SQL;        |
   |  timer paused)   | -------> |  1 Hz stats refresh)          |
   +-------+----------+          +---------------+---------------+
           |                                     |
           | Stop                                | Stop
           |                                     |
           +----------------+--------------------+
                            v
                +---------------------------+
                |         Stopped           |
                | (final stats frozen on    |
                |  screen; "New Session"    |
                |  button visible)          |
                +-------------+-------------+
                              | New Session
                              v
                          (back to Config; pilot selection preserved)
```

App-launch with an open `session_time` row in the database lands directly in **Paused** so the user explicitly decides whether to resume the prior session or stop it.

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
        |  - Reads new bytes; splits on \r\n
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
        |       no active session         -> drop
        |       state == Paused           -> drop
        |       Tick/Crit with TypeId=null -> drop (parser already warned)
        |       Residue with TypeId=null   -> INSERT row with NULL type_id/volume_m3
        |  - Bounded Channel<MiningEvent> handed to a single background consumer
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
                            stationId, yieldPct, priceMode)
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
        |     IMarketPriceFetcher.EnsurePricesAsync(materialTypeIds, stationId, priceMode)
        |       |
        |       v
        |     MarketPriceFetcher
        |       - Skip any material already priced for the active mode
        |       - Fetch a gap (no usable price for the mode) at most once per
        |         15 min per (session, type) via an in-memory attempt map
        |       - On a gap: GoonMetricsPriceClient HTTP GET
        |       - INSERT-only into `market_data`
        |       - On transport/parse failure: log Warning, return what we have
        |
        |  Step 4 - For each material:
        |     price = SELECT isk_buy/isk_sell FROM market_data ORDER BY fetched_at DESC LIMIT 1
        |     totalIsk += adjustedQty * price
        |     critIsk  += adjustedCritQty * price   (when crit subset is present)
        |     Missing price -> 0 contribution + Warning log
        |
        v
IskCalculationResult { TotalIsk, CritIsk, IskByInputType }
```

`IIskCalculator` exposes two overloads: the original single-dict
`ComputeAsync(typeIdToUnits, ...)` and the two-dict
`ComputeAsync(typeIdToUnits, critTypeIdToUnits, ...)`. The repository now
calls the two-dict overload exclusively so it derives Total ISK + Crit ISK
in one pass per pilot (shared price cache, exact math, single round-trip
through `EnsurePricesAsync`). `IskByInputType` reflects the FULL units and
drives the Reports per-ore breakdown widget; `CritIsk` powers the
`Crit ISK %` stat on the live + saved-single + compare SessionStats widgets.

Configurable from the Settings dialog: refining yield percentage (50.0 - 90.6), price mode (buy / sell / split), and the station to price against (Jita 60003760 by default; C-J6MT 1049588174021 also seeded). Custom stations can be added.

### Daily SDE refresh flow

```
EverefSdeUpdater.CheckAndUpdateAsync (at startup)
        |
        v
HEAD https://data.everef.net/reference-data/reference-data-latest.tar.xz
        |
        | If Last-Modified <= local meta.everef_last_modified -> NoUpdateNeeded
        |
        v
GET the .tar.xz to a temp folder under the SDE storage parent
        |
        v
Stream-decompress (SharpCompress) into a per-snapshot temp directory
        |
        v
EverefSdeFormatConverter
        |  - Reads the JSON tree (types/, materials/, stations/, ...)
        |  - INSERTs into a *new* SQLite file in the same temp folder
        |  - Validation: SELECT COUNT(*) FROM types must be >= 10,000
        |
        v
Atomic rename: temp .sqlite -> live SDE path
        |
        v
Raise SdeUpdater.Updated event
        |
        v
SqliteSdeLookup.LoadAsync (re-reads in-memory caches)
```

Failure modes (network down, malformed payload, validation fail) are non-fatal: a Warning is logged and the existing SDE keeps serving lookups.

---

## Data stores

Two SQLite files under `%APPDATA%\EveDashboard\`:

### `session.sqlite` — sessions, events, settings, prices

```
+---------------------------------------------+
| session                                     |
|---------------------------------------------|
| session_id     INTEGER PK AUTOINCREMENT     |<------+
| name           TEXT NOT NULL                |       |
| notes          TEXT                         |       |
+---------------------------------------------+       |
                                                       | FK
+---------------------------------------------+       |
| session_time                                |       |
|---------------------------------------------|       |
| session_time_id INTEGER PK AUTOINCREMENT    |       |
| session_id      INTEGER NOT NULL  ----------+-------+
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
| session_id INTEGER NOT NULL ----------------+---+---+
| pilot_id   INTEGER NOT NULL ----------------+---+
| PRIMARY KEY (session_id, pilot_id)          |
+---------------------------------------------+
                                                       |
+---------------------------------------------+       |
| log                                         |       |
|---------------------------------------------|       |
| log_id        INTEGER PK AUTOINCREMENT      |       |
| session_id    INTEGER NOT NULL  ------------+-------+
| pilot_id      INTEGER NOT NULL              |
| eve_timestamp TEXT (ISO-8601 UTC)           |
| event_kind    TEXT  CHECK IN                |
|              ('Tick','Crit','Residue',      |
|               'Depleted')                   |
| type_id       INTEGER (NULL on Residue)     |
| units         INTEGER                       |
| volume_m3     REAL    (NULL on Residue)     |
+---------------------------------------------+

+---------------------------------------------+   +-------------------------------+
| setting                                     |   | market_stations               |
|---------------------------------------------|   |-------------------------------|
| key   TEXT PK                               |   | station_id   INTEGER PK       |<-+
| value TEXT NOT NULL                         |   | system_name  TEXT NOT NULL    |  |
+---------------------------------------------+   | station_name TEXT NOT NULL    |  |
   keys: schema_version, theme,                   | region_id    INTEGER          |  |
         refining_yield_percent, price_mode,      | region_name  TEXT             |  |
         selected_station_id, gamelog_path,       +-------------------------------+  |
         window_geometry, widget_layout,                                             |
         session_stats_config,                                                       |
         per_pilot_stats_config.<pilot_id>                                           |
                                                                                     |
+---------------------------------------------+                                      |
| market_data                                 |                                      |
|---------------------------------------------|                                      |
| marketdata_id INTEGER PK AUTOINCREMENT      |                                      |
| type_id       INTEGER NOT NULL              |                                      |
| station_id    INTEGER NOT NULL  ------------+--------------------------------------+
| fetched_at    TEXT (ISO-8601 UTC)           |
| isk_buy       REAL  (NULL allowed)          |
| isk_sell      REAL  (NULL allowed)          |
+---------------------------------------------+
   IDX: (type_id, station_id, fetched_at DESC)
```

Full reference: [session-schema.md](session-schema.md), [market-schema.md](market-schema.md).

### `sde\eve.sqlite` — EVE Static Data Export

The subset of the everef.net snapshot the dashboard actually reads: published types, the `is_ore` flag, `portion_size` (for `volume_m3`), and `type_materials` (recursive reprocessing). Everything else is intentionally skipped to keep the file small. An embedded fallback (`src/EveDashboard.Sde/Data/sde-fallback.sqlite`) is extracted on first launch so the app boots without network.

Full reference: [sde-schema.md](sde-schema.md).

---

## User-facing surface

### Widgets

| Widget | Source | Stats / controls |
| --- | --- | --- |
| `SessionConfig` | `Widgets/Session/SessionConfigView.xaml` | Pilot checklist, session-name template (`{{$DATE}}`, `{{$TIME}}`, `{{$DATETIME}}`, `{{$CHARS}}`). |
| `SessionStats` | `Widgets/Session/SessionStatsView.xaml` | Display name, total time mined, total units, total m3, total ISK, plus per-pilot rollups. |
| `PerPilotWidget` | `Widgets/Pilot/PerPilotWidgetView.xaml` | Per-pilot units mined, m3, ISK, ISK/hr, last event, last update. One per active pilot. |

Every widget uses the shared `WidgetBase` chrome (close button + cog). The cog opens a popup that lets the user reorder stat rows by drag-and-drop, hide individual rows, and change emphasis (None / Header0 / Header1 / Header2 / Hero) per row.

### Settings dialog

Opens from the **Settings** chrome button. Fields:

- Theme (Light / Dark)
- Gamelog folder (with **Pick Folder** browse)
- Refining yield % (50.0 - 90.6, clamped)
- Price mode (Buy / Sell / Split)
- Selected pricing station (drop-down from `market_stations`)
- Add / Remove market stations (deferred until **Apply**)

Apply persists everything atomically; the theme reapplies live.

### Bootstrap-failure dialog

If the gamelog path is missing or the YAML config is malformed, the same Settings dialog opens in **bootstrap-failure mode** before the main window is ever created: only the gamelog-folder field is editable, and Apply re-runs the startup chain in place. Cancel exits the app cleanly.

### Reports window — Import / Export sessions

The Reports window provides round-trippable JSON export + import and human-readable Excel export of saved sessions:

- **Right-click a session in the sidebar** -> **Export to JSON...** writes a single-session `.json` file mirroring the SQLite tables, with a `_manifest` block (format / schema_version / app_version / exported_at / session_id / session_name) and arrays for `session`, `session_time`, `pilot`, `session_pilot`, `log`, `market_stations`, `market_data`. Settings are deliberately not exported.
- **Right-click a session in the sidebar** -> **Export to Excel...** writes a single-session `.xlsx` workbook with a Summary tab first, then one tab per database table, then one tab per pilot. Excel export is one-way; there is no Excel import path.
- **Import Session...** button at the top of the Reports window reads a `.json` file produced by Export to JSON. The import is atomic; auto-incremented IDs are regenerated; existing pilots / market_stations are preserved on collision (Warning logged if names differ); a mismatched `_manifest.schema_version` hard-fails the import with a clear message (no migration — pre-release rule).
- **Right-click a session in the sidebar** -> **Delete...** removes the session and its FK-cascading rows (`session_time`, `session_pilot`, `log`) atomically after a confirm dialog. Pilots, `market_data`, and `market_stations` are left alone (no direct session FK; same rule the user stated for market data).
- A status row at the bottom of the Reports window surfaces `Imported: ...` / `Exported: ...` / `Deleted: ...` / `Import failed: ...` / `Export failed: ...` / `Delete failed: ...` after each action.

---

## Configuration

### `%APPDATA%\EveDashboard\config.yaml`

Created on first launch (if a `Documents\EVE\logs\Gamelogs` folder exists at autodetect time it is filled in for you):

```yaml
gamelog_path: C:\Users\<you>\Documents\EVE\logs\Gamelogs
```

Environment variables in `gamelog_path` (e.g. `%USERPROFILE%`) are expanded.

### Setting keys (in the SQLite `setting` table)

| Key | Type | Notes |
| --- | --- | --- |
| `schema_version` | int as text | Currently `1`. |
| `theme` | string | `Light` or `Dark`. |
| `refining_yield_percent` | float as text | Clamped to [50.0, 90.6]. |
| `price_mode` | string | `Buy` / `Sell` / `Split`. |
| `selected_station_id` | int as text | Default `60003760` (Jita IV - Moon 4 - Caldari Navy Assembly Plant). |
| `window_geometry` | JSON | Window pos/size. |
| `widget_layout` | JSON | Order + hidden flag for each widget. |
| `session_stats_config` | JSON | Stat-row order / visibility / emphasis for the SessionStats widget. |
| `per_pilot_stats_config.<pilot_id>` | JSON | Same, per pilot. |
| `reports_column_widths.<grid_key>` | JSON | Reports DataGrid column widths per grid type (column index -> pixel width); only user-resized columns are stored. |
| `reports_text_size.<widget_type>` | JSON | Reports table-widget text emphasis per widget type (element key -> `StatEmphasis`); only elements that deviate from their default are stored. |

### Logs

`%APPDATA%\EveDashboard\logs\EveDashboard.log` — rolling daily file (Serilog). Levels: Debug to file, Information to console (when run from a terminal).

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
