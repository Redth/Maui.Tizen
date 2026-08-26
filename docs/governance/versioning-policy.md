# Versioning Policy

This document defines package versioning and MAUI/Tizen workload dependency
policy for the externally-owned `Maui.Tizen.*` package family. It is
intentionally conservative: **no package described here is published yet**
(see `.github/workflows/release.yml`); this policy exists so that when
publishing is enabled, versions are predictable and align with .NET MAUI's
own release cadence.

## 1. .NET alignment

Maui.Tizen tracks the **.NET 11** release train as its baseline, matching
.NET MAUI's own major-version alignment with .NET:

- **Target Framework Moniker (TFM)**: packages target the **versioned**
  platform TFM `net11.0-tizen11.0` as the primary, always-built target
  (mirroring versioned platform TFMs such as `net11.0-android35.0` /
  `net11.0-ios18.0` in `dotnet/maui` — Tizen platform TFMs are versioned,
  not bare `net11.0-tizen`). Lower Tizen API-level TFMs (e.g.
  `net11.0-tizen9.0`, `net11.0-tizen8.0`) are **optional matrix entries**,
  added only when `docs/governance/tizen-support-matrix.md` records a
  concrete, validated need to multi-target down-level, not by default. A
  `net11.0` reference/facade target is used only where a package is purely
  abstractions with no Tizen-specific implementation.
- **MAUI version alignment**: the `Microsoft.Maui.*` package versions
  referenced by `Maui.Tizen.*` packages must be within the same .NET 11
  MAUI servicing band (i.e., `11.0.x` depends on `Microsoft.Maui.* 11.0.x`,
  not a mismatched major/minor). Cross-band references (e.g., a `11.0.x`
  Tizen package depending on `Microsoft.Maui.* 12.0.x`) are not supported
  and must not ship.
- **Preview builds**: while .NET 11 is in preview, `Maui.Tizen.*` previews
  use matching preview labels (e.g., `11.0.0-preview.3` paired with
  `Microsoft.Maui.Controls 11.0.0-preview.3`) so consumers can reason about
  compatibility by version string alone.

## 2. Package version scheme

`Maui.Tizen.*` packages use SemVer 2.0, structured as:

```
<major>.<minor>.<patch>[-<prerelease>][+<build-metadata>]
```

- **Major** tracks the aligned .NET major version (e.g., `11` for the
  .NET 11 train). A major bump happens in lockstep with the .NET MAUI major
  version, not independently.
- **Minor** tracks feature-level Tizen backend changes within a .NET major
  version (new controls/handlers, new Tizen API surface support).
- **Patch** is reserved for servicing: bug fixes, security fixes, and
  Tizen platform compatibility fixes that do not add public API.
- **Prerelease labels**: `-preview.N`, `-rc.N` mirror the aligned MAUI/.NET
  SDK prerelease labels for the same train. `-daily.YYYYMMDDNN` (or CI-run
  based) labels are reserved for CI/dev feed builds and must never reach
  the public NuGet feed (see `docs/governance/package-metadata-conventions.md`).

All packages in the `Maui.Tizen.*` family are versioned **in lockstep**
(same version number across the family) unless a documented exception is
recorded in a release's notes — this avoids "dependency matrix" confusion
for consumers picking Tizen packages alongside core MAUI packages.

## 3. MAUI / Tizen workload dependency policy

- **Workload identity**: the Samsung-published Tizen workload identity is
  `tizen` (not a `maui-tizen`-prefixed identity). `Maui.Tizen.*` packages
  declare a minimum and, where necessary, maximum supported `tizen`
  workload manifest version in package metadata /
  `PackageValidationBaselineVersion`-style tracking. The workload manifest
  version is published and versioned independently by Samsung but must
  stay within the same .NET 11 SDK band.
- **Installation path**: workload installation currently does **not** go
  through a plain `dotnet workload install <id>` against the public NuGet
  feed — Samsung's Tizen .NET tooling is installed via Samsung's own
  installer scripts and a private/side-loaded workload manifest flow (the
  exact command and manifest source depend on the Samsung Tizen .NET SDK
  release being targeted). Do not document a specific `dotnet workload
  install` invocation here until that flow is confirmed and stable; the
  authoritative, version-specific install instructions belong in
  `docs/governance/tizen-support-matrix.md` (or a linked Samsung doc) once
  populated, not hardcoded into this policy.
- **No implicit floating dependencies**: package references to
  `Microsoft.Maui.*` and Tizen SDK/NuGet dependencies use exact or
  minimum-inclusive version ranges (`[11.0.0, 12.0.0)`-style floors are
  allowed; open-ended floating latest is not) to avoid silent breaking
  upgrades for consumers.
- **Tizen .NET SDK / `Tizen.NET.Sdk` dependency**: version compatibility
  with Samsung's `Tizen.NET.Sdk` and `Tizen.NET.API*` packages is tracked
  explicitly in `docs/governance/tizen-support-matrix.md` and must be
  updated any time the minimum supported Tizen API level changes.
- **Workload install requirement**: packages that require the `tizen`
  workload must fail fast with an actionable build error (pointing at the
  install path documented in the support matrix) rather than a silent
  restore failure when the workload is missing. This is a build-time
  requirement tracked against the (separately owned) core scaffolding, not
  this policy directly — flagged here so release validation
  (`.github/workflows/release.yml`) can assert it.

## 4. Servicing and support windows

See `docs/governance/release-and-servicing-policy.md` for how long each
major/minor line receives patch releases, and
`docs/governance/deprecation-policy.md` for how APIs and TFMs are retired.

## 5. Version bump checklist (for release approvers)

Before approving a version bump for release:

- [ ] Version number follows the scheme above and matches across all
      `Maui.Tizen.*` packages being released together.
- [ ] `Microsoft.Maui.*` dependency versions are in the same .NET 11
      servicing band.
- [ ] Prerelease label (if any) matches the aligned MAUI/.NET SDK label.
- [ ] `docs/governance/tizen-support-matrix.md` reflects any change to
      minimum supported Tizen API level or workload manifest version.
- [ ] Public API diff reviewed per
      `docs/governance/api-compatibility-policy.md`.
