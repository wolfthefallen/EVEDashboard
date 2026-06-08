# EVE Dashboard — SDE Schema

The shape of the SQLite database that holds EVE Static Data Export information at runtime. Canonical reference for any code that reads, writes, or migrates the SDE store.

**Source:** [everef.net reference data](https://data.everef.net/reference-data/) — `reference-data-latest.tar.xz`. JSON files inside the archive are converted to this SQLite shape by the format converter.

**Why this shape:** the dashboard only persists what it reads or computes against. Anything not used today is dropped and brought back per-feature when needed. See the full skip list at the bottom.

---

## Visual diagram

```
┌─────────────────────────────────┐         meta is single-row (id always = 1).
│ meta                            │         No FK relationships. Carries the
│─────────────────────────────────│         everef build context — useful for
│ id              INTEGER  PK     │         "SDE updated 3h ago" UI later, and
│ build_time      TEXT  NOT NULL  │         the 3 SHAs document the upstream
│ esi_sha256      TEXT            │         state for debugging "did anything
│ sde_sha256      TEXT            │         actually change?".
│ hoboleaks_sha256 TEXT           │
│ last_modified   TEXT            │         last_modified / etag are populated
│ etag            TEXT            │         by the updater from the HTTP
└─────────────────────────────────┘         response headers of the most recent
                                            successful download. Used to drive
                                            conditional GET (If-Modified-Since,
                                            If-None-Match) on subsequent checks.
                                            NULL on the shipped fallback and on
                                            any DB that has not yet been updated
                                            from upstream.

┌────────────────────────────────┐
│ categories                     │
│────────────────────────────────│
│ category_id INTEGER  PK        │◄──────────┐
│ name        TEXT  NOT NULL     │           │ FK
└────────────────────────────────┘           │
                                             │
┌────────────────────────────────────────────┴───┐
│ groups                                         │
│────────────────────────────────────────────────│
│ group_id    INTEGER  PK                        │◄──────────┐
│ category_id INTEGER  NOT NULL  FK→categories   │           │ FK
│ name        TEXT     NOT NULL                  │           │
│ IDX(category_id)                               │           │
└────────────────────────────────────────────────┘           │
                                                             │
┌────────────────────────────────────────────────────────────┴───┐
│ types                                                          │
│────────────────────────────────────────────────────────────────│
│ type_id      INTEGER  PK                                       │◄────┐
│ group_id     INTEGER  NOT NULL  FK→groups                      │     │
│ name         TEXT     NOT NULL                                 │     │
│ volume       REAL     NOT NULL    -- m³ per unit               │     │
│ portion_size INTEGER  NOT NULL    -- units per refining batch  │     │
│ is_ore       INTEGER  NOT NULL    -- 0 or 1                    │     │
│ icon_id      INTEGER  NULL        -- nullable; future widgets  │     │
│ IDX(name)                  ◄── name → type_id (parser hot path)│     │ FK
│ IDX(group_id)                                                  │     │ (twice — see below)
│ IDX(is_ore)                ◄── lookup-startup filter           │     │
└────────────────────────────────────────────────────────────────┘     │
                                                                        │
┌──────────────────────────────────────────────────────┐                │
│ type_materials    (refining outputs per ore)         │                │
│──────────────────────────────────────────────────────│                │
│ type_id          INTEGER  NOT NULL  FK→types  ◄──────┼────────────────┤  parent ore
│ material_type_id INTEGER  NOT NULL  FK→types  ◄──────┼────────────────┘  refined output
│ quantity         INTEGER  NOT NULL                   │
│ PRIMARY KEY (type_id, material_type_id)              │
│ IDX(material_type_id)  ◄── reverse: "what produces   │
│                              Tritanium?"             │
└──────────────────────────────────────────────────────┘
```

---

## SQL DDL (canonical — what the schema initialiser actually emits)

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE meta (
    id               INTEGER PRIMARY KEY CHECK (id = 1),
    build_time       TEXT NOT NULL,
    esi_sha256       TEXT,
    sde_sha256       TEXT,
    hoboleaks_sha256 TEXT,
    last_modified    TEXT,
    etag             TEXT
);

CREATE TABLE categories (
    category_id INTEGER PRIMARY KEY,
    name        TEXT NOT NULL
);

CREATE TABLE groups (
    group_id    INTEGER PRIMARY KEY,
    category_id INTEGER NOT NULL REFERENCES categories(category_id),
    name        TEXT NOT NULL
);
CREATE INDEX idx_groups_category_id ON groups(category_id);

CREATE TABLE types (
    type_id      INTEGER PRIMARY KEY,
    group_id     INTEGER NOT NULL REFERENCES groups(group_id),
    name         TEXT    NOT NULL,
    volume       REAL    NOT NULL,
    portion_size INTEGER NOT NULL,
    is_ore       INTEGER NOT NULL CHECK (is_ore IN (0, 1)),
    icon_id      INTEGER
);
CREATE INDEX idx_types_name     ON types(name);
CREATE INDEX idx_types_group_id ON types(group_id);
CREATE INDEX idx_types_is_ore   ON types(is_ore);

CREATE TABLE type_materials (
    type_id          INTEGER NOT NULL REFERENCES types(type_id),
    material_type_id INTEGER NOT NULL REFERENCES types(type_id),
    quantity         INTEGER NOT NULL,
    PRIMARY KEY (type_id, material_type_id)
);
CREATE INDEX idx_type_materials_material_type_id ON type_materials(material_type_id);
```

---

## Refining math example (the math we never pre-compute)

```sql
-- "I mined 10,000 units of Veldspar (type_id = 1230). What does it refine to
--  at 90.6% yield?"
SELECT
    t.type_id   AS material_type_id,
    t.name      AS material_name,
    (10000 / parent.portion_size) * m.quantity * 0.906 AS refined_units
FROM type_materials m
JOIN types parent ON parent.type_id = m.type_id
JOIN types t      ON t.type_id      = m.material_type_id
WHERE m.type_id = 1230;
```

One JOIN-and-multiply per query. No pre-computed refining table to maintain.

---

## What we deliberately skipped from the everef.net JSON

The dashboard only persists what it actually reads or computes against. Everything below is in the upstream JSON and is intentionally not mirrored into SQLite.

**Skipped fields on `types`:**
- `description` (multilingual paragraphs, never displayed)
- `mass`, `packaged_volume`, `capacity`, `radius` (no current use case)
- `ore_variations` (names resolve directly via the `name → type_id` index; variants are just other types)
- `required_skills` (gameplay info we don't display or use)
- `dogma_attributes` (game-mechanics attributes; not relevant to mining stats)
- `base_price` (real market prices come from GoonMetrics, not the SDE base price)
- `market_group_id` (UI hierarchy info; not used)
- `description` localisations beyond English

**`icon_id` is kept** — for future widgets that render icons instead of text labels.

**Skipped JSON files (not imported):**
- `market_groups.json`, `type_bonus.json`, `masteries.json`, `type_dogma.json`, `dogma_attributes.json`, `dogma_effects.json`, `meta_groups.json`, `mutaplasmids.json`, `skills.json`, `units.json`, `blueprints.json`, `icons.json`, `regions.json`, `schematics.json`. Each gets imported when a feature needs it.

**`type_materials.json` is empty in the everef archive** — refining data lives **embedded inside each `types.json` record** under a `type_materials` field, and is read from there.

---

## Filtering rules

- **`published = false` rows are skipped at import.** The `published` flag exists on every `categories`, `groups`, and `types` JSON record. Filtering happens at convert-time and the column is not carried into SQLite — every row in the database is a published EVE item by construction.
- **Multilingual `name` dicts are reduced to `name.en` only.** EVE log lines are English regardless of client locale.
