# Manual tests — EVE Dashboard

The single canonical manual-test document for the dashboard. Anything that
needs hands-on WPF interaction or a real EVE client lives here; anything
unit-testable lives under `tests/EveDashboard.*.Tests/`.

This file is workflow-ordered by application section, top to bottom:
bootstrap -> Settings -> live session -> live widgets -> Crystal Diagram ->
Reports. Each section has its own Setup, numbered Steps, and Cleanup so
you can run any one section in isolation.

**Maintenance rule:** when a code change adds, alters, or
removes any manual-verifiable behaviour, update this file in the same
change. Update existing scenarios, add new ones, delete obsolete ones —
do not create a sibling file, do not leave a stale scenario behind.

---

## Shared setup

These conditions apply to every section unless overridden in the section's
own Setup.

- Windows 10 / 11 host with .NET 8 SDK installed.
- EVE Online installed and launched at least once on this user account so
  `%USERPROFILE%\Documents\EVE\logs\Gamelogs\` exists.
- At least 2-3 mining-capable EVE characters with recent gamelog files in
  that folder.
- Build clean: `dotnet build` exits 0 / 0 / 0.
- Tests green: `dotnet test` reports all passing (skipped tests by design
  are OK — see the test output).
- Launch from source: `dotnet run --project src/EveDashboard.App.Wpf`.

App data lives under `%APPDATA%\EveDashboard\` (`config.yaml`,
`session.sqlite`, `sde\eve.sqlite`, `logs\`). Per `MEMORY.md ->
feedback_no_migrations_until_release`, deleting that folder between rounds
is the canonical reset.

---

## 1. App launch + bootstrap failure re-route

Verifies the chain `BootstrapConfigLoader -> autodetect -> validate ->
SDE update -> schema init` and its failure re-routes to the Settings
dialog (decision 22.2-B).

### Setup

Make a backup of `%APPDATA%\EveDashboard\config.yaml` if it exists,
so the recovery steps can restore your real path.

### Steps

1. **Autodetect happy path.** Delete `%APPDATA%\EveDashboard\`
   entirely. Launch the app.
   - Expected: MainWindow opens at 1280x800 centered, dark theme by
     default, log shows
     `Bootstrap autodetected gamelog path: <Documents>\EVE\logs\Gamelogs`.
     SessionConfig widget shows pilot list.
2. **Malformed YAML re-route.** Create `config.yaml` with
   `gamelog_path: [unterminated`. Launch.
   - Expected: Settings dialog opens with only the Logs section visible;
     red banner explains malformed YAML and ends with "Edit the gamelog
     folder below and click Apply to retry." Set the real path via
     Browse... and click Apply -- dialog closes, MainWindow opens.
3. **AutodetectFailed re-route.** Delete `config.yaml`. Rename
   `%USERPROFILE%\Documents\EVE\logs\Gamelogs` to `Gamelogs_disabled` (skip
   if the folder doesn't exist; failure already triggers). Launch.
   - Expected: Settings dialog opens with the AutodetectFailed banner and
     the autodetect path in the field. Set the real path -> Apply ->
     MainWindow opens. Restore the renamed folder.
4. **ZeroCharacters re-route.** `mkdir C:\temp\empty`. Edit `config.yaml`
   to `gamelog_path: C:\temp\empty`. Launch.
   - Expected: Settings dialog opens with the ZeroCharacters banner.
     Set the real path -> Apply -> MainWindow opens.
5. **SaveAsync retry-once fallback.** `attrib +R` on `config.yaml`. Trigger
   any failure mode (e.g. re-edit to garbage). In the dialog, set a valid
   path and Apply.
   - Expected (1st Apply): banner changes to
     `Could not save: <message>. Try again or close to exit.` Dialog
     stays open.
   - Click Apply again WITHOUT clearing the read-only attribute.
   - Expected (2nd Apply): MessageBox "Could not save your gamelog
     folder" appears, then the app exits with code 1. `echo $LASTEXITCODE`
     in PowerShell confirms `1`. Clear the read-only attribute.
6. **Transient SaveAsync recovery.** Same setup as step 5; click Apply
   once (banner shows save error). Clear the read-only attribute WITHOUT
   closing the dialog. Click Apply again.
   - Expected: SaveAsync succeeds, chain re-runs, MainWindow opens (retry
     counter reset).
7. **Close in failure mode = Shutdown(1).** Trigger any failure mode.
   Click Close / X / Alt-F4.
   - Expected: app exits silently with code 1, no "discard?" prompt
     (suppressed per decision 22.S).
8. **Chain idempotency on retry.** After any successful retry, open the
   session SQLite and run
   `SELECT value FROM setting WHERE key='schema_version';` -- should
   return `1` with no double-bump. App log should show exactly one
   `everef.net` HEAD probe per launch (no double-fetch).
9. **Window geometry persistence.** Resize MainWindow to ~900x700, move
   to top-left of the primary monitor, wait ~1 second, close. The
   `setting` table now has a `window_geometry` row with JSON like
   `{"Width":900,"Height":700,"Left":0,"Top":0,"IsMaximized":false}`.
   Relaunch -- window opens at exactly those bounds. Maximize, wait,
   close, relaunch -- opens maximized; Restore-down snaps back to the
   pre-maximize bounds.

### Cleanup

Restore `config.yaml` from your backup. Restore the renamed Gamelogs
folder. Delete `C:\temp\empty`. Clear any read-only attribute on
`config.yaml`. Optional full reset: delete `%APPDATA%\EveDashboard\`.

---

## 2. Settings dialog

Verifies validation, Apply / Close semantics, dirty-flag discard prompt,
theme live re-apply, station list edits, and refining-yield / price-mode
persistence.

### Setup

Launch the app. The chrome bar has a `Settings` button at the right.

### Steps

1. **Open / close clean.** Click Settings -> dialog opens modal over
   MainWindow. Click Close -> dialog closes immediately, no warning.
2. **Theme live re-apply.** In Settings, switch Theme to the opposite
   value (Dark <-> Light). Click Apply. Both MainWindow and the open
   Settings dialog re-theme immediately. Close. Relaunch -> theme
   persists.
3. **Refining yield validation.** Set yield to `95` -> Apply ->
   MessageBox "Refining yield must be between 50 and 90.6 (you entered
   95)". Set to `abc` -> Apply -> MessageBox "Refining yield is not a
   valid number (you entered 'abc')". Set to `75.5` -> Apply -> no
   prompt, persisted. Close + reopen -> shows 75.5.
4. **Gamelog folder validation.** Type `Z:\nope` -> Apply -> MessageBox
   "Gamelog folder does not exist". Browse to `C:\Windows` -> Apply ->
   "Gamelog folder contains no EVE gamelog files". Browse to the real
   EVE Gamelogs path -> Apply -> no prompt.
4a. **Auto-pause section validation + persistence (plan 37).** In the
    Auto-pause section, set Idle minutes to `0` -> Apply -> MessageBox
    lists "Idle minutes must be between 1 and 60 (you entered 0)". Set
    to `abc` -> Apply -> MessageBox "Idle minutes is not a valid integer
    (you entered 'abc')". Set to `10` -> Apply -> no prompt. Uncheck
    "Play sounds on auto-pause / auto-resume" -> Apply -> no prompt.
    Close + reopen -> both values persist. Restore: Idle minutes `5`,
    sound enabled checked, Apply.
5. **Apply / Close pattern.** Apply does NOT close the dialog. Close is
   the only dismiss verb.
6. **Close-with-unsaved-changes.** Make a change without Apply -> Close
   -> MessageBox "You have unsaved changes. Discard them?" Click No ->
   dialog stays open with the dirty value. Click Close -> Yes -> dialog
   closes. Reopen -> the dirty edit was discarded.
7. **Markets Add.** Click `Add station`. With all fields empty, OK ->
   validation lists all required-field errors. Fill in: station_id
   `60008494`, system_name `Amarr`, station_name `VIII - Oris - Emperor
   Family Academy`, region_id `10000043`, region_name `Domain`. OK ->
   sub-dialog closes, Amarr appears in the markets list. Apply -> Close
   -> Reopen -> Amarr is persisted.
8. **Markets Remove.** Select Amarr in the markets list -> Remove
   station -> Amarr disappears from the list AND from the Selected
   station ComboBox. Apply -> persisted.
9. **Markets pending discard.** Add a station, then Close without Apply
   and click Yes on the discard prompt. Reopen -> the added station is
   gone.
10. **Selected station + Price mode persistence.** Change Selected
    station from Jita to C-J6MT, change Price mode from Buy to Sell.
    Apply -> Close -> Reopen -> both values are persisted.

### Cleanup

Restore your preferred values: theme, refining yield (90.6), price mode
(Buy), selected station (Jita), gamelog folder (real EVE path),
auto-pause idle minutes (5), auto-pause sound enabled (checked). Remove
any test stations added during steps 7 and 9.

---

## 3. New Session (Fleets) + chrome bar lifecycle

Verifies the plan-42 New Session window (saved Fleets are the only way to
start a session), the four-mode (`Config / Running / Paused / Stopped`)
chrome-button surface, app-launch Paused detection, and template variables.

### Setup

Delete `widget_layout` from the `setting` table (or the whole
`session.sqlite` for a full layout reset). Launch the app. This first launch
after plan 42 has NO saved fleets yet.

### 3a. Empty state + build a fleet

1. **Open the window.** Click `New Session` in the chrome bar. The modal
   New Session window opens showing a **Fleets** header, an **empty-state
   prompt** ("No fleets yet. Click Create Fleet to make one ..."), a
   **Create Fleet** button, the session-name textbox, and `Start`.
   - Expected: `Start` is DISABLED (no fleet selected).
2. **Create Fleet -> pilot picker.** Click `Create Fleet`. The pilot picker
   appears: a **Sort by** dropdown plus one row per discovered pilot, each a
   checkbox + a role dropdown (Miner / Booster / Cyno / Combat). `Add Fleet`
   is disabled until at least one pilot is checked.
3. **Sort the picker.** Change **Sort by** through its four options:
   `Last active (newest)` [default], `Last active (oldest)`, `Name (A-Z)`,
   `Name (Z-A)`. The pilot rows reorder accordingly. Re-check the same pilot
   after re-sorting -> its check and chosen role are preserved across the
   sort change (selections are never dropped).
4. **Pick pilots + roles.** Check 2+ pilots; set one to `Booster` and the
   rest to `Miner`. Click `Add Fleet`.
   - Expected: the confirm panel appears listing the picked pilots with their
     roles (e.g. `PilotOne  -  Miner`, `PilotTwo  -  Booster`), a **name**
     textbox, a **Make default fleet** checkbox, and `Save`.
5. **Save the fleet.** Type a name (e.g. `Moon crew`), tick **Make default
   fleet**, click `Save`.
   - Expected: the picker/confirm panels hide; the Fleets list now shows
     `Moon crew` marked `(default)` and already selected; `Start` is ENABLED.

### 3b. Start a session from a fleet (plan 37: lands in Paused)

6. **Confirm + Start.** With `Moon crew` selected, type the session name
   `manual-{{$DATE}} with {{$CHARS}}` and click `Start`.
   - Expected: the dialog CLOSES; `SessionStats` appears at slot 0 with
     elapsed time `00:00:00` (FROZEN). Per-pilot widgets at slots 1+, each
     already showing its fleet role on first render (the Booster's border +
     `[Booster]` tag, no flash of Miner). Chrome bar shows `Continue` +
     `Stop`. Session name reads e.g.
     `manual-2026-06-11 with PilotOne, PilotTwo`. `ActiveSessionContext.State`
     is Paused, so live mining events are dropped UNLESS they auto-resume per
     section 6.
7. **Adjust roles mid-session, then Continue.** Optionally right-click a
   per-pilot widget to change its role. Click `Continue`.
   - Expected: chrome bar flips to `Pause` + `Stop`; elapsed time ticks from
     `00:00:00` at 1 Hz; live mining events accumulate normally.
8. **Manual pause + Continue.** Click `Pause` -> SessionStats freezes; chrome
   shows `Continue` + `Stop`; stats do NOT advance. Click `Continue` ->
   chrome back to `Pause` + `Stop`; elapsed picks up; ticks accumulate again.
9. **Stop with confirm.** Click Stop -> themed ConfirmDialog `Stop session?`.
   Click Cancel -> mode stays. Click Stop again -> `Stop` in the dialog ->
   Mode = Stopped; SessionStats keeps frozen final stats; per-pilot widgets
   stay; chrome shows `New Session` only.
10. **New Session re-open -> default auto-selected.** Click `New Session`.
    - Expected: the New Session window opens with `Moon crew` already selected
      (the default fleet), `Start` enabled. SessionStats / per-pilot widgets
      behind the modal still show the prior session's data.

### 3c. Manage fleets

11. **Single-select.** Create a second fleet (3a steps 2-5, name it `Solo`,
    do NOT make default). In the Fleets list, click `Solo`'s checkbox -> it
    selects and `Moon crew` deselects (only one fleet selectable at a time).
12. **Caret expander.** Click a fleet row's caret -> it expands to list that
    fleet's pilots + roles. Collapse it again.
13. **Make default (right-click).** Right-click `Solo` -> `Make default`.
    - Expected: the `(default)` marker moves to `Solo`. Close + reopen the
      window -> `Solo` is auto-selected.
14. **Delete (right-click), including the default.** Right-click `Solo` ->
    `Delete` -> confirm. The row disappears. Because `Solo` was the default,
    no fleet now shows `(default)`; reopening the window auto-selects nothing
    and (if it was the only remaining fleet) shows the empty-state prompt with
    `Start` disabled.
15. **Duplicate name rejected.** Create a fleet, and in the confirm panel type
    a name already in use -> `Save` -> an inline error appears and the confirm
    panel stays open (nothing saved).
16. **Offline fleet pilot skipped at Start.** Build a fleet that includes a
    pilot, then close that pilot's EVE client (so it has no current gamelog).
    Select the fleet, Start.
    - Expected: the session starts with the still-logged-in pilots; the
      offline pilot is skipped (a Warning is logged, no per-pilot widget for
      it). If NONE of the fleet's pilots are logged in, Start is blocked and an
      inline error appears in the dialog (the dialog stays open).

### 3d. App-launch Paused detection (unchanged by plan 42)

17. **App-launch Paused detection.** Start a fresh session (let Continue bring
    it to Running), then close MainWindow without Stop. Relaunch.
    - Expected: SessionStats appears at its saved slot with the prior
      session's frozen elapsed time. Chrome shows `Continue` + `Stop` ONLY.
      NO `New Session`/`Start`. Per-pilot column empty until Continue.
18. **Continue from app-launch Paused.** Click `Continue` -> Mode = Running;
    per-pilot widgets appear and rehydrate from stored log rows.
19. **Stop from app-launch Paused.** Re-enter the state (step 17). Click Stop
    -> confirm dialog appears (consistent across all Stop paths) -> Stop ->
    Mode = Stopped.

### Cleanup

Click `New Session` then Cancel to close the modal. Close the app. Optional
reset: delete `widget_layout` (and the `fleet` / `fleet_pilot` tables +
`default_fleet_id` / `pilot_sort_order` setting rows) from `session.sqlite`.

---

## 4. Live widgets — layout, hide / show, stat config

Verifies the WrapPanel layout, drag-to-reorder, per-widget hide via the
chrome `x`, the `Hidden widgets v` popup, and the stat-config cog popup
(reorder / hide / emphasis / persistence / preview / per-pilot
independence).

### Setup

Have an active session running (per section 3) with 2+ per-pilot widgets
visible.

### Steps

1. **Drag-reorder per-pilot.** Drag a per-pilot widget chrome row onto
   SessionStats and drop -> the pilot widget lands at index 0;
   SessionStats moves to index 1. Drag back to restore.
2. **Hide via x.** Click the `x` on a per-pilot widget chrome -> widget
   disappears, layout reflows. Chrome bar's `Hidden widgets v` button
   appears (was hidden when nothing was hidden).
3. **Show via popup.** Click `Hidden widgets v` -> popup lists the
   hidden widget with a `Show` button. Click Show -> widget reappears
   at its previous slot; popup closes; `Hidden widgets v` button hides
   itself (count back to 0).
4. **Hide SessionStats / SessionConfig.** Same hide / show flow works
   on SessionConfig (Config mode) and SessionStats (Running / Stopped).
5. **Cross-restart layout persistence.** Hide some widgets, reorder
   others. Close the app. Relaunch the same session via Resume.
   - Expected: hidden widgets stay hidden; visible widgets keep their
     order; `Hidden widgets v` count matches.
6. **WrapPanel reflow.** Narrow the window -> widgets wrap to one
   column; vertical scrollbar appears if needed. NO horizontal
   scrollbar at any width. Widen -> widgets unwrap.
7. **Cog button visibility.** SessionConfig chrome has `x` only, NO
   cog. SessionStats and per-pilot widgets show BOTH cog and `x`.
8. **Per-pilot stat-config popup.** Click cog on a per-pilot widget.
   Popup opens with: header "Customize stats"; Reorderable section
   listing `Pilot, m^3/sec, ISK/h, Total ISK, Total m^3, Crit m^3,
   Crit count, Crit chance` (8 rows) each with checkbox + label
   preview + emphasis ComboBox + drag handle on the right;
   Hide-only section with `Last event, Last updated` (no emphasis,
   no drag); Apply + Close at the bottom right.
9. **Drag-reorder a row.** Drag `ISK/h` to position 0. Apply. The
   widget body re-renders with ISK/h at the top within ~1 second.
   Reopen popup -> ISK/h still at position 0.
10. **Hide a stat.** Uncheck `Crit chance`. Apply. Row disappears
    from the widget body. Reopen popup -> the row's checkbox is
    unchecked AND its position in the order is preserved.
11. **Re-show preserves slot.** Re-check `Crit chance`. Apply ->
    appears at its prior position (NOT at the end of the list).
12. **Hide-only rows.** Uncheck `Last updated` in the hide-only zone
    -> the chrome `Last updated: HH:MM:SS` line disappears.
    Re-check -> reappears. Same for `Last event` on per-pilot.
13. **Emphasis with live preview.** Open another pilot's popup.
    Change `ISK/h` emphasis to `Hero`. The row INSIDE the popup
    immediately re-renders at 22pt AccentBrush (preview). Click
    Apply -> widget body renders ISK/h at 22pt AccentBrush
    (full-width, no `ISK/h:` label prefix). Other pilots'
    widgets are unaffected.
14. **Per-pilot independence.** Confirm that stat-config changes on
    one pilot do not affect others (per-pilot keyed on `pilot_id`).
15. **SessionStats popup (live).** Click cog on SessionStats. Popup
    shows 10 Reorderable rows: `Session (Header1), Total time
    (Hero), m^3/sec, ISK/h, Total ISK, Total m^3, Crit m^3,
    Crit count, Crit chance, Crit ISK %`. Hide-only section: `Last
    updated`.
16. **Live re-render after Apply.** With events arriving, drag `Crit
    chance` to position 0 in the SessionStats popup. Apply.
    SessionStats body re-renders with `Crit chance` at the top in
    sub-second, AND live values continue ticking in place.
17. **Close discards unsaved edits.** Open popup, drag a row, change
    an emphasis, click Close (NOT Apply). Reopen -> the edits are
    NOT present.
18. **Click-outside discards.** Same as 17 but click anywhere
    outside the popup -> StaysOpen=False closes it, drag is NOT
    persisted.
19. **Persist across restart.** Customize several rows, click Apply,
    close the app, relaunch via Resume -> customizations restored.

### Cleanup

Re-show any hidden stats / rows you want visible by default. The
stat-config order and emphasis choices are persisted -- restore them
manually if you want the defaults.

---

## 5. Live stats — ISK pipeline, Crit ISK %, pilot roles

Verifies the GoonMetrics prefetch, real ISK / ISK-per-hour numbers,
Crit ISK % stat, the pilot-role right-click menu, role-driven border
colors + DisplayName tag, the booster-m^3 gate, and the event-time
role-stamping semantics.

### Setup

Confirm SDE present at `%APPDATA%\EveDashboard\sde\eve.sqlite`.
In Settings, confirm: Selected station = Jita (`60003760`), Refining
yield = 90.6, Price mode = Buy. Note current values so you can restore.

### Steps

1. **Prefetch on Start.** Toggle on a mining pilot, click Start.
   Within ~2 seconds, query:
   ```sql
   SELECT COUNT(*) FROM market_data WHERE station_id = 60003760;
   ```
   Expect ~36 rows with `fetched_at` near current UTC. Log line:
   `Prefetch complete: inserted 36 of 36 requested rows.`
2. **Live ISK numbers.** Mine briefly in EVE. Per-pilot `Total ISK`
   and `ISK/h` go from 0 to real values within one tick (~1s).
3. **Aggregate matches.** Session-level `Total ISK` equals the sum
   of per-pilot `Total ISK` values.
4. **Magnitudes sensible.** Tritanium-heavy ores (Veldspar) yield
   small per-unit ISK (Tritanium ~5 ISK). Rare-mineral-heavy ores
   (Crokite -> Zydrine, Megacyte) yield orders-of-magnitude larger
   per-unit ISK.
5. **Crit ISK % stat appears.** On the live SessionStats widget,
   confirm a `Crit ISK %` row exists (default order = 8, after
   `Crit chance`). It renders as `P1` (one-decimal percent). The
   value is `Total ISK from crits / Total ISK mined`; live-updates
   as crit events arrive.
6. **Crit ISK % divide-by-zero guard.** Start a fresh session,
   mine briefly but with NO crits (or with `ZeroIskCalculator`
   conditions). `Crit ISK %` shows `0.0 %`, no exception.
7. **Graceful degradation.** Disconnect from the internet. Stop +
   Start again -> Warning log on HTTP failure. No new `market_data`
   rows; ISK numbers do not change for already-fetched types but
   the app does not crash.
8. **Pilot role right-click menu.** Right-click any per-pilot
   widget body. Context menu opens with four items: Miner,
   Booster, Cyno, Combat. The current role shows a checkmark.
9. **Apply non-Miner roles.** Set pilot A = Booster, B = Cyno,
   C = Combat.
   - Expected: A's border = mint green, header `PilotA [Booster]`.
     B's border = light blue, header `PilotB [Cyno]`. C's
     border = coral/pink, header `PilotC [Combat]`. Borders
     are visibly thicker than Miner's default.
10. **Aggregate filter (eligibility CTE).** Mine with A/B/C.
    Session totals reflect ONLY:
    - Miner: always counted.
    - Booster: excluded until that pilot mines any m^3
      themselves; then included.
    - Cyno: always excluded.
    - Combat: always excluded.
    All three per-pilot widgets remain VISIBLE (decision 1.2 = A);
    Cyno + Combat widgets show 0s (filtered at the CTE level).
11. **Mid-session role change is event-time stamped.** Set A back
    to Miner mid-session. A's border returns to default, header
    drops the `[Booster]` suffix. Session totals do NOT change
    retroactively -- the existing A-Booster rows keep their
    filter status; new events from A are stamped Miner and count
    immediately.
12. **Fleet roles flow into a new session (plan 42).** Stop the
    session. Click New Session, select a fleet whose pilots have
    assigned non-Miner roles (e.g. one Booster), confirm the name,
    Start.
    - Expected: each pilot's role comes from the fleet's stored
      role (NOT from any prior session -- the old default-to-last-
      role lookup is gone). Border + header tag are correct on the
      first widget creation -- no flash of Miner before the first
      stats tick.
13. **Theme reactivity.** With non-Miner pilots visible, swap
    Theme via Settings.
    - Expected: the role-colored borders re-resolve to the new
      theme's brushes (slight color shift -- e.g. Light's
      `#3ECF9E` vs Dark's `#5FE0B8`). No border goes black or
      blank.
14. **No periodic re-fetch (gaps-only).** With the session Running
    and prices captured at Start, mine for several minutes. Replacing
    `<sid>` with the active session id, periodically run:
    ```sql
    SELECT type_id, COUNT(*) FROM market_data WHERE session_id = '<sid>' GROUP BY type_id;
    ```
    - Expected: a fully-priced material's row count does NOT grow
      over time -- the live path never re-fetches a material it
      already has a usable price for (the old ~per-minute refetch is
      gone). Only a material that returned no price at Start may gain
      at most ONE extra row per 15 minutes.
15. **Station change re-prices mid-session (decision 1.4).** With the
    session still Running, open Settings, change Selected station from
    Jita to C-J6MT (`1049588174021`), Apply.
    - Expected: a fresh `Prefetch complete: inserted N of N requested
      rows.` log line appears;
      `SELECT station_id, COUNT(*) FROM market_data WHERE session_id = '<sid>' GROUP BY station_id;`
      now shows rows at the C-J6MT station id; per-pilot / session
      `Total ISK` shifts to the new hub's valuation within a tick.
    - Negative check: while **Paused**, changing the station does NOT
      emit a new prefetch log line (it re-fetches on the next Resume
      instead).

### Cleanup

Restore default Settings values you changed (incl. Selected station =
Jita). Stop the session. Optional: `DELETE FROM market_data;` for a
clean slate next round.

---

## 6. Auto-pause / auto-resume

Plan 37 / plan 50. Verifies that an idle session auto-pauses with a
notification sound, that the next mining event auto-resumes the session with
the triggering event counted in the totals, and that the sound volume slider
scales playback (plan 50).

### Setup

Open Settings; set Idle minutes to `1` (so the test doesn't take 5+
minutes); confirm "Play sounds on auto-pause / auto-resume" is checked.
Apply + Close. Start a session in MainWindow with 1+ mining-active
pilot, click `Continue` to bring it to Running, ensure live ore-tick
events are flowing (TotalM3 advances).

### Steps

1. **Idle auto-pause + sound.** Stop mining in EVE (move ship out of
   mining range, or undock and re-dock). Wait for the most-recent log
   line for the active pilot to be at least 1 minute old.
   - Expected: within the next 1 Hz tick, the session auto-pauses:
     SessionStats freezes; chrome bar flips to `Continue` + `Stop`;
     `audio/Miners_have_stopped.wav` plays once. App log shows
     `SessionController auto-paused session N on 1-minute idle.`
2. **Sound off -> silent auto-pause.** Open Settings, uncheck the
   sound checkbox, Apply, Close. Click `Continue` to resume, mine
   briefly, then idle again for 1+ minute.
   - Expected: auto-pause fires SILENTLY (no WAV) but the visual
     transitions still happen. Re-enable the sound for the rest of
     the section.
3. **Mining event auto-resumes.** With the session in auto-paused
   state, re-engage mining in EVE. Within 1-2 seconds of the first
   new Tick reaching the log file:
   - Chrome bar flips from `Continue` + `Stop` to `Pause` + `Stop`.
   - `audio/Mining_detected.wav` plays once.
   - **The triggering Tick is counted.** `TotalM3 mined` jumps by
     the expected volume, NOT 0. Verify via SQL:
     ```sql
     SELECT COUNT(*) FROM log WHERE session_id = N AND event_kind IN ('Tick','Crit');
     ```
     The count is HIGHER than before the resume (the triggering
     event landed in `log`).
   - Elapsed time picks up where it left off.
4. **Threshold change takes effect mid-session.** With the session
   Running, open Settings, change Idle minutes from `1` to `2`,
   Apply, Close. Stop mining.
   - Expected: auto-pause does NOT fire at the 1-minute mark; it
     fires at the 2-minute mark.
5. **Auto-resume from MANUAL pause.** Click `Pause` to manually pause
   a Running session. Re-engage mining in EVE.
   - Expected: session auto-resumes within 1-2 seconds; sound plays;
     event counts.
6. **Auto-resume from Start-as-paused.** Click `New Session`,
   configure, click `Start`. Session lands in Paused mode (per
   section 3 step 3). Immediately re-engage mining in EVE (or use a
   pilot whose log has very recent activity).
   - Expected: the first Tick reaching the log auto-resumes the
     session and counts -- identical behaviour to the manual-pause
     case in step 5.
7. **No auto-resume on Residue / Depleted.** Set up a T2 crystal
   mining cycle that produces Residue events. Manually `Pause` the
   session.
   - Expected: Residue / Depleted events alone do NOT auto-resume;
     only the following Tick / Crit does. (Residue almost never
     arrives without a preceding Tick, so this is mainly a regression
     guard.)
8. **Sound volume slider (plan 50).** Open Settings -> Auto-pause. A
   **Volume** slider (default ~65%) sits under the sound checkbox.
   - It is ENABLED only while "Play sounds on auto-pause / auto-resume"
     is checked: uncheck the box -> the slider greys out; re-check it
     -> the slider is active again.
   - With the box checked, set the slider low (~15%), Apply, Close, and
     trigger an auto-pause (idle 1+ minute) -> `Miners_have_stopped.wav`
     plays QUIETLY.
   - Reopen Settings, set the slider to 100%, Apply, trigger another
     auto-pause -> the sound is noticeably LOUDER.
   - A fresh install defaults to 65%.

### Cleanup

Stop the session. Open Settings; restore Idle minutes to `5`; ensure
sound enabled = checked and Volume = 65%; Apply; Close.

---

## 7. Crystal Diagram window

Verifies the per-anomaly dropdown, image swap, FancyZones snap
resize, in-session persistence (close + reopen), and cross-restart
persistence of the selected entry.

### Setup

7 per-anomaly PNGs under `images/anomaly_*.png` plus the full chart
at `images/Mineral_to_asteroid_crystal.png`. The user can verify by
listing the folder.

### Steps

1. **Open the window.** Click the chrome `Crystal Diagram` button.
   - Expected: window opens at default 900x650 centered, dark theme.
     First-ever launch shows `All / Full Chart` in the dropdown and
     the full chart image.
2. **Dropdown entries.** Click the dropdown -> 8 entries in order:
   `All / Full Chart`, `Griemeer (Isogen)`, `Veldspar (Tritanium)`,
   `Kylixium (Mexallon)`, `Hezorime (Zydrine)`, `Ueganite (Megacyte)`,
   `Nocxite (Nocxium)`, `Mordunium (Pyerite)`.
3. **Image swap on selection.** Cycle through every entry. Each
   shows its corresponding panel within one frame, no flicker.
4. **FancyZones snap.** Drag-resize, then snap into a FancyZones
   zone smaller than 900x650. Image stretches `Uniform` in both
   cases (regression check against the Bug 1 fix).
5. **In-session persistence.** Leave the dropdown on
   `Mordunium (Pyerite)`. X-close the window. Click Crystal Diagram
   again. Expected: same size / position, dropdown still
   `Mordunium (Pyerite)`, same image.
6. **Cross-restart persistence.** Same setup as step 5 but quit
   the entire app and relaunch. Click Crystal Diagram. Expected:
   dropdown still `Mordunium (Pyerite)`, same image.

### Cleanup

Optionally set the dropdown back to `All / Full Chart` as your
default starting point.

---

## 7.5 About window

Verifies the chrome `About` button (now the rightmost button), the
modeless About window content (version, GitHub link, SDE date,
thank-yous, CCP legal text), and geometry persistence on
close + reopen.

### Setup

The app launched normally with a populated SDE (the bundled fallback
SDE has a build time, so the SDE date line is never `unknown` on a
fresh install).

### Steps

1. **Button placement.** Look at the chrome toolbar's right edge.
   - Expected: order right -> left is `About`, `Settings`, `Reports`,
     `Crystal Diagram`. `About` is the rightmost button; `Settings`
     sits immediately to its left with an 8px gap.
2. **Open the window.** Click the chrome `About` button.
   - Expected: window opens at default 460x560 centered, current theme.
3. **Read each line.** Top to bottom:
   - `EVE Dashboard v<version>` (matches the running release, e.g.
     `0.1.3`).
   - A clickable GitHub link reading
     `https://github.com/wolfthefallen/EVEDashboard`.
   - `SDE data: <yyyy-MM-dd>` (a real date, not `unknown`).
   - A horizontal rule, then three thank-you lines (BETA testers,
     GoonMetrics, everef.net).
   - A second horizontal rule, then the CCP legal paragraph.
4. **GitHub link.** Click the link.
   - Expected: the repo opens in your default browser; the About
     window stays open (it does not navigate itself).
5. **In-session geometry persistence.** Move and resize the window,
   then X-close it and click `About` again.
   - Expected: it reopens at the moved / resized geometry.
6. **Cross-restart geometry persistence.** Same as step 5, but quit
   the whole app and relaunch before reopening `About`.
   - Expected: the moved / resized geometry is restored.

### Cleanup

None required (geometry persistence is expected behaviour). To reset
to the default size, delete the `about_window_geometry` row from the
`setting` table in the session store.

---

## 8. Reports — open, sidebar, geometry, single-session breakdown

Verifies the modeless Reports window, sidebar sort, single-session
breakdown content (SessionStats + session-level per-ore + per-pilot
stack), and geometry / sidebar-width persistence.

### Setup

Have at least 2 saved (Stopped) sessions in the DB. If not, run two
short mining sessions and Stop them.

### Steps

1. **Open modeless.** Click the chrome `Reports` button.
   - Expected: `Reports - EVE Dashboard` window opens. NOT
     modal (MainWindow remains clickable). NOT Topmost (clicking
     MainWindow brings it to the front). NOT transparent. Default
     ~1100x700.
2. **Sidebar shows saved sessions.** Columns: Name, Start,
   Duration, Pilots, Total ISK. Newest session first (descending
   `session_id` default sort).
3. **Column sort.** Click each column header in turn -> rows
   resort; click again -> direction reverses.
4. **Click a row -> single-mode breakdown.** Click row 1 in the
   sidebar.
   - Expected (right pane, top-to-bottom):
     - `SessionStatsView` with the session's stats. Same metrics
       and `Crit ISK %` row as the live widget.
     - `PerOreBreakdownView` titled "Session ore breakdown" with
       columns Type / m^3 / ISK. Sorted by m^3 descending.
     - One stack per pilot: `PerPilotWidgetView` + that pilot's
       `PerOreBreakdownView` titled `<PilotName> ore breakdown`.
5. **Per-ore column sort.** Click `m^3` header -> resort ascending;
   click again -> descending. Same for `ISK` and `Type` headers.
6. **Switch row.** Click row 2 -> right pane updates without
   flicker; per-pilot stack rebuilds with the new session's pilots.
7. **GridSplitter.** Hover the vertical splitter between sidebar
   and content -> resize cursor. Drag ~100 px left -> sidebar
   narrows, content widens live. Drag back to a wider position.
8. **Window resize.** Drag the Reports corner smaller / larger.
   Content reflows; vertical scrollbar appears when content
   exceeds window height.
9. **Geometry + sidebar-width persistence.** Move + resize Reports
   to a non-default position. Drag splitter to a non-default
   width. Close Reports (window hides; app continues). Click
   `Reports` again -> reopens at exactly those bounds and sidebar
   width.
10. **Live mining unaffected by Reports open.** With Reports open,
    Start a new session in MainWindow. Live widgets update; Reports
    data stays frozen (no cross-talk). After Stop, close + reopen
    Reports to see the new session in the sidebar (no live sidebar
    refresh in v1).
11. **Stat-config carries from live to Reports.** Hide `Crit chance`
    via the live SessionStats popup. Switch focus to Reports; click
    row 1 again -> the saved-mode SessionStats also has `Crit chance`
    hidden (shared `session_stats_config` key). Restore `Crit chance`
    -> both come back.
12. **Cross-check totals.** Sum the per-pilot Total ISK across all
    pilot widgets for session 1; compare to the session-level Total
    ISK in SessionStats and the sidebar's Total ISK column.
    - Expected: the three agree exactly. Sum of per-ore ISK matches
      the session-level total within sub-percent rounding (per
      plan-25 R2; per-(input, material) floor accepted).

### Cleanup

Close Reports via its X (window hides; singleton survives). No DB
changes from viewing reports.

---

## 9. Reports — Lifetime Totals window

Verifies the all-time rollup (plan 51). An empty selection now leaves the
Reports content pane BLANK; the rollup lives in its OWN window, opened by a
`Lifetime Totals` button in the Reports chrome strip (next to
`Import Session...`). The window shows total active time tracked, total ISK, and
a `Total Refined Output` table (`Material | Quantity | Total ISK`) summed across
all sessions. It is modeless, hide-on-close, re-fetches the totals on every
open, and persists its size + position.

### Setup

2+ saved (Stopped) sessions in the DB, ideally with refined output and
non-zero ISK in more than one. Note each session's `Total ISK` and
`Duration` from the sidebar columns (you will reconcile against their
sums).

### Steps

1. **Empty selection -> blank pane.** Open Reports with no sidebar checkboxes
   ticked (first open, or untick all first).
   - Expected: the right content pane is BLANK — no auto-shown rollup, no
     placeholder. The chrome strip shows `Import Session...` and
     `Lifetime Totals` buttons.
2. **Button opens the Lifetime Totals window.** Click `Lifetime Totals`.
   - Expected: a SEPARATE window titled `Lifetime Totals` opens (modeless — the
     Reports window stays usable behind it). It shows a `Lifetime Totals` header
     with a separator rule; then `Time Tracked:` + an `HH:mm:ss` value and
     `Total ISK:` + an `N0` value; then a `Total Refined Output` header and a
     table with `Material | Quantity | Total ISK` columns.
3. **Total ISK reconciles to the sidebar sum.** Add up the sidebar's
   `Total ISK` column across every session.
   - Expected: the `Total ISK:` figure equals that sum exactly (each
     session is priced from its own captured rows, then summed — no
     cross-session pricing).
4. **Time Tracked reconciles to the sidebar sum.** Add up the sidebar's
   `Duration` column across every session.
   - Expected: the `Time Tracked:` figure equals that sum. Hours are not
     wrapped at 24 (e.g. a 30-hour total reads `30:00:00`).
5. **Refined table sums across sessions, sorted by quantity.**
   - Expected: one row per refined material, quantity summed across all
     sessions, default sorted by `Quantity` descending. The sum of the
     table's `Total ISK` column reconciles to the `Total ISK:` figure
     (within sub-percent rounding).
6. **Columns sort.** Click `Material`, then `Quantity`, then
   `Total ISK` headers -> rows resort; click again -> direction
   reverses.
7. **Session selection is independent.** With the Lifetime Totals window open,
   tick sessions in the Reports sidebar.
   - Expected: the Reports right pane drives Single / Compare as usual; the
     Lifetime Totals window is unaffected (it keeps showing the all-time rollup).
8. **Close hides; reopen re-shows with current totals.** Close the Lifetime
   Totals window (X), then click `Lifetime Totals` again.
   - Expected: the window re-appears showing the SAME figures (nothing changed
     in the DB between close and reopen).
9. **Refreshes after import.** Import a session (section 12) that adds new data,
   then open (or re-open) the Lifetime Totals window.
   - Expected: the Time / ISK / refined figures grow to include the imported
     session.
10. **Refreshes after delete.** Right-click a session you can spare (Export it to
    JSON first if you want it back) -> Delete -> confirm. Re-open the Lifetime
    Totals window.
    - Expected: the figures shrink to exclude the deleted session.
11. **Text size cog.** In the Lifetime Totals window, click the `A` (text size)
    cog in the widget chrome.
    - Expected: the popup lists `Title` / `Time Tracked` / `Total ISK` /
      `Total Refined Output` / `Table`; raising a level enlarges that element.
      (This is the `lifetime` text-size config, moved here from the pane.)
12. **Geometry persists across restart.** Resize / move the Lifetime Totals
    window, close it, fully close and relaunch the app, open Reports, click
    `Lifetime Totals`.
    - Expected: the window reopens at the size + position you left it.
13. **Empty database (optional).** On a DB with no saved sessions (a
    fresh install or a throwaway store — do NOT delete real sessions for
    this):
    - Expected: the window shows `Time Tracked: 00:00:00`, `Total ISK: 0`,
      and an empty `Total Refined Output` table.

### Cleanup

Close the Lifetime Totals window (hides). Untick all sessions. Close Reports
(window hides). Re-import any session you exported + deleted in step 10 if you
want it back.

---

## 10. Reports — sidebar checkbox-driven compare + loading spinner

Verifies the per-row checkbox column that drives compare-mode
selection, the orbital loading spinner, cancel-in-flight on rapid
clicks, and that row clicks (without checkbox) only set the
right-click highlight.

### Setup

3+ saved sessions in the DB, ideally with overlapping and
non-overlapping pilots / ore types. One LONG session (~6h) is ideal
for spinner / cancel-in-flight verification.

### Steps

1. **Single check -> single mode + spinner.** Tick session A's
   checkbox.
   - Expected: checkbox flips IMMEDIATELY (no input lag); within a
     render frame the right pane shows the spinning loading wheel
     centered; widgets do NOT appear yet.
2. **Spinner replaced by widgets.** Wait for the load.
   - Expected: spinner disappears; brief empty-pane moment (100ms-2s
     on small sessions; longer on multi-session compares) BEFORE the
     widgets render. This is by design -- spinner is hidden BEFORE
     the widget render pass starts so the user does not see a frozen
     spinner during the render.
3. **Row click does not change compare data.** With A checked,
   click on session C's row BODY (not its checkbox) -> C gets the
   highlight but the right pane is unchanged.
4. **Right-click acts on the right-clicked row.** Right-click C ->
   Export to JSON -> file-save dialog default name is C's slug
   (NOT A's). Cancel. Right-click C -> Delete... -> confirm-dialog
   body names C. Cancel.
5. **Add to compare.** Tick session B's checkbox.
   - Expected: checkbox B flips immediately; spinner appears
     (replacing single-mode); eventually the compare widget renders
     (see section 11 for compare-widget shape).
6. **Rapid clicks (cancel-in-flight).** Untick everything. Within
   ~200 ms, tick A, then B, then C (three rapid clicks).
   - Expected: each checkbox flips immediately; spinner stays
     visible continuously (does NOT flash off / on between loads);
     intermediate states do NOT render; final view is the 3-session
     compare. Total wait roughly ONE load duration, not three.
7. **Cancel-in-flight on long sessions.** Tick the long session
   ONLY -> spinner starts. While it spins, tick a different short
   session.
   - Expected: the long load aborts immediately; right pane renders
     the new state within the SHORT session's load duration.
8. **Mash 50 clicks does not freeze.** Uncheck everything. Mash 30+
   rapid checkbox toggles.
   - Expected: every click stays responsive; spinner stays
     animating; within ~3s of the LAST click the spinner clears and
     the final-state view renders. No freeze, no crash.
9. **Spinner animation actually orbits.** The mining barge orbits
   the asteroid clockwise, one full loop every 4 seconds. Lasers
   stay pointed inward at every position. Smooth animation through
   the entire load.
10. **Spinner stays smooth during render pass.** Trigger a heavy
    compare (5-session ideal). Watch the spinner -- it either spins
    smoothly OR is gone; never frozen mid-spin. The empty-pane gap
    between spinner-hide and widget-render is expected.
11. **Spinner hides when not visible.** Uncheck everything. The right
    pane is BLANK (plan 51 — Lifetime Totals is now a separate window
    opened from the Reports chrome button, section 9); spinner subtree is
    Collapsed; CPU% idle.
12. **Reopen Reports preserves checks AND skips spinner.** With
    (A, B) checked, X-close Reports. Click Reports again.
    - Expected: A + B still checked; compare view appears
      IMMEDIATELY (no spinner -- no new load triggered).
13. **Delete during compare.** With A + B compare rendered,
    right-click A -> Delete -> confirm -> spinner reappears briefly
    while the sidebar reloads; post-delete view = B-only single
    mode (or empty if B was the only survivor).
14. **PNGs render correctly.** Spinner shows BOTH the centred
    asteroid AND the orbiting barge with lasers and blue thrusters.
    NOT a broken-image icon, NOT a dark rectangle around either
    image (PNGs are truly transparent).
15. **Optional memory check (rapid clicks do not leak).** With a
    .NET profiler attached, snapshot heap; mash 30+ clicks; settle;
    untick everything; force GC; snapshot again. Expect ZERO
    instances of `SavedSessionStatsViewModel`,
    `CompareSessionViewModel`, `SavedPerPilotWidgetViewModel` left
    alive (cancelled loads dispose their transient VMs in the
    Task.Run catch block).

### Cleanup

Untick all sessions. Close Reports.

---

## 11. Reports — compare-mode widget

Verifies the single decluttered compare widget: stacked session-name
subheader, the plan-45 `TOTAL TIME` / `TOTAL ISK` labels and the Total
Refined Output table (`Material | Quantity | Total ISK`), a 4-row
Cumulative table (`Stat | Avg | Total`) ending in `Crit ISK %`, and a
2-row Rates table (`Stat | Avg`) using the corrected
`sum(total) / sum(time)` math.

### Setup

2+ saved sessions in the DB, ideally with non-zero crits in at least
one. Per-session totals noted in advance (open each in single mode,
read off `Total ISK`, `Total time`, `Total m^3`, `Total ISK from
crits`).

### Steps

1. **Two-session compare.** Tick sessions A + B in the sidebar.
   - Expected: right pane shows ONE compare widget, top to bottom:
     ```
     Comparing sessions:
         <session 1 name>
         <session 2 name>

     TOTAL TIME:  <hh:mm:ss>
     TOTAL ISK:   <n0>

     Total Refined Output
         <Material | Quantity | Total ISK table>

     Cumulative
         <Stat | Avg | Total: ISK, Time, m^3, Crit ISK %>

     Rates
         <Stat | Avg: m^3/sec, ISK/h>
     ```
     No per-pilot widgets, no per-ore tables, no per-session columns,
     no Diff column, no em-dash cells.
2. **Grand-total labels (plan 45).** The `TOTAL TIME` label equals
   the Cumulative `Time` row's `Total` cell, and `TOTAL ISK` equals
   the Cumulative `ISK` row's `Total` cell. The labels and the table
   intentionally show the same two numbers (decision 1.2 = A).
3. **Total Refined Output table (plan 45).** Three columns
   `Material | Quantity | Total ISK`; one row per refined material,
   summed across both sessions, default-sorted by Quantity
   descending. Click each header -> rows re-sort. Summing the
   `Total ISK` column equals the `TOTAL ISK` label (within rounding).
   A material with no captured price shows its quantity with `0` in
   Total ISK.
4. **Cumulative ISK math.** `Total` cell of `ISK` row =
   `s1.TotalIskMined + s2.TotalIskMined` (sum). `Avg` cell = that
   divided by 2.
5. **Cumulative Time math.** `Total` = sum of per-session active
   durations, formatted `HH:mm:ss`. `Avg` = `Total / N`, same
   format. Hours can exceed 23 (no overflow truncation).
6. **Cumulative Crit ISK % math.** The Avg AND Total cells render
   the SAME value: union-wide
   `sum(TotalIskFromCrits) / sum(TotalIskMined)` formatted as P1.
   A percentage is not summable; per-session means would be
   misleading.
7. **Rate m^3/sec math (corrected).** Avg =
   `sum(m^3) / sum(active_seconds)` over the union of selected
   sessions -- NOT the mean of per-session m^3/sec. Verify against
   a hand-computed value from the per-session totals you noted in
   Setup.
8. **Rate ISK/h math (corrected).** Avg = `sum(ISK) /
   (sum(active_seconds) / 3600)`. Same hand-verification.
9. **3-session compare.** Tick a third session.
   - Expected: subheader lists all three session names (one per
     indented line). Cumulative still has 4 rows; Rates still has
     2; the Total Refined Output table now sums across all three.
     NO extra columns; NO per-session expansion.
10. **5-session compare.** Tick five sessions.
    - Expected: same shape; subheader lists all five names.
11. **Empty selection blanks the pane.** Untick everything ->
    the right pane is BLANK; Lifetime Totals is now a separate window
    opened from the Reports chrome button (plan 51, section 9), no spinner.
12. **Switch back to single.** Tick exactly one session -> single
    mode returns (per section 8); compare widget gone.

### Cleanup

Untick all. Close Reports.

---

## 12. Reports — import / export / delete

Verifies JSON round-trip, Excel export (Summary tab + per-table
tabs + per-pilot tabs + role column + refined-output tables),
schema-version refusal, pilot-rename collision warning,
bogus-file rejection, and the delete flow with the cancel branch.

### Setup

At least one finished session in the DB. Pick a working folder for
artifacts, e.g. `D:\tmp\plan29\`. Open Reports.

### Steps

1. **JSON export.** Right-click a sidebar row -> `Export to
   JSON...`. Save to the working folder with the default
   filename. Open in a text editor. Expected:
   - `_manifest` block with `"format": "EveDashboardSessionExport"`,
     `"schema_version": 1`, `"app_version"`, `"exported_at"`,
     `"session_id"`, `"session_name"`.
   - Top-level keys: `session`, `session_time`, `pilot`,
     `session_pilot`, `log`, `market_stations`, `market_data`.
   - `log` entries have a `role` column. `session_pilot` entries
     have a `role` column.
   - Reports status line reads `Exported: <session-name>`.
2. **Excel export.** Right-click -> `Export to Excel...`. Save +
   open. Expected:
   - `Summary` is the FIRST tab. Cells: Name, Notes, Start UTC,
     Stop UTC, Active duration, Paused duration, Pilot count,
     Total m^3, Total ISK. Then a Per pilot block (with Role
     column), a Per ore block, and a `Total Refined Output
     (session)` block (`Material | Quantity | Total ISK`,
     summed across pilots, sorted by quantity descending).
   - One tab per DB table: `session`, `session_time`, `pilot`,
     `session_pilot`, `log` (has `role` column 9),
     `market_stations`, `market_data`.
   - One tab per pilot named `Pilot - <pilot name>` (truncated to
     31 chars). Pilot names are clean (NO `[Role]` suffix in any
     cell or sheet name). Each pilot tab has its ore breakdown
     table, then a `Refined output` table
     (`type_id | type_name | quantity | isk`).
   - Reconciliation: the Summary `Total Refined Output` ISK column
     sums to the Summary `Total ISK` cell; each pilot tab's refined
     `isk` column sums to that pilot's ISK in the Summary Per pilot
     block (same ISK pipeline).
3. **JSON round-trip import.** Click `Import Session...` in the
   Reports header -> pick the JSON from step 1. Expected:
   - Sidebar refreshes; newly imported session at the top.
   - Status line: `Imported: <session-name>`.
   - Whatever was selected before the import is still selected.
   - Re-export the import to a second JSON and diff -- they should
     differ only in autoincrement IDs and `_manifest.exported_at`.
4. **Schema-version refusal.** Edit the JSON's
   `_manifest.schema_version` from `1` to `99`. Import.
   - Expected status: `Import failed: schema_version mismatch
     (file=99, current=1)`. Sidebar unchanged.
5. **Pilot rename collision.** Edit a `pilot` entry's `name` to
   add `_RENAMED`. Import.
   - Expected: import SUCCEEDS; status `Imported: ...`. Sidebar
     refreshes; imported session shows pilots with their ORIGINAL
     names (the rename is discarded). App log has a Warning:
     `Pilot {PilotId} import name '<RenamedName>' differs from
     existing '<OriginalName>'; keeping existing.`
6. **Pre-plan-35 JSON reimport.** If you have a pre-plan-35 JSON
   file (no `role` field in `log` / `session_pilot`), import it.
   - Expected: import succeeds (schema_version is still 1, no
     mismatch). Every pilot in the imported session has
     role = Miner (the JsonConverter defaulted missing roles to
     "Miner"). All totals look correct (every row is Miner; no
     filter).
7. **Bogus file rejection.** Create `bogus.json` with
   `{"some_other_format": true}`. Import.
   - Expected status: `Import failed: File _manifest.format is
     '<missing>'; expected 'EveDashboardSessionExport'.` Sidebar
     unchanged.
8. **Delete -- cancel branch.** Right-click a row -> `Delete...`
   (under the Separator below the two Export options). Themed
   ConfirmDialog titled `Delete session?` with body `Are you sure
   you want to delete '<session-name>'? It cannot be undone.`
   Buttons: Cancel / Delete. Click Cancel.
   - Expected: dialog closes; sidebar unchanged; status line
     unchanged.
9. **Delete -- confirm branch.** Right-click a test session ->
   `Delete...` -> click `Delete` in the dialog.
   - Expected: sidebar refreshes; deleted session is gone. Status:
     `Deleted: <session-name>`. App log Information:
     `Deleted session #<id>: 1 session + N session_time + N
     session_pilot + N log row(s).`
   - DB cross-check: `SELECT COUNT(*) FROM session WHERE
     session_id = <id>` returns 0; same for `session_time`,
     `session_pilot`, `log`. `pilot`, `market_data`, and
     `market_stations` row counts are UNCHANGED (the deleter only
     touches direct `session_id` FK rows).

### Cleanup

Delete artifact files from the working folder. If duplicate
imports left junk in the DB and you don't want to keep them, the
canonical reset is to delete `%APPDATA%\EveDashboard\
session.sqlite` between rounds.

---

## 12.5 Reports — resizable columns + per-element text sizing (plan 47)

Verifies the restored DataGrid column-resize grippers with per-grid-type
width persistence (phase 1a), and the second "text size" chrome cog that
sizes each Reports table widget's elements per emphasis level (phase 1b).

### Setup

- At least 3 saved sessions, at least one with 2+ pilots and refined output
  (so the per-pilot ore/refined tables and the "Total Refined Output" table
  render). Open Reports (chrome-bar Reports button).

### 12.5.1 Column resize + persistence (phase 1a)

1. **All column text is left-aligned.** Glance at every Reports table (ore,
   refined, compare, sidebar) plus the refined table in the Lifetime Totals
   window (plan 51 — opened from the Reports chrome button).
   - Expected: every cell's text — including the numeric columns (m³, ISK,
     Quantity, Total ISK) — is left-aligned, matching the left-aligned headers.
2. **Grippers exist.** Hover the boundary between two column headers in the
   sidebar "Saved sessions" grid. Cursor becomes a horizontal resize arrow.
   - Expected: every Reports DataGrid (sidebar, ore, refined, compare tables,
     plus the Lifetime Totals window's refined table) shows the resize cursor at
     header edges. NO horizontal scrollbar ever appears.
3. **Resize.** Drag a sidebar column edge (e.g. "Name") wider, then narrower.
   - Expected: the column resizes live; other columns reflow; no scrollbar.
4. **Persist across reopen.** Note the new width. Close Reports (the window
   hides). Reopen Reports.
   - Expected: the sidebar column keeps the width you set.
5. **Persist across restart.** Select one session so "Session ore breakdown"
   shows; drag its "Type" column. Close the whole app. Relaunch, open Reports,
   reselect that session.
   - Expected: the ore "Type" column keeps the width you set.
6. **Per-type scope.** With a 2+ pilot session selected, resize the session ore
   table's "Type" column. Reopen Reports and reselect.
   - Expected: the per-pilot ore tables share the same saved "Type" width (width
     is per grid TYPE, not per instance).
7. **Star columns still stretch.** Widen the content pane WITHOUT having resized
   a given table's `*` column.
   - Expected: the untouched stretchy column (e.g. ore "Type") keeps filling the
     width; only columns you explicitly dragged stay frozen at a fixed size.

### 12.5.2 Text size cog (phase 1b)

The cog uses the SAME 5 emphasis levels as the live SessionStats / per-pilot cog
(None / Header0 / Header1 / Header2 / Hero), and applies the FULL look — size +
weight + accent — to labels (the tables size only). Each element starts at a
sensible DEFAULT (Layout 1): every heading / total / section label defaults to
Header0 (Bold 14), every table + data row defaults to None (plain).

1. **Cog appears on TABLE widgets only.** Select one session.
   - Expected: the ore / refined TABLE widgets each show an "A" (text size) cog
     plus the `x` close button. The SessionStats and per-pilot STATS widgets
     keep their existing gear (stat-config) cog and do NOT gain an "A" cog.
2. **Default look matches today.** Before touching anything, eyeball the ore /
   refined / compare widgets (and the lifetime widget in the Lifetime Totals
   window, plan 51 — opened from the Reports chrome button).
   - Expected: the same look as before plan 47 — the titles + "TOTAL TIME/ISK" +
     "Lifetime Totals" + the section labels render Bold ~14; the data rows + table
     cells render plain. (Internally those headings now default to Header0 and the
     data to None, but the on-screen result is today's look.)
3. **Re-size a label, with live preview.** Click the "A" cog on the ore widget.
   The "Text size" popup lists "Title" (shown bold = its Header0 default) and
   "Table". Change "Title" to Hero in the dropdown.
   - Expected: the popup's "Title" preview row immediately grows to 22pt in the
     ACCENT colour (same preview behaviour as the live stat cog). Apply -> the ore
     widget's title line renders 22pt accent; the table is unchanged; popup closes.
4. **Re-size a table (size only).** Reopen the "A" cog, set "Table" to Header2, Apply.
   - Expected: all cells + headers of the ore table grow to 20pt (size only — no
     bold/accent on table cells, per the table rule).
5. **Reset to default.** Open the cog, set "Title" back to Header0 (its default), Apply.
   - Expected: the title returns to today's Bold 14 look (an element re-picked to
     its default is stored as "no override").
6. **Persistence.** Close + reopen Reports, reselect the session -> choices kept.
   Restart the app and reselect -> still kept.
7. **Per-type scope.** With a 2+ pilot session, set the session ore widget's
   "Table" to Header1.
   - Expected: every ore table (session + per-pilot) renders at Header1 — text
     sizing is per widget TYPE.
8. **Compare + lifetime.** Check 2+ sessions (compare mode): the compare "A" cog
   lists Header / TOTAL TIME / TOTAL ISK / the three section labels + tables; set a
   few to bigger levels and Apply -> those labels take the full size+weight (+accent
   on Hero), the tables size only. Open the Lifetime Totals window (plan 51 — the
   Reports chrome button, section 9): its "A" cog lists Title / Time Tracked /
   Total ISK / Total Refined Output / Table; verify the same.

### Cleanup

- Reset any element via each widget's "A" cog (pick its default — Header0 for
  headings, None for tables/data — and Apply) and drag columns back to taste. OR,
  to clear the saved state wholesale, delete `%APPDATA%\EveDashboard\session.sqlite`
  (drops the `reports_column_widths.*` and `reports_text_size.*` settings rows —
  only if you don't mind losing saved sessions).

---

## 12. SDE updater against live everef.net (advanced, network)

Verifies the real-network code path against `data.everef.net`. The
matching xUnit test ships with `[Fact(Skip = "...")]`; this script
is the toggle-and-run procedure.

### Setup

Working tree clean (or only the SDE-related changes you are
verifying). `dotnet build` and `dotnet test` exit clean. Internet
available; no corporate proxy intercepting `data.everef.net`.
Backup the runtime SDE if you want to roll back:
```powershell
Copy-Item "$env:APPDATA\EveDashboard\sde\eve.sqlite" eve.sqlite.bak
```

### Steps

1. Open `tests/EveDashboard.Sde.Tests/Updater/EverefSdeUpdaterRealNetworkTests.cs`.
2. Comment the `[Fact(Skip = "...")]` line and uncomment the
   `//[Fact]` line so the test is un-skipped. Save.
3. Run only this test:
   ```
   dotnet test tests/EveDashboard.Sde.Tests --filter "FullyQualifiedName~EverefSdeUpdaterRealNetworkTests"
   ```
4. Restore the file (re-comment `[Fact]`, un-comment
   `[Fact(Skip = ...)]`). Save. Confirm:
   ```
   dotnet build EveDashboard.sln
   dotnet test EveDashboard.sln --filter "FullyQualifiedName~EverefSdeUpdaterRealNetworkTests"
   ```
   Expected: 0 active + 1 skipped.
5. Un-skip again and run -- second run should hit the
   HEAD-tags-match-local fast path because the first run wrote
   `last_modified` / `etag` to the test's per-run `meta` row.

### Expected results

- **First run:** `SdeUpdateResult.Updated(buildTime: <recent>)` or
  `NoUpdateNeeded` if cache tags happen to already match. Either
  is acceptable. On `Updated`: `meta.build_time` matches everef's
  snapshot; `last_modified` / `etag` populated; `types` row count
  > 10,000.
- **Second run:** `SdeUpdateResult.NoUpdateNeeded`. No `Error`-level
  log lines. No orphan folders under `<test-temp>/tmp/`.
- **Cross-check (optional):** fetch `meta.json` directly and
  compare `build_time` to the test DB's `meta.build_time`.

### Cleanup

Skip attribute restored (verified by step 4 above). `git diff` on
the test file is empty. The test creates its own temp directory
under `%TEMP%\evemining-sde-realnet-<guid>\`; teardown attempts
recursive delete -- remove manually if locked. Optional: restore
the `eve.sqlite.bak` you took in Setup.

---

## 13. Session-bound pricing + sha256 migration (plan 38)

Verifies that a saved session's ISK is permanently reproducible: prices are
bound to the session that fetched them and read **station-blind** (the selected
station only controls where prices are fetched, never how a saved session is
valued), Reports never call the market API, the session-start fetch is
unconditional, re-import of a known session is a no-op, legacy (pre-plan-38,
integer-id) export files still import, and a pre-plan-38 database upgrades
itself on first launch.

### Setup

- For step 1 (migration) you need a **pre-plan-38 `session.sqlite`** — a copy
  taken before this build (integer session ids, `market_data` with no
  `session_id`). Keep a copy somewhere safe; you will place it at
  `%APPDATA%\EveDashboard\session.sqlite` before launching.
- For steps 2-5, a normal post-migration database (any sessions you have mined
  on this build).
- EVE running with at least one mining character for the live-session steps.

### Steps

1. **One-time migration on launch.** Close the app. Replace
   `%APPDATA%\EveDashboard\session.sqlite` with your pre-plan-38 copy
   (delete any `-wal` / `-shm` siblings). Launch the app.
   - Expected: a message box titled **"Updating local database"** appears once;
     after you click OK the app opens normally. A backup file
     `session.sqlite.pre-sha256-<timestamp>.bak` now sits next to the DB. Open
     Reports — every session from before is still listed, newest first by start
     time. Relaunching the app does NOT show the message box again.
2. **ISK is stable across viewings (Bug 2).** Open Reports, pick a finished
   session, note its **Total ISK**. Close Reports. Start and fully finish a NEW
   mining session (so fresh prices are fetched). Re-open Reports and select the
   FIRST session again.
   - Expected: its Total ISK is **identical** to what you noted — a later
     session's prices did not change it.
3. **Reports never fetch.** Disconnect from the network (or block the app's
   internet). Open Reports and select any saved session.
   - Expected: the report renders with its captured ISK with no hang and no
     change; the latest `logs\<date>.log` shows **no** GoonMetrics request while
     the report was open.
4. **Start fetch is unconditional.** Reconnect. Finish a session, then within a
   few seconds Start another session and mine.
   - Expected: ISK values populate on the first ticks (prices were fetched at
     start even though the previous session fetched the same items seconds
     ago — the throttle did not skip the start fetch).
5. **Re-import dedup.** In Reports, right-click a session -> Export to JSON.
   Then Import that same file twice in a row.
   - Expected: the first import adds the session (or reports it already present
     if it is the one you exported); the second import shows a status like
     **"Already in your database: <name>"** and the sidebar gains **no**
     duplicate row.
6. **ISK is station-blind (Bug 3).** Open Reports, pick a finished session, note
   its **Total ISK**. Open Settings, change the **Selected station** (e.g. Jita
   -> C-J6MT), Apply. Reopen Reports and select the same session.
   - Expected: its Total ISK is **identical** — the selected station controls
     only where new prices are fetched, never how a saved session is valued. If
     you have older sessions whose prices were captured under a different
     station than the one now selected, they still show their real ISK (not 0 /
     "no market data").
7. **Legacy JSON import (Bug 4).** In Reports, Import one of your **older**
   export files (a pre-plan-38 export with integer session ids — e.g. a file
   from `D:\user_files\Documents\EVE\EVEDashboard\`).
   - Expected: the import succeeds (no "file is not a recognisable session
     export" error); the session appears in the list, ordered by its original
     mining date; opening its report shows ISK computed from the imported
     prices.

### Cleanup

- Delete the `session.sqlite.pre-sha256-*.bak` file(s) created in step 1 once
  you have confirmed the migration (or keep one as your own safety copy).
- Delete any throwaway sessions created in steps 2 / 4 via Reports ->
  right-click -> Delete.
- Delete any sessions imported in steps 5 / 7 (unless you want to keep them).
- Restore your preferred **Selected station** in Settings if you changed it in
  step 6.
- Re-enable the network if you disabled it in step 3.

---

## 14. Data-folder rename migration (plan 39)

Verifies the one-time move of the per-user data folder from the old
`%APPDATA%\EveMiningDashboard` to `%APPDATA%\EveDashboard`, so an existing user
keeps all sessions / config / SDE cache / logs after the EveDashboard rename.

### Setup

- A profile with data at the OLD path `%APPDATA%\EveMiningDashboard\`
  (`session.sqlite`, `config.yaml`, `sde\`, `logs\`) and NO
  `%APPDATA%\EveDashboard\` folder yet. If you are already on the renamed build,
  copy a pre-rename backup into `%APPDATA%\EveMiningDashboard\` and delete
  `%APPDATA%\EveDashboard\` to recreate this precondition.

### Steps

1. **One-time move on launch.** Confirm `%APPDATA%\EveMiningDashboard\` exists and
   `%APPDATA%\EveDashboard\` does not. Launch the app.
   - Expected: the app opens normally; `%APPDATA%\EveDashboard\` now exists and
     holds your `session.sqlite` (+ `-wal`/`-shm`), `config.yaml`, `sde\`, and
     `logs\`; the old `%APPDATA%\EveMiningDashboard\` folder is gone. Reports
     lists all prior sessions; a session start/stop works.
2. **Idempotent on relaunch.** Close and relaunch.
   - Expected: no move happens (the new folder already exists); the app opens
     normally, no error, no data change.
3. **Fresh install (no legacy folder).** Delete both
   `%APPDATA%\EveMiningDashboard\` and `%APPDATA%\EveDashboard\`. Launch.
   - Expected: the app bootstraps a fresh `%APPDATA%\EveDashboard\` (autodetect
     gamelog path, new DB) with no migration and no error.

### Cleanup

- None required. Keep a backup of your real data folder if you recreated the
  precondition by hand.

---

## 15. Update check on launch (plan 41)

Verifies the launch-time GitHub release check: a newer published release prompts a
modal Yes/No dialog that opens the download page, the prompt is non-blocking and
re-appears every launch until updated, and a failure (offline) is silent.

### Setup

- Build + run from source per Shared setup. The app version comes from `<Version>`
  in `Directory.Build.props` (currently `0.1.0`, matching the published release
  tag `v0.1.0`).

### Steps

1. **Newer release available.** Edit `Directory.Build.props` and set `<Version>`
   BELOW the latest published release (e.g. `0.0.1`). Rebuild and launch
   (`dotnet run --project src/EveDashboard.App.Wpf`).
   - Expected: shortly after the main window appears, a modal dialog titled
     "Update available" names the latest release (e.g. `v0.1.0`) and shows two
     buttons, **Yes** and **No** (not "Cancel"). The main window finished showing
     first - the check did not block startup.
2. **Yes opens the download page.** Click **Yes**.
   - Expected: your default browser opens the release page
     (`https://github.com/wolfthefallen/EVEDashboard/releases/tag/v0.1.0`); the
     app keeps running.
3. **Nag persists.** Close and relaunch (still on the low `<Version>`).
   - Expected: the "Update available" dialog appears again - there is no
     "don't remind me" and no persisted dismissal.
4. **No opens nothing.** Relaunch; click **No**.
   - Expected: the dialog closes, no browser opens, the app runs normally.
5. **Up to date - no prompt.** Restore `<Version>` to match (or exceed) the latest
   release tag (`0.1.0`). Rebuild and launch.
   - Expected: no dialog. The log shows an Information line that the running
     version is current with the latest release.
6. **Offline - silent.** Disconnect the network (or block api.github.com). Launch.
   - Expected: no dialog, the app starts normally, and a single Warning is logged
     to `%APPDATA%\EveDashboard\logs\<latest>.log` that the update check was
     skipped. Startup is not delayed.

### Cleanup

- Restore `<Version>` in `Directory.Build.props` to `0.1.0` (or the real current
  version) and rebuild.
- Re-enable the network if you disabled it in step 6.

---

## 16. Refined output (plan 43)

Verifies the live "Refined output" section in the Session + per-pilot widgets
(quantities of refined materials, tracked as they are mined), the Settings
auto-show toggle, the sticky per-material hide in the stat-config cog, and the
Reports refined-output breakdown.

### Setup

A working SDE + market prices (so ISK computes). Start a session with at least
one pilot and mine some ore in EVE whose reprocess outputs you know (e.g.
Veldspar -> Tritanium; or Kylixium -> Tritanium / Pyerite / Mexallon).

### Steps

1. **Live refined output appears.** With auto-show ON (Settings -> Refined
   output -> "Auto-show refined materials as they are mined" checked, the
   default), mine ore and Continue the session.
   - Expected: a "Refined output" section appears under the stats in the Session
     widget AND under each mining pilot's widget, listing each refined material
     (name + quantity) as it is first produced. Quantities grow as you mine; the
     numbers are yield-applied (reflect your refining-yield setting). The
     Session-widget list is the sum across pilots; each pilot widget shows that
     pilot's own.
2. **Auto-show OFF = manual only.** Open Settings, uncheck "Auto-show refined
   materials...", Apply. Mine an ore that produces a material NOT yet shown.
   - Expected: the new material does NOT appear in any "Refined output" section.
3. **Reveal a hidden material via the cog.** Click a widget's stat-config cog;
   scroll to the "Refined materials" subsection; check the box next to the
   hidden material; Apply.
   - Expected: that material now shows in this widget's "Refined output" section.
     (Per-widget: revealing it on one pilot does not reveal it on another —
     decision 2.2 = A.)
4. **Sticky hide survives auto-show ON.** With a material currently visible,
   open the cog, UNCHECK that material, Apply -> it disappears from the section.
   Now re-enable auto-show in Settings (check the box, Apply).
   - Expected: the explicitly-hidden material STAYS hidden even though auto-show
     is on (sticky hide wins). Other materials auto-show as normal.
5. **Reporting shows ALL materials.** Stop the session. Open Reports, select the
   session.
   - Expected: a "Session refined output" breakdown (Material | Quantity,
     sortable) lists EVERY refined material for the session, plus a per-pilot
     "refined output" breakdown under each pilot — regardless of anything you
     hid live (decision 1.3 = A). Quantities match the live totals.
6. **A new session clears the refined output (bug 1 / issue #2).** After a
   session that produced refined output (above), Stop it, then start a brand-new
   session (New Session -> Start) and Continue it.
   - Expected: at the moment the new session starts, BOTH the Session widget's
     and every per-pilot widget's "Refined output" section is EMPTY — the prior
     session's materials do not carry over. As you mine in the new session, only
     the new session's materials appear and their quantities reflect only this
     session (not added on top of the previous one).
7. **Pilot-widget refined output can be disabled (issue #1).** With live refined
   output showing (above), open Settings -> Refined output -> UNCHECK "Show
   refined output on pilot widgets" -> Apply.
   - Expected: every per-pilot widget's "Refined output" section disappears
     immediately (no restart); the SESSION widget's section is unchanged. Re-check
     the box + Apply -> the per-pilot sections come back. Default (fresh install)
     is checked/on.

### Cleanup

Re-check "Auto-show refined materials..." in Settings (Apply) if you want the
default. Stop the session. To fully reset the per-widget hides, delete the
`refined_materials_visibility*` rows from the `setting` table (optional).

---

## 17. Sustained-session stability (plan 44 — DB thread-safety + no silent failures)

This is the regression check for BUGS.md Bug 1: a shared SQLite connection used
concurrently by the live consumer and the UI stat-refresh corrupted the driver,
threw a non-`DbException`, and silently killed the mining-log insert pipeline —
the live stats froze (with a ~5-minute auto-pause/resume flap) while the app kept
running. The fix serialises the connection, makes transactions atomic, and makes
the consumer un-killable.

### Setup

- A real EVE client (ideally a multi-character fleet — the heavier the write
  load, the better this exercises the old race), mining live.
- The dashboard built from this branch. Have `tools/sqlite3.exe` handy.

### Step-by-step actions

1. Start a session with several actively-mining pilots and mine for **30+ minutes**
   without interruption.
   - Expected: the live stats (ISK, m3, per-pilot, refined output) advance the
     **whole time** — no multi-minute freeze, no "stats stuck while the clock
     ticks", no repeating auto-pause/auto-resume every ~5 minutes while you are
     actively mining.
2. Mid-session, confirm capture is keeping pace:
   `& "tools\sqlite3.exe" "file:$env:APPDATA\EveDashboard\session.sqlite?mode=ro" "SELECT COUNT(*), MAX(eve_timestamp) FROM log WHERE session_id='<id>';"`
   - Expected: the row count climbs and `MAX(eve_timestamp)` stays within a few
     seconds of the newest `(mining)` line in the gamelogs.
3. While the session is still Running, open **Reports** and **Import** a session
   JSON, then **Delete** a saved session.
   - Expected: both complete; the live session keeps capturing throughout; the
     app does not crash.
4. Open **New Session** a few times and change the pilot **sort** dropdown each time.
   - Expected: no crash; the chosen sort order is remembered on reopen.

### Expected results

No freeze, no silent stall, no crash. `%APPDATA%\EveDashboard\logs\EveDashboard.log`
shows continuous activity with no unhandled-exception traces and no repeating
5-minute idle auto-pause while you are actively mining.

### Cleanup

Stop the session. Delete any test session you imported (Reports -> right-click ->
Delete). No other cleanup needed.

---

## Reporting a failure

If any scenario fails, capture:

- Which section + step number.
- What you saw vs what was expected.
- Whether the issue is consistent or intermittent.
- Any relevant log lines from
  `%APPDATA%\EveDashboard\logs\<latest>.log`.

The implementer fixes the issue, re-runs the relevant section, and
asks for a re-check.
