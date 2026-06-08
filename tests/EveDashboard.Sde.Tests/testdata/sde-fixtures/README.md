# SDE test fixtures

Synthetic JSON files mirroring the everef.net `reference-data-latest.tar.xz` layout. Hand-crafted, small, and deliberate — each row exercises a specific code path in `EverefSdeFormatConverter`. Real EVE classifications are sometimes simplified (e.g. Hydrocarbons is in group 18 here even though real EVE puts moon materials in their own groups) — the converter doesn't care, only schema integrity matters.

## What each row exercises

### `meta.json`

| Field | Value | Why |
|---|---|---|
| `build_time` | `2026-04-30T12:00:00Z` | Fixed-time anchor for tests |
| `esi.sha256` / `sde.sha256` / `hoboleaks.sha256` | distinct test strings | Verifies all three pass through round-trip |

### `categories.json`

| `category_id` | `published` | Why |
|---|---|---|
| `0` | false | **Filter test** — must NOT appear in DB after import |
| `2` | true | Holds the gas group (711) |
| `4` | true | Holds the minerals group (18) |
| `25` | true | Holds the ore groups (462 Veldspar, 1884 Bitumens) |

### `groups.json`

| `group_id` | `category_id` | `published` | Why |
|---|---|---|---|
| `0` | 0 | false | **Filter test** — must not appear |
| `18` | 4 | true | Holds minerals: Tritanium, Pyerite, Mexallon, Hydrocarbons |
| `462` | 25 | true | Holds Veldspar (the ore) |
| `711` | 2 | true | Holds Fullerite-C28 (the gas) |
| `1884` | 25 | true | Holds Bitumens (a moon ore) |

### `types.json`

Nine entries; covers nine distinct converter behaviours:

| `type_id` | name | `published` | `is_ore` | Has `type_materials`? | Has `icon_id`? | Why |
|---|---|---|---|---|---|---|
| `0` | `#System` | false | false | no | no | **Filter test** — must NOT appear in DB |
| `34` | Tritanium | true | false | no | yes (100) | Mineral (refining output target); icon present; **`type_materials` field omitted entirely** — converter must treat as no materials |
| `35` | Pyerite | true | false | no | yes (101) | Mineral; explicit empty `type_materials: {}` |
| `36` | Mexallon | true | false | no | yes (102) | Mineral |
| `1230` | Veldspar | true | true | yes (1 entry → Tritanium) | yes (1234) | Asteroid ore — exercises the simplest type_materials case (1 entry) |
| `16633` | Hydrocarbons | true | false | no | yes (103) | Moon-material refining target (referenced by Bitumens) |
| `25268` | Fullerite-C28 | true | false | no | yes (5678) | Gas — `is_ore = false`; covers the cat-2/group-711 path |
| `45492` | Bitumens | true | true | yes (3 entries → Pyerite, Mexallon, Hydrocarbons) | yes (21792) | Moon ore — exercises multi-entry type_materials with a non-mineral output |
| `99999` | `Café Mocha (test)` | true | false | no | **no** (absent) | **Unicode test** for the `name` round-trip; **null `icon_id`** test (field omitted) |

## After import (expected counts in the SQLite)

Assuming all `published = true` rows pass through:

- `meta`: 1 row
- `categories`: 3 (filtered: 0)
- `groups`: 4 (filtered: 0)
- `types`: 8 (filtered: 0)
- `type_materials`: 4 (1 from Veldspar + 3 from Bitumens)

Tests assert these counts and the per-row content.

## Files we deliberately do NOT include

The everef.net archive has 19 files. Plan 06's converter only requires `meta.json`, `categories.json`, `groups.json`, `types.json`. The other 15 files are absent from this fixture folder — confirms the converter doesn't accidentally try to read them.
