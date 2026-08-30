# API Compatibility Policy

Maui.Tizen packages are consumed as compiled dependencies by app developers,
so uncontrolled breaking changes are treated as high-severity issues. This
policy defines what counts as a breaking change and how it must be handled.

## 1. What counts as public API

Any type, member, or resource that is:

- `public` or `protected` (including `protected internal`) and reachable
  from outside the assembly, **or**
- Shipped as a `.targets`/`.props` MSBuild property, item, or task intended
  for consumer use, **or**
- A resource/behavior documented as consumer-facing (e.g. a Tizen-specific
  `Handler` mapping key, an `AppDomain`/DI service registration contract).

`internal`, `private`, and anything explicitly marked
`[EditorBrowsable(EditorBrowsableState.Never)]` or annotated as
experimental (see §4) is not covered by this policy.

## 2. Compatibility tooling

- Public API surface is tracked via checked-in baseline files (for example
  `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`-style tracking,
  consistent with the tooling used by `dotnet/maui`).
- CI must run an API compatibility check comparing the current build
  against the last shipped stable version before a release candidate is
  approved. `.github/workflows/release.yml` invokes the executable
  `eng/release/release-contract.py verify-policies` gate against the exact
  unsigned release artifact. It fails until
  `eng/release/release-policy.json` names a concrete baseline version and
  baseline directory. The baseline manifest must identify that same version
  and hash every schema-v2 API dump plus every consumer-facing
  `build*/*.props` / `build*/*.targets` file. The comparison rejects removed or changed public
  types/members, delegate and enum shape changes, tighter generic
  constraints, reduced property accessor visibility, newly sealed
  overrides, added abstract requirements, and new base-interface
  requirements.
- `eng/api-baselines/net9.0-tizen7.0` is an `upstream-reference` baseline for
  migration/parity work. It cannot be selected by the release policy because its
  `Microsoft.Maui.*` assembly/package identities are not the standalone
  `Maui.Tizen.*` shipping shape.
- Generate a selectable `standalone-release` baseline from one complete, reviewed
  Maui.Tizen package set with:
  `eng/scripts/generate-release-api-baseline.ps1 -PackagesDirectory <dir> -PackageVersion <version> -ReleaseManifest <manifest>`.
  The generator records the standalone assembly names, API dump hashes, and
  consumer-facing `build*/*.props`/`build*/*.targets` bytes. The selected
  baseline must predate the release being built; bootstrap the first public
  release from a separately reviewed prerelease candidate rather than
  baselining a release against itself.

## 3. Change classification

| Change type | Examples | Allowed in |
| --- | --- | --- |
| **Additive (non-breaking)** | New type, new optional overload, new member on a `sealed`/non-inheritable type, new enum value marked to tolerate unknown values | Patch or minor |
| **Breaking (binary or source)** | Removing/renaming public members, changing a method signature, sealing a previously unsealed public type, changing default behavior in an observable way, tightening a public constraint | **Major only**, and only with the deprecation notice period in `docs/governance/deprecation-policy.md` satisfied |
| **Behavioral breaking (no signature change)** | Changing default value, changing exception type thrown, changing thread-affinity/async behavior | Treated as breaking; requires the same notice period and explicit release-notes callout even though ApiCompat tooling may not catch it automatically |

## 4. Experimental / preview API

- APIs not yet stable may be marked with an `[Experimental]`/analyzer-diagnostic
  attribute (consistent with .NET's `Experimental` diagnostic pattern) and
  are exempt from the breaking-change restriction above, provided:
  - The experimental status is documented in the member's XML doc comment.
  - The release notes list all experimental APIs added/changed/removed.
  - An API does not stay "experimental" across more than **2 minor
    releases** without either stabilizing or being removed — indefinite
    experimental status defeats the purpose of this carve-out.

## 5. Review requirements

Any PR touching public API surface must:

- [ ] Update the API baseline file(s) alongside the code change (fails CI
      otherwise).
- [ ] State in the PR description whether the change is additive or
      breaking, using the table in §3.
- [ ] For breaking changes: link the deprecation/notice issue required by
      `docs/governance/deprecation-policy.md`, and get sign-off from a
      CODEOWNERS approver with release authority (see `.github/CODEOWNERS`),
      not just a general code reviewer.

For a major release, each approved break must also be entered exactly in
`release-policy.json` under `apiCompatibility.approvedBreakingChanges`, together
with its concrete GitHub deprecation issue. The executable gate accepts the list
only when the release major is newer than the baseline major, every detected
managed or MSBuild difference has one exact approval bound to that baseline
version and release major, and no stale approval remains.

## 6. Tizen-specific considerations

- Because Tizen exposes capabilities across multiple profiles/API levels
  (see `docs/governance/tizen-support-matrix.md`), an API that is
  functionally unavailable on a given profile must **not** be removed from
  the public surface — it should throw `PlatformNotSupportedException` (or
  the MAUI-equivalent pattern used elsewhere in `dotnet/maui`) rather than
  fail to compile, so app code stays portable across profiles.
- Raising the *minimum* supported Tizen API level is a breaking change for
  purposes of this policy (it can strand existing consumers), and follows
  the same major-version/notice-period rules as a public API removal.
