# Release and Servicing Policy

## 1. Release cadence

Maui.Tizen releases follow .NET MAUI's own cadence, since it depends
directly on `Microsoft.Maui.*` packages:

- **Major/minor releases**: aligned to .NET's yearly major release (e.g.
  .NET 11 in November) and MAUI's corresponding minor releases throughout
  a servicing year.
- **Servicing (patch) releases**: on the same monthly-ish cadence as .NET
  servicing releases, when there are fixes to ship. Not every servicing
  month requires a Maui.Tizen patch — only cut a release when there is a
  fix, security update, or a required compatibility bump.
- **Preview releases**: during a .NET preview cycle (e.g. .NET 11
  Preview 1-7, RC1/RC2), Maui.Tizen publishes matching previews so the
  Tizen backend is validated against MAUI previews before GA, not after.

## 2. Supported version lines

| Line | Definition | Support duration |
| --- | --- | --- |
| **Current (N)** | Latest GA major.minor line | Full support: bug fixes, security fixes, new Tizen API coverage |
| **Previous (N-1)** | Prior GA major line | Security and critical-bug fixes only, for the remainder of its aligned .NET support window |
| **Preview/RC** | Pre-GA builds for the next .NET train | Best-effort; not guaranteed patchable, superseded by next preview |
| **Out of support** | Anything older than N-1, or an out-of-support .NET version per Microsoft's `.NET Support Policy` | No fixes; upgrade guidance only |

Maui.Tizen support windows **cannot exceed** the underlying .NET version's
own support window (per the [.NET and .NET Core Support Policy](https://dotnet.microsoft.com/platform/support/policy)):
if .NET 11 is Standard Term Support (STS) or Long Term Support (LTS), the
matching Maui.Tizen `11.x` line inherits that same classification and end
date. This must be stated explicitly in each release's notes.

## 3. What qualifies for a servicing (patch) release

- Regressions from a previous release.
- Security vulnerabilities (see `SECURITY.md`), fast-tracked outside normal
  cadence when severity warrants.
- Crashes or data-loss bugs on supported Tizen profiles/API levels (see
  `docs/governance/tizen-support-matrix.md`).
- Compatibility fixes required to keep pace with a .NET/MAUI servicing
  release in the same band.

New features, new public API, and non-critical enhancements are **minor**
release material, not patch material, per
`docs/governance/versioning-policy.md`.

## 4. Release process gate

No release is published without:

1. A version bump following `docs/governance/versioning-policy.md`.
2. A clean build + full test pass on all supported Tizen profiles in the
   support matrix.
3. Public API diff review per
   `docs/governance/api-compatibility-policy.md`.
4. Required approvals in the protected GitHub Environments used by
   `.github/workflows/release.yml` (build/sign/publish gates).
5. Release notes describing changes, the .NET/MAUI version band, and any
   support-window changes.

Until package IDs, NuGet ownership, and signing are finalized (see
`docs/governance/package-metadata-conventions.md`), the publish step of the
release workflow remains disabled regardless of how far a candidate gets
through steps 1-4.

## 5. Hotfix process

For a critical/security fix on a supported line:

1. Branch from the released tag (not `main`) if `main` has since diverged
   with unrelated changes.
2. Apply the minimal fix + regression test.
3. Follow the same release process gate above, using an expedited review
   (still requires environment approvals — security urgency does not skip
   the sign/publish gates).
4. Backport the fix to `main`/the active development branch.

## 6. Deprecation and EOL communication

See `docs/governance/deprecation-policy.md` for how APIs, TFMs, and whole
version lines are announced as deprecated/EOL, including minimum notice
periods.
