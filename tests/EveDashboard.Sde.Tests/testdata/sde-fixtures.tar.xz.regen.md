# `sde-fixtures.tar.xz` — regeneration

This binary is `tar -cJf` of [sde-fixtures/](sde-fixtures/) (the four JSON files; **README.md excluded**). Used by `EverefSdeUpdaterTests` to feed `EverefSdeUpdater` a realistic but tiny payload via the fake `HttpMessageHandler` — the production code path's xz + tar decoder runs against bytes whose contents test cases assert against.

We commit the binary because SharpCompress 0.47.4 cannot **write** xz (it only reads it) Building the fixture at test setup is therefore not viable; the file is generated once and shipped with the repo.

## When to regenerate

Whenever any file under [sde-fixtures/](sde-fixtures/) changes (a new test row, a renamed field, etc.). The pre-existing converter tests run against the JSON folder directly; the updater tests run against this archive. They must encode the same rows.

## How to regenerate

From the repo root, with Git Bash (or any shell that ships `tar` with xz support):

```bash
cd tests/EveDashboard.Sde.Tests/testdata
tar -cJf sde-fixtures.tar.xz -C sde-fixtures categories.json groups.json meta.json types.json
```

Verify (should be ~1 KB):

```bash
ls -la sde-fixtures.tar.xz
tar -tJf sde-fixtures.tar.xz
```

Commit the new binary. **Do not** add `sde-fixtures/README.md` to the archive — the converter only reads the four required JSON files, and including the README would change the expected entry count in tests that iterate the archive.
