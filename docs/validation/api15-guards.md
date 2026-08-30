# API15 source guards

Guards that stop compiled source from using platform APIs that do not exist, or are deprecated, on
Tizen API15.

Contract: `eng/validation/api15-contract.json`
Tests: `Maui.Tizen.Conventions.Tests.Api15SourceGuardTests`, `Api15ReferencePackTests`

## The rules

| Rule | Banned | Replacement |
|---|---|---|
| `tizen-maps-removed` | `Tizen.Maps`, `MapService` | none — the platform API is gone |
| `nui-window-instance-deprecated` | `Window.Instance` | `Window.Default` |

Both are **verified facts**, not assertions copied from a migration note. The reference pack is an
ordinary NuGet package, so the hosted lane downloads `Samsung.Tizen.Ref.API15 15.0.0.19396` (the
version pinned in `eng/baselines.json`) and checks them:

- `Tizen.Maps.dll` is absent from the pack's 105 reference assemblies.
- `Tizen.NUI.Window.Instance` carries
  `[Obsolete("This has been deprecated in API12, please use Default instead")]`, and
  `Window.Default` exists.

This also makes the rules **self-retiring**. If Samsung restores a removed assembly or drops a
deprecation, `Api15ReferencePackTests` fails and says the rule should be deleted — rather than the
repository carrying a workaround for a problem that no longer exists.

## Scope: only what is actually compiled

The guard scans a file exactly when a project compiles it.

This matters because the raw import still shares directories with adopted code. Core, Controls and
Essentials keep default globbing disabled and instead compile explicit shared source manifests; the
guard resolves those manifests and scans their exact closures. Projects that still compile nothing
remain out of scope, so historical sources are not mistaken for shipping code.

A project enters scope automatically the moment it opts into compiling, so the handler, Essentials
and Blazor waves get covered as they land, with no change here.

`CompiledSourceInventory` resolves the compile set by reading project XML rather than by evaluating
MSBuild, because the Tizen-targeted projects cannot be evaluated at all without the Samsung workload
— the platform identifier is unrecognised, so evaluation fails long before item lists exist. It
handles explicit `<Compile Include/Remove>`, the default `**/*.cs` glob, and
`EnableDefaultCompileItems` inherited from an imported props file, which is the mechanism
`eng/targets/TizenPackage.props` actually uses.

Today that includes the finalized Core/Waves, Controls navigation, implemented Essentials, and the
two diagnostics projects. The guard previously found a real violation in the DevFlow agent:
`Window.Instance` in three places.

## Why not just rely on the compiler?

`Window.Instance` is `[Obsolete]`, and CI builds with `TreatWarningsAsErrors`, so it would fail as
`CS0618` eventually. Two problems with waiting for that:

1. It cannot fail today. The Tizen projects cannot be compiled by anyone until the Samsung workload
   ships, so the compiler is not available as a gate at the moment the code is being written.
2. `CS0618` reports the deprecation message, not the repository's decision. The guard reports the
   file, line, the offending text, the reason and the replacement.

`Tizen.Maps` would not be a warning at all — it would be a type-not-found error, and only on a
machine that can build Tizen.

## Comments and strings are not code

The scanner blanks out comments and string literals before matching, preserving length so line and
column numbers stay exact.

This is not theoretical: this repository's own documentation and code comments discuss `Tizen.Maps`
and `Window.Instance` at length, including in the contract file that defines the rules. A raw text
search would fail on the explanation of the rule it was enforcing.

## `MapService` vs `MapServiceToken`

The ban is on `MapService`. `MapServiceToken` is explicitly **allowed**:

| Member | Status | Why |
|---|---|---|
| `IGeocoding` / `IPlatformGeocoding` | registered unsupported service | Built on `Tizen.Maps`, which no longer exists. One `TizenGeocoding` singleton is registered for both contracts; operations throw `FeatureNotSupportedException`. |
| `MapServiceToken` | accepted, no-op | App startup and the Essentials DI bridge set it during initialisation. Removing it turns a now-meaningless configuration call into a compile break for every consumer; throwing turns it into a startup crash. |

The distinction is enforced by matching whole identifiers (`(?!\w)` after the symbol), so
`MapServiceToken` is not a `MapService` match. A naive substring ban would flag the shim it is meant
to preserve, and the only way to silence that would be to drop the rule.

`IGeocoding` is recorded as unsupported rather than silently returning empty results, because an
empty result is indistinguishable from "no match found" and would send callers debugging their input
instead of discovering the platform gap.

## Adding a rule

1. Add an entry to `bannedSymbols` in `eng/validation/api15-contract.json` with an `id` and a
   `reason`. Both are enforced.
2. Where the rule derives from the platform, add `referencePackAssembly` +
   `expectedInReferencePack`, or `referencePackType` + `obsoleteMember` + `replacementMember`, so it
   is verified rather than asserted.
3. If the ban would catch a look-alike identifier that is intentional, add it to
   `allowedIdentifiers`.
