# Public API baselines

## What lives here

| Directory | Contents | Owner |
|---|---|---|
| `net9.0-tizen7.0/` | API surface extracted from the **published** MAUI 9.0.120 Tizen assemblies | Inventory tooling |
| `net11.0-publicapi/` | The 18 `PublicAPI/net-tizen/*.txt` files collected from the `net11.0` source baseline | Inventory tooling |

The `net11.0-publicapi/manifest.json` hashes are also the repository trust anchor for the
normalized copies under `src/**/PublicAPI/net-tizen/`. The manifest pins the upstream commit,
and `eng/manifests/source-disposition.json` maps each upstream path to its imported target path.
The workload-free tests join those two manifests and require every target file to remain
byte-identical. They also reject deletion, extra files, and path or case drift without network
access or a writable hash sidecar.

## Imported provenance versus package baselines

These directories have different owners and must never be generated into each other:

| Path | Purpose | Update rule |
|---|---|---|
| `src/**/PublicAPI/net-tizen/` | Imported dotnet/maui provenance fixture for the pinned `sourceBaseline` | Never regenerate from a Maui.Tizen assembly. It changes only with an intentional source-baseline import update. |
| `src/**/PublicAPI/slice/` | Public API contract generated for a standalone Maui.Tizen package assembly | Regenerate when that package's API intentionally changes and attach it explicitly with `AdditionalFiles`. |

To intentionally update the imported fixtures, change the pinned source commit in
`eng/baselines.json`, obtain a verified snapshot with `Get-MauiSourceSnapshot.ps1`, regenerate
`net11.0-publicapi/` and `source-disposition.json`, and copy the source bytes to the mapped
`net-tizen` targets. The source-ref change, trusted hashes, copied artifacts, and imported files
must appear together in review. CI only verifies this state; it never learns or rewrites hashes.

## Why two baselines

They answer different questions, and conflating them produces misleading diffs.

- **`net9.0-tizen7.0`** is what shipped and what real applications compile against today.
  It is the behavioural contract — the thing that must not silently regress.
- **`net11.0-publicapi`** is where upstream was heading before the extraction. It reflects
  in-progress API that was never released.

A diff between them is expected to be noisy, and not every difference is a regression.
Note in particular that the platform version moves from `tizen7.0` to `tizen11.0`, so some
surface differences come from the Tizen platform itself rather than from any MAUI change.

## Generation notes

The 9.0.120 assemblies cannot be loaded by reflection on a machine without the Tizen
reference assemblies, so extraction must be **metadata-only** (for example
`System.Reflection.Metadata`) rather than `Assembly.Load`-based.

Package versions and feeds are pinned in [`../baselines.json`](../baselines.json). Do not
hardcode them in generators — a baseline that disagrees with `baselines.json` is worse
than no baseline.

## See also

- [`../../docs/migration.md`](../../docs/migration.md)
- [`../manifests/README.md`](../manifests/README.md)
