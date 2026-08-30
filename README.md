# Maui.Tizen

The Tizen backend for [.NET MAUI](https://github.com/dotnet/maui), extracted into a
standalone, externally maintained repository.

> **Status: compiled migration, not yet publishable.** Core, Waves A/B/C, Essentials,
> alerts, gestures, and the provisional modal adapter are compiled and tested through
> workload-free host and API15 reference-pack lanes. No packages are published. See
> [Current state](#current-state) before filing issues.

## Why this exists

The Tizen backend shipped inside `dotnet/maui` for years, compiled directly into the
`Microsoft.Maui.*` assemblies. Maintaining it there ties Tizen's release cadence to
.NET MAUI's and puts platform-specific work in a repository whose maintainers do not own
the platform. This repository separates the two.

## Current state

| | |
|---|---|
| Imported history | 1,236 commits, 121 authors, back to 2016 |
| Files imported | 316 |
| Repository scaffolding | Complete |
| Package projects | Core, Controls/Waves, Essentials, alerts and gestures have deterministic compiled source closures |
| Published packages | None |
| Workload-free verification | Host tests, API15 RefPack compilation, PublicAPI/source closure, consumer compile, tooling and mutation suites |
| **Real Tizen build/package/device lanes** | **Blocked on external dependencies** |

### External gates

`net11.0-tizen11.0` cannot be restored or built by anyone right now. The workload manifest
`Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7` has not been published to nuget.org — only the
`9.0.100` and `10.0.100` bands exist.

This is deliberately surfaced rather than worked around. There is no neutral `net11.0`
fallback: it would make CI green while producing assemblies that cannot run on Tizen.
Building a Tizen project without the workload fails with a `MAUITIZEN0001` error
explaining exactly this.

Details in [`docs/migration.md`](docs/migration.md).

Additional publication gates remain explicit:

- `Tizen.UIExtensions.NUI` must be republished without its .NET 6-era MAUI dependency graph.
- dotnet/maui#37853 modal contracts have not been adopted in the pinned published MAUI package.
- dotnet/maui#37861 is merged, but its public long-press send APIs are absent from the pinned
  MAUI package.
- Device, lifecycle/input and visual-baseline evidence needs the Samsung workload and provisioned
  mobile/TV runners.

`Maui.Tizen.Controls` has distinct `GenerateNuspec` guards for the modal and long-press package
contracts. They inspect the pinned `Microsoft.Maui.Controls.dll` and the compiled local source
closure. Even a verified upstream binary override cannot unblock packing until the provisional
modal contracts are removed/registered against MAUI and long-press dispatch actually calls the
public send APIs.

Window-scope alert teardown is also single-channel: every waiting request receives native
close/dispose failures through its result task, while framework-owned unsubscribe, scope disposal
and application termination attempt all cleanup, report diagnostics, and do not throw again.

## What you can build today

```bash
./eng/build-workload-free.sh
```

This runs everything that does not need the Tizen workload: compiled Core/Waves/Essentials and
alerts/gestures host suites, API15 Core/Controls/Sample/Essentials lanes, Controls consumer compile,
PublicAPI and source-closure checks, migration tooling, package policy, and locked negative-control
mutations. `eng/validation/run-hosted-validation.sh` adds repository, package, convention, DevFlow
and consumer validation. The external-gate job installs through Samsung's supported workload
installer and runs `eng/build-tizen.sh`; it cannot report success by skipping or masking a failed
real Tizen restore/build/pack.

Requires the .NET SDK pinned in [`global.json`](global.json) (11.0.100-preview.7).

## Layout

```
src/
  Maui.Tizen.Core/           Handlers, platform views, lifecycle, fonts, image sources
  Maui.Tizen.Controls/       Shell, CollectionView, shapes, gesture and modal managers
  Maui.Tizen.Essentials/     Sensors, device info, connectivity, storage, media
  Maui.Tizen.BlazorWebView/  BlazorWebView platform implementation
  Maui.Tizen.Maps/           Map handlers and controls
  Maui.Tizen.Graphics/       Skia view (provisional)
  Maui.Tizen.Compatibility/  Provisional; see its README
  Maui.Tizen.Build.Tasks/    Manifest, resource and splash MSBuild tasks
  Maui.Tizen.Templates/      dotnet new templates (not yet authored)
samples/                     Imported sample applications
tests/UnitTests/             Repository invariant tests
eng/
  baselines.json             Pinned upstream baselines
  import/                    Reproducible history import tooling
  manifests/                 Source disposition schema and data
  api-baselines/             Imported and standalone PublicAPI baselines
  validation/                Hosted, package, consumer and device validation
docs/                        Architecture and migration documentation
```

## Naming policy

Three identities, decided independently — this trips people up, so it is stated plainly:

| | Value | Reason |
|---|---|---|
| Package IDs | `Maui.Tizen.*` | Externally owned; must not squat Microsoft-owned IDs |
| Assembly names | `Maui.Tizen.*` | Two assemblies cannot share a simple name |
| Namespaces | `Microsoft.Maui.*` **preserved** | Compile-time only; keeps imported code and consumer `using` directives unchanged |

New implementation-only types use `Microsoft.Maui.Platforms.Tizen.*`, a prefix unused
throughout `dotnet/maui`, so it cannot collide.

Full rules in [`docs/architecture.md`](docs/architecture.md).

## Provenance

This is a history-preserving extraction, not a copy. Original commits, authors and dates
were carried across, reaching back to the Xamarin.Forms era — the backend predates .NET
MAUI by years.

```bash
git shortlog -sne --no-merges
git log --follow src/Maui.Tizen.Core/Platform/Tizen/ViewExtensions.cs
```

The import is reproducible from [`eng/import/`](eng/import/). See
[`PROVENANCE.md`](PROVENANCE.md).

## Licence

MIT — see [`LICENSE`](LICENSE).

Samsung's TizenFX and `Tizen.UIExtensions` are Apache-2.0 and are consumed only as
published NuGet packages; no Samsung source is included here. See
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — how this composes with .NET MAUI, type collision rules
- [`docs/migration.md`](docs/migration.md) — phases, status, and the external gate
- [`PROVENANCE.md`](PROVENANCE.md) — what was imported and how to reproduce it
