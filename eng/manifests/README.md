# Source disposition manifests

## What lives here

| File | Owner | Status |
|---|---|---|
| `source-disposition.schema.json` | Foundation (this PR) | Landed |
| `source-disposition.json` (and any generated companions) | Inventory tooling | Pending |

The schema is the **contract**. The manifest data is **generated**, not hand-written, so
that it cannot silently drift from the baselines it describes.

## What the manifest is for

Every Tizen-relevant file in both baselines must appear exactly once, with exactly one
disposition. The point is to make "we forgot about that file" an impossible outcome
rather than a discovery made three phases later.

Scale, from the pinned baselines. These are **blob** counts — see the counting note below.

| Category | Count | Baseline |
|---|---|---|
| Tizen-named files | 314 | `net11.0` (`ee4d06cde6`) |
| Shared files with `#if TIZEN` | 136 | `net11.0` |
| Tizen-named files present at `9.0.120` but absent at the net11.0 pin | 87 | `9.0.120` (`c1f4f7d879`) |

The 87 that exist only at `9.0.120`: `src/Compatibility/**` 70 (Core 48, Material 17,
Maps 5), `src/Controls/docs/…TizenSpecific/*.xml` 9, `src/Templates/**/Platforms/Tizen/**`
7, and one Essentials multi-target file.

> **Counting note for generators.** The GitHub tree API returns `tree` (directory) entries
> alongside `blob`s. Filtering on "path contains tizen" without also filtering
> `type == blob` counts `Tizen/` directories as files, which inflates these figures — it
> gives 76 for `src/Compatibility` and 102 overall. The manifest is per-file, so directory
> entries must be dropped or they become bogus manifest rows.

> **Two distinct "Compatibility" locations.** `src/Controls/src/Core/Compatibility/**`
> (the legacy renderer shim) is **still on `net11.0`**, with an identical 11-file Tizen set
> at both refs, and was imported normally. Only the top-level `src/Compatibility/**` was
> removed upstream. Do not collapse them into one disposition.

## Constraints the schema enforces

Two are worth calling out because they encode real correctness rules, not style
preferences:

1. **`shared-conditional` files may never be `move`.** A file containing `#if TIZEN`
   branches also contains iOS, Android and Windows branches. Copying it wholesale would
   fork code this repository does not own and cannot maintain. Only `rebuild`,
   `keep-upstream` and `exclude` are permitted.

2. **`exclude` requires written justification.** Without it, an excluded file is
   indistinguishable from an overlooked one when someone re-reads the manifest in six
   months.

Commit SHAs must be full 40-character hashes. Branch names are rejected: `origin/net11.0`
advanced from `ee4d06cde6` to `bedd1b18b7` during the few hours the initial import was
prepared, which is exactly the class of failure this guards against.

## Generating

Generators are owned by the inventory tooling workstream. They must:

- read baselines from [`../baselines.json`](../baselines.json) rather than hardcoding refs
- read **both** baselines — a `net11.0`-only pass under-reports by the 70 Compatibility
  files, with no error
- consider historical paths when recording provenance. The backend has lived under
  `Xamarin.Forms.Platform.Tizen/**`, `Stubs/**`, `Samples/Samples.Tizen/**`,
  `PagesGallery/**`, `EmbeddingTestBeds/**` and `src/Platform.Renderers/**`; current
  paths alone do not identify a file's history
- validate output against `source-disposition.schema.json` before writing

## See also

- [`../../docs/migration.md`](../../docs/migration.md) — disposition legend and open decisions
- [`../../docs/architecture.md`](../../docs/architecture.md) — the collision rules the dispositions implement
