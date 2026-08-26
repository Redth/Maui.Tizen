# Public API baselines

## What lives here

| Directory | Contents | Owner |
|---|---|---|
| `net9.0-tizen7.0/` | API surface extracted from the **published** MAUI 9.0.120 Tizen assemblies | Inventory tooling |
| `net11.0-publicapi/` | The 18 `PublicAPI/net-tizen/*.txt` files collected from the `net11.0` source baseline | Inventory tooling |

Both are currently empty. The generators are owned by the inventory tooling workstream;
this directory structure is the contract they target.

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
