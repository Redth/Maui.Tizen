# Tizen Supported Profile / API Matrix (Template)

> **Status: TEMPLATE.** No column below is populated with a committed
> support claim yet — values are placeholders (`TBD`) until the
> validation harness (owned by another workstream) produces real pass/fail
> data. Do not treat this file as an actual compatibility claim until the
> `TBD` markers are replaced and this file is referenced from a tagged
> release's notes.

This matrix is the canonical, versioned record of which Tizen profiles and
API levels each `Maui.Tizen.*` release line supports. It is updated as part
of the release process gate (`docs/governance/release-and-servicing-policy.md`
§4) and referenced by `docs/governance/api-compatibility-policy.md` §6 when
evaluating whether raising a minimum API level is a breaking change.

## How to read this table

- **Tizen Profile**: Mobile, Wearable, TV, IoT/Common, Automotive, etc.
- **Min API Level / Max API Level**: the Tizen API level range validated
  for this release line. "Max" is typically "latest available" unless a
  known incompatibility exists.
- **Status**: `Supported`, `Best-effort`, `Deprecated` (see
  `docs/governance/deprecation-policy.md`), or `Not supported`.
- **Validated by**: how support was verified — device, emulator, or CI
  harness — and the date/build it was last confirmed on.

## Maui.Tizen `11.0` (target: aligned with .NET 11)

| Tizen Profile | Min API Level | Max API Level | Status | Validated by | Notes |
| --- | --- | --- | --- | --- | --- |
| Mobile | TBD | TBD | TBD | TBD | Primary target profile (planned) |
| Wearable | TBD | TBD | TBD | TBD | |
| TV | TBD | TBD | TBD | TBD | |
| IoT / Common | TBD | TBD | TBD | TBD | |
| Automotive | TBD | TBD | TBD | TBD | Not currently in scope; revisit post-GA |

## Tooling dependency versions (per release line)

| Dependency | Minimum version | Notes |
| --- | --- | --- |
| `Tizen.NET.Sdk` | TBD | Samsung-published SDK; pin range per `docs/governance/versioning-policy.md` §3 |
| Tizen workload manifest (`tizen`) | TBD | Samsung-published workload identity; install path is Samsung's installer scripts/private manifest flow, not a public `dotnet workload install` today — record the exact confirmed steps here once validated |
| .NET SDK | pinned via `global.json` once published (target: .NET 11 preview band, e.g. `11.0.100-preview.7`) | Must match the .NET train this release line targets |
| Emulator / device images used for validation | TBD | List specific emulator image versions, not just "latest" |

## Process for updating this file

1. Whoever owns the validation harness produces a pass/fail report per
   profile/API level for the release candidate.
2. Release approvers replace `TBD` with concrete values (or `Supported` /
   `Not supported` / `Best-effort`) before sign-off, referencing the
   validation run (link/build ID) in the "Validated by" column.
3. Any downgrade from `Supported` to `Best-effort`/`Deprecated`/
   `Not supported` between releases must follow
   `docs/governance/deprecation-policy.md`'s notice-period rules — it
   cannot happen silently in a patch release.
4. This file is versioned per release line (consider snapshotting a copy
   per major/minor under `docs/governance/history/` once the first real
   release ships, so historical claims remain auditable).
