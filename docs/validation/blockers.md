# Blockers

External dependencies that stop parts of the validation lane from running. Everything here was
verified on this machine rather than assumed.

## 1. The Samsung .NET 11 workload is unpublished

**Blocks:** every `net*-tizen*` build, TPK creation, deploy/run, on-device tests, visual baselines,
live handler parity, live DI and Essentials coverage.

`eng/baselines.json` records it:

```json
"workloadManifest": {
  "id": "samsung.net.sdk.tizen.manifest-11.0.100",
  "status": "unavailable"
}
```

Reproduced directly:

```
$ dotnet build probe.csproj      # <TargetFramework>net11.0-tizen11.0</TargetFramework>
error NETSDK1139: The target platform identifier tizen was not recognized.
```

The same error appears on SDK `10.0.400` and `11.0.100-preview.7`.

### The trap: `maui-tizen` is not the Tizen workload

`dotnet workload list` can report `maui-tizen` as installed while every Tizen build still fails.
That workload carries no Tizen platform packs at all — in `microsoft.net.sdk.maui`'s manifest it is:

```json
"maui-tizen": { "description": ".NET MAUI SDK for Tizen", "extends": ["maui-blazor"] }
```

Platform support comes from Samsung's separate `samsung.net.sdk.tizen` manifest. On this machine:

```
$ ls "$DOTNET_ROOT/sdk-manifests/10.0.100/" | grep -i samsung   # (nothing)
$ ls "$DOTNET_ROOT/packs/" | grep -i tizen                      # (nothing)
```

`TizenWorkload` in `Maui.Tizen.TestUtils` probes for the Samsung manifest specifically, and
`tizen-device-lane.sh preflight` greps for the `tizen` workload rather than `maui-tizen`, so neither
can produce this false positive.

**Clears when:** Samsung publishes the `11.0.100` band. Only `9.0.100` and `10.0.100` exist today,
and the newest `Samsung.Tizen.Sdk` is `10.0.128`.

**Watched by:** the `tizen-workload-gate` job, which attempts the install on every CI run and
reports the result in the job summary.

## 2. No Tizen device or emulator infrastructure

**Blocks:** everything in the device lane, independently of blocker 1.

The lane requires a self-hosted runner labelled `tizen`, with Tizen Studio (`sdb`, `tizen`) and an
emulator or device attached. No such infrastructure is referenced by this repository, by design —
serials, hostnames and accounts belong in a runner's own configuration, never in source.

**Enabled by:** setting the `TIZEN_DEVICE_LAB_ENABLED` repository variable to `true` and registering
a runner. See [device lane](device-lane.md).

## 3. `Tizen.UIExtensions` still carries .NET 6-era MAUI Graphics

**Blocks:** nothing from building; it is a correctness risk that would surface at runtime on a
device.

`Tizen.UIExtensions.NUI 0.9.2` declares a dependency that resolves `Microsoft.Maui.Graphics` in the
6.x line. Against a `net11.0-tizen11.0` head that produces missing-type failures at runtime rather
than build errors.

This is recorded as a policy rule with `expectedStatus: "known-violation"` in
`eng/validation/profiles/tizen-profiles.json` and verified in **both directions** by
`DependencyPolicyTests.PublishedUIExtensions_MatchesItsRecordedDependencyStatus`: it fails today if
the violation disappears, prompting the rule to be flipped to enforcing. That is the only reliable
way to stop a stale exemption outliving the problem it described.

**Clears when:** Samsung republishes `Tizen.UIExtensions` without the .NET 6-era dependencies. No
API surface change is expected (`eng/baselines.json > target.notes`).

## 4. DevFlow has no Tizen support upstream

**Blocks:** nothing here; it shapes the design.

- No `Microsoft.Maui.DevFlow.Agent.Tizen` package exists, and `Microsoft.Maui.DevFlow.Agent` ships
  only Android, iOS, Mac Catalyst, macOS and Windows target frameworks. The Tizen backend is
  therefore ours to write, against `Microsoft.Maui.DevFlow.Agent.Core`.
- The `agent-status.json` schema constrains `platform` to
  `ios | android | maccatalyst | windows | linux | macos`. `tizen` is not a member, so an accurate
  value is out of spec. Both behaviours are implemented and selectable
  (`TizenPlatformReporting`), and `DevFlowContractTests.DevFlowSpecPlatformEnum_StillLacksTizen`
  fails the moment upstream adds it.

**Follow-up:** file an issue at `dotnet/maui-labs` to add `tizen` to the platform enum.

## 5. DevFlow preview packages have an inconsistent dependency graph

**Blocks:** nothing; worked around explicitly.

`Microsoft.Maui.DevFlow.Agent.Core 0.1.0-preview.12.26421.1` depends on
`Microsoft.Maui.Essentials >= 10.0.20` while also pulling `Microsoft.Maui.Core 10.0.41`, which needs
`Essentials >= 10.0.41`. NuGet reports `NU1605` unless the consumer pins Essentials, so the
diagnostics projects reference it explicitly.

DevFlow also requires `SkiaSharp >= 3.119.2` while this repository centrally pins `3.116.1`. Raising
the central pin would let a debug-only diagnostics package dictate the version the shipping Graphics
assemblies build against, so instead the diagnostics projects set
`CentralPackageTransitivePinningEnabled=false` locally. DevFlow is never referenced by a shipping
package, so this cannot affect what is published.

## 6. Two test frameworks in one repository

**Blocks:** `dotnet test` as a single entry point.

`tests/UnitTests` uses xunit v2 on VSTest. The validation suites use xunit v3, which runs on
Microsoft.Testing.Platform, and the .NET 10+ SDK removed VSTest support for that platform:

```
error: Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
       on .NET 10 SDK and later.
```

Opting into the Microsoft.Testing.Platform runner in `global.json` fixes the v3 suites and
simultaneously breaks the v2 project. Rather than force that migration as a side effect, the v3
suites are executed directly as binaries by `eng/validation/run-hosted-validation.sh` — a supported
and ordinary way to run them.

`RepositoryContractTests.TestRunnerSplit_IsRecordedRatherThanAssumed` guards the transition: it
fails if `global.json` opts in while the v2 project still exists.

**Clears when:** `tests/UnitTests` moves to xunit v3. Then add
`"test": { "runner": "Microsoft.Testing.Platform" }` to `global.json` and the runner script
collapses to a single `dotnet test`.

## 7. The Tizen DevFlow agent is not verified by a compiler

**Consequence of blocker 1.**

`src/Diagnostics/Maui.Tizen.DevFlow.Agent` targets `net11.0-tizen11.0` and cannot be compiled
anywhere. Two things limit the exposure:

- Everything that does not need Tizen types lives in `Maui.Tizen.DevFlow.Agent.Shared`, which builds
  and is tested on the hosted lane — capability policy, privilege gating, platform identity, the
  native-element registry, and the connection/forwarding descriptors.
- Every DevFlow member the Tizen agent overrides is pinned by `DevFlowContractTests` against the
  real published package. If maui-labs changes a signature, the hosted lane fails immediately rather
  than the problem surfacing on a device months later.

What remains unverified is the Tizen-specific code itself: NUI property access, `Tizen.NUI.Capture`
usage, and privilege queries. That is stated plainly at the top of the project file rather than left
for someone to discover.
