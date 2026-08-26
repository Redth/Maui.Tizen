# Hosted lane

Everything that can be validated without a Tizen workload or a device. Runs on `ubuntu-latest` for
every push and pull request, and is a required check.

```bash
./eng/validation/run-hosted-validation.sh
```

## Suites

| Project | Covers |
|---|---|
| `Maui.Tizen.Validation.Tests` | Repository contracts, profile matrix, catalog manifest, baseline conventions, PNG codec and image comparer |
| `Maui.Tizen.Build.Tests` | Restore/build/pack, package-content contracts, dependency policy, MSBuild and `buildTransitive` behaviour |
| `Maui.Tizen.Conventions.Tests` | Handler mapper/command parity, DI registration, Essentials coverage, public-API file conventions |
| `Maui.Tizen.DevFlow.Tests` | DevFlow API contract, capability/privilege policy, native-element registry, connection descriptors |
| `Maui.Tizen.Consumer.Tests` | Consumer package restore, MAUI.Sherpa handoff contract |

## Why the suites run as binaries, not `dotnet test`

The .NET 10+ SDK removed VSTest support for Microsoft.Testing.Platform, which is what xunit v3 uses:

```
error: Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
       on .NET 10 SDK and later.
```

Opting into the Microsoft.Testing.Platform runner via `global.json` fixes the v3 suites and
simultaneously breaks `tests/UnitTests`, which is still xunit v2 on VSTest. Rather than force that
migration as a side effect of adding a validation lane, the v3 suites are executed directly — they
are self-hosting executables, so this is entirely ordinary.

`run-hosted-validation.sh` asks MSBuild for `TargetPath` rather than guessing an output path,
because the repository sets a custom `BaseOutputPath`. Results are written as TRX to
`artifacts/test-results/`.

See [blocker 6](blockers.md#6-two-test-frameworks-in-one-repository) for how this resolves.

## Things that look device-bound but are not

Pulling work into this lane was a deliberate goal, since the device lane is blocked indefinitely.

**The DevFlow API contract.** `src/Diagnostics/Maui.Tizen.DevFlow.Agent` cannot be compiled by
anyone. But DevFlow's own packages are plain `net10.0` assemblies, so the hosted lane loads them and
asserts that every member the Tizen agent overrides still exists with the expected signature. A
maui-labs change fails an ordinary PR instead of surfacing on a device months later.

**Agent decision logic.** Capability advertisement, privilege gating, platform identity and the
native-element registry live in `Maui.Tizen.DevFlow.Agent.Shared`, which has no Tizen references and
is fully unit-tested here.

**Dependency policy.** `Tizen.UIExtensions` cannot be restored on a host, but its *declared*
dependencies can be read straight from its nuspec. The rule is verified against the real published
package.

**Convention engines.** Mapper parity, DI registration and implementation coverage are exercised
against in-assembly fakes, including cases that must fail. Without that, a broken engine would be
indistinguishable from a passing one until the device lane first ran.

## Public API

Two different mechanisms, split by what can execute where:

- **The real gate** is `Microsoft.CodeAnalysis.PublicApiAnalyzers`, which runs during compilation of
  the Tizen target framework. It is blocked with everything else.
- **The hosted checks** cover the health of the tracking files: `Shipped`/`Unshipped` pairing,
  `#nullable enable` headers, no duplicates, no stray whitespace, and TFM-specific folders.

Note what is deliberately *not* asserted: ordinal sorting. The analyzer orders entries with its own
comparer and the imported dotnet/maui files follow it, so an ordinal-sort rule would flag the entire
inherited surface as broken for no benefit.

`UnshippedApiIsEmptyBeforeARelease` is release-only (`MAUI_TIZEN_RELEASE_VALIDATION=1`). The
imported baseline starts with hundreds of pending entries, so failing pull requests on it would be
pure noise; it matters at exactly one moment.

## Determinism

- `InvariantGlobalization` on every suite, so no culture drift between runners.
- `DotNetCli` scrubs telemetry, first-run and inherited MSBuild environment before invoking the CLI,
  so an inner build cannot pick up the outer build's context.
- Consumer tests use a per-workspace `NUGET_PACKAGES`. Without it, a package produced by one test
  resolves from the shared global cache in another test even when absent from the feed under test,
  turning a negative restore test into a false pass. That happened during development.
- Generated fixture projects ship empty `Directory.Build.*` and a CPM-disabling
  `Directory.Packages.props`, so they cannot inherit repository conventions and stop being consumer
  tests.
