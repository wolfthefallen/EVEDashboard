# Changelog

Release notes for EVE Dashboard, newest first.

**How a release happens.** The CI workflow (`.github/workflows/ci.yml`) creates the GitHub
release itself: when a commit lands on `master` whose `<Version>` in `Directory.Build.props` has no
`v<Version>` tag yet, it builds, tests, publishes the single-file `EVEDashboard.exe` (+ a zip), and
publishes release `v<Version>` with the matching `## v<Version>` section of this file as the notes
(GitHub's auto-generated notes are used if the section is missing).

To ship a version: bump `<Version>`, write its `## v<Version>` section here, merge to `master`.
Inside a version section use `###` or lower for sub-headings - `##` marks the next version.

## v0.1.4

**New**
- **Build from history** — forgot to press Start? Reports has a new `Build from history...` button: pick a date/time window (EVE time) and a fleet, and the session is rebuilt straight from your gamelogs. Saved sessions that overlap the window are listed so you choose what to combine; anything you leave out is excluded for those pilots, so nothing is ever counted twice.
- **Combine sessions** — tick two or more saved sessions in the Reports sidebar, right-click, `Combine selected...`, and they become one session. If a session brings pilots your fleet doesn't have, the app offers to create a union fleet.
- **Archived sessions** — the originals of a combine are archived, not deleted: hidden from the sidebar, Lifetime Totals, and Haul Value. A `Show archived` checkbox in the sidebar reveals them, and deleting the combined session brings them back.
- **Session name tokens** — `{{$STARTDATE}}`, `{{$ENDDATE}}` (e.g. `16Aug`) and `{{$YYYY}}`; history builds default to `16Aug - 19Aug 2026 history`.

**Improved**
- Historical sessions are valued with the market prices the app captured during that window when it has them; otherwise today's prices are used and the summary says so.
- Idle stretches inside a rebuilt window are treated exactly like a live session would: paused after your auto-pause idle minutes, so ISK/hour stays honest.
- Documentation refreshed to match the app.

**Under the hood**
- Your database upgrades itself on first launch (adds the archive fields); sessions, prices, and settings carry over, and older exports still import.

## v0.1.3

**New**
- **Lifetime Totals window** — all-time time tracked, ISK, and refined output across every session, in its own window launched from Reports.
- **Excel export now includes refined output** — quantity + ISK per material on the Summary tab and on every pilot tab.
- **Resizable Reports tables** — drag column widths (they persist) and adjust per-element text size with the new "A" cog.
- **Refined output everywhere** — per-material quantities on live and saved widgets, plus a setting to hide it on pilot widgets.
- **Auto-pause sound volume** — a slider to set how loud the pause/resume chimes are.

**Improved**
- Smarter market pricing — fetches only missing prices instead of re-pulling every 60 seconds.

**Fixed**
- Refined output now clears properly when you start a new session (#2).

## v0.1.2

This release is for a rare but serious bug that could make a mining session silently stop recording. This release fixes this bug. It is the only change.

### Fixed

- **Live stats no longer freeze during long, busy sessions.** Under heavy load —
  typically a large fleet mining for a while — the dashboard could quietly stop
  recording new mining while still appearing to run. The freeze even hid itself
  (prices kept updating, so the app looked healthy). The root cause was a
  thread-safety bug in the database layer; it's now fully serialized, so capture
  stays reliable for the **entire** session no matter how hard you're mining.

### Hardening

- Reworked the app so background work that hits an unexpected error **logs and
  recovers** instead of silently giving up
- Added automated tests that reproduce the original race condition.

## v0.1.1

Three additions this release: a faster way to start sessions, live visibility into what your ore refines into, and a heads-up when a new version ships.

### What's new

#### Fleets — start a session in a couple of clicks
Save your regular line-ups as named Fleets, each pilot tagged with their role (Miner / Booster / Cyno / Combat). Pick a fleet and start — no more re-ticking the same pilots every time. Your default fleet is selected automatically when you open New Session, and you can sort the pilot list while building one.

#### Refined output, tracked live
The Session and per-pilot widgets now show a running total of every refined material your ore produces — Tritanium, Pyerite, Mexallon, moon materials, ice products — added as you mine them. The same breakdown shows up in Reports for any saved session. Quantities line up with the numbers behind your ISK. Not interested? Turn it off with the auto-show toggle in Settings, or hide individual materials per widget (they stay hidden).

#### Launch update check
On startup the app checks whether a newer release is out and gives you a one-click link. No auto-updating — just a quiet nudge.

---

### Auto DB update
Your local database with all your session data will auto upgrade on launch with this new version.
Your existing sessions, captured prices, and settings carry over automatically — the database upgrades itself on first launch. Old session exports still import cleanly.

## v0.1.0

First public release.

The first public build of EVE Dashboard — a free, native-Windows dashboard that shows your
**live EVE Online mining stats** while you play, by reading the game's own log files. No
automation, no account login, no interaction with the game client.

### Highlights

- **Live mining tracker** — mining ticks appear on screen about a second after EVE writes them.
- **Per-pilot stats** — units mined, volume (m³), ISK value, ISK/hour, and last activity for
  every character in your fleet.
- **ISK valuations** — your ore is reduced to refined materials and priced against Jita (or a
  station you choose), using live public market data.
- **Sessions** — start / pause / resume / stop / new, with named sessions and name templates.
  Auto-pauses when mining stops and auto-resumes when it picks back up.
- **Reports** — review saved sessions, compare sessions side by side, and see per-ore
  breakdowns and Crit ISK %.
- **Import / Export** — round-trippable JSON export + import, plus Excel (.xlsx) export of
  saved sessions.
- **Crystal Diagram** — a reference view for mining crystals and ore anomalies.
- **Make it yours** — drag to reorder widgets, hide what you don't need, tune which stats show
  and how they're emphasised, and switch between Light and Dark themes. Your layout persists
  across restarts.

### Your data stays on your PC

EVE Dashboard reads your local gamelogs and stores everything in its own database under
`%APPDATA%\EveDashboard\`. It **never uploads your data**, never logs into your EVE account,
and never touches the game client. The only network traffic is read-only downloads of public
market prices (GoonMetrics) and EVE's Static Data Export (everef.net).

### Requirements

- Windows 10 or 11
- EVE Online launched at least once on this PC (so your gamelog folder exists)

### Install & run

Download `EVEDashboard.exe` below and run it — it's self-contained, with no installer and no
separate .NET runtime required.

### Notes & known limitations

- This release focuses on **mining**; other activity types (ratting, wallet) are planned.
- Early public build in active use — bug reports and feedback are very welcome via Issues.
- On first launch it builds its local database and fetches the latest Static Data Export
  (falling back to a bundled copy if you're offline).

### License & legal

Released under the MIT License. EVE Online and the EVE logo are registered trademarks of
CCP hf.; this is a third-party tool, not affiliated with or endorsed by CCP. See
`THIRD-PARTY-NOTICES` and the README for full details.
