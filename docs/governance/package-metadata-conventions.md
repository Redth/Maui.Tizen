# NuGet Package Metadata Conventions & Publishing Ownership

This document defines the metadata conventions for `Maui.Tizen.*` NuGet
packages and how package ownership / publishing trust is intended to work.
**No secrets, API keys, or personal-account credentials are defined or
referenced here or anywhere in this repository.** Publishing is gated and
disabled until the items in this document and the transfer checklist are
resolved (see `.github/workflows/release.yml`).

## 1. Package identity conventions

- **ID prefix**: all packages use the `Maui.Tizen.` prefix (e.g.
  `Maui.Tizen.Core`, `Maui.Tizen.Controls`, `Maui.Tizen.Essentials`).
  Reserve the prefix on nuget.org via **NuGet ID prefix reservation** tied
  to the organization account that will own publishing (target: a
  Samsung-controlled or jointly-administered nuget.org organization — see
  the transfer checklist), not an individual maintainer account.
- **Naming**: package IDs use PascalCase segments separated by `.`, mirror
  the primary namespace they expose, and avoid abbreviations that aren't
  already established by `Microsoft.Maui.*` naming (e.g., prefer
  `Maui.Tizen.Controls` over `Maui.Tizen.Ctrls`).
- **No personal namespaces**: package IDs, authors, and owners must never
  reference an individual's personal GitHub/NuGet account as the
  canonical owner. Individual maintainers may be listed as contributors in
  release notes, not as package owners.

## 2. Required package metadata (`.nuspec` / `PropertyGroup`)

Every published package must set:

| Property | Convention |
| --- | --- |
| `PackageId` | `Maui.Tizen.<Area>` |
| `Authors` | Organization name (e.g. `Samsung` / `.NET Foundation` — finalized at transfer), never an individual |
| `Company` | Same as `Authors` |
| `Description` | One sentence, states this is a Tizen backend/extension for .NET MAUI |
| `PackageProjectUrl` | Canonical repo URL (updated at transfer to the new org/repo location) |
| `RepositoryUrl` + `RepositoryType=git` | Same repo, commit-verifiable |
| `PackageLicenseExpression` | `MIT` (pending final confirmation at transfer) |
| `PackageTags` | Includes at minimum `maui`, `tizen`, `dotnet` |
| `PackageReadmeFile` | Package-level README shipped in the `.nupkg` |
| `PublishRepositoryUrl` | `true`, to enable source link / repo association |
| `EmbedUntrackedSources` | `true` |
| `IncludeSymbols` + `SymbolPackageFormat=snupkg` | `true` / `snupkg`, so symbols publish to nuget.org's symbol server |
| `Deterministic` | `true` |
| `ContinuousIntegrationBuild` | `true` when building in CI (required for reproducible/source-linked builds) |
| `PackageIcon` | Shared org icon (added when finalized; do not use a placeholder that implies personal branding) |

These are documented here as the convention; wiring them into the actual
build props is in scope for whichever workstream owns
`Directory.Build.props`/packaging (coordinate with the foundation/CI
session to avoid duplicate or conflicting edits).

## 3. Signing

- All published packages must be **Authenticode-signed** and have their
  NuGet package signed with an organization-owned certificate, applied
  through a CI environment secret/service (e.g., an organization code-signing
  service), never a locally-held personal certificate.
- Signing happens in the release workflow's `sign` job, gated behind a
  protected GitHub Environment (see `.github/workflows/release.yml`).
  Until an organization signing service/certificate is provisioned, the
  `sign` job is a placeholder that fails closed (does not silently skip).
- The signer consumes the exact versioned unsigned workflow artifact emitted
  by `pack`, verifies its SHA-256 manifest, and writes signed copies to a
  separate directory. There is no rebuild and no unsigned fallback.
- Signature verification, SHA-256 recording, GitHub build-provenance
  attestation, and upload of the versioned signed artifact are mandatory
  steps after the signer. They do not use `continue-on-error`.

## 4. Ownership & trusted publishing

- **Target model**: nuget.org **Trusted Publishing** (OIDC-based, no
  long-lived API keys stored as secrets) from a GitHub Actions environment
  scoped to this repository, once it lives under its permanent
  organization. This removes the need to store a NuGet API key as a GitHub
  secret at all.
- **Interim state**: until trusted publishing / organization ownership is
  configured, `.github/workflows/release.yml`'s publish job is disabled
  (see that file's header). Do not add a NuGet API key secret to this
  repository as a workaround — that would create a personal-account
  publishing dependency, which this policy explicitly avoids per the task
  requirements.
- **Package owners on nuget.org**: co-own each `Maui.Tizen.*` package ID
  with (a) the organization account that will be the long-term owner and
  (b) a small break-glass group of individually named maintainers, so a
  single account compromise or departure cannot orphan the package. Exact
  accounts are finalized during transfer (`docs/governance/samsung-transfer-checklist.md`).
- **Multi-factor auth**: any account (individual or organizational) that
  retains standing publish rights outside of trusted publishing must have
  MFA enforced, per nuget.org's own security requirements for
  high-download packages.

## 5. Provenance & supply chain

- Builds intended for publishing must run from this repository's protected
  branches only, via the workflow in `.github/workflows/release.yml`, so
  that GitHub Artifact Attestations / NuGet package provenance can be
  generated and verified. Local/manual `dotnet nuget push` from a
  developer machine is not a supported publishing path once this policy is
  active.
- Release artifacts (`.nupkg`/`.snupkg`) produced by CI should be
  retained as workflow artifacts for audit, independent of whether the
  publish step actually runs.
- Publishing downloads only the versioned signed artifact, then re-verifies
  its hashes, NuGet signatures, and GitHub attestation before the disabled
  publishing guard. It never reads the unsigned artifact from `pack`.
