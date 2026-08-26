# PublicAPI baselines for the ported Tizen slice

These files **replace** dotnet/maui's inherited `Microsoft.Maui.*` baselines under
`../net-tizen/`, which `eng/targets/TizenPackage.props` attaches to every project by convention.

## Why the swap is necessary

The inherited baselines describe ~3,270 members of a completely different assembly. This project
exports `Microsoft.Maui.Platforms.Tizen.*`. With `Microsoft.CodeAnalysis.PublicApiAnalyzers` also
referenced, leaving them attached makes the real product build fail twice over:

* **RS0017** for every one of the ~3,270 inherited entries, none of which exist here.
* **RS0016** for every type this assembly actually does export.

Neither diagnostic would say anything useful about a genuine API change, so the baselines would be
noise that has to be suppressed wholesale - which is worse than having none.

The inherited files are deliberately kept on disk: they are the imported baseline for the sources
that have not been ported yet, and the API-comparison tooling in `eng/api-baselines/` consumes
them. They are simply detached from the analyzer for this project.

## Why these files are empty, and what is still enforced

`RS0016` (undeclared public symbol) is suppressed, with the reason recorded in both
`Maui.Tizen.Core.csproj` and `tests/Maui.Tizen.Core.RefPackCompile`. The assembly cannot be built
until Samsung publishes the .NET 11 Tizen workload, so the analyzer's own code fix cannot enumerate
its public surface. Hand-writing ~1,000 signatures that no build can verify would produce a
baseline nobody should trust - a worse outcome than an explicitly empty one.

**`RS0017` is left enabled**, and that is the check that matters today: it fires immediately if the
inherited baselines are re-attached by mistake, which is the actual defect this directory exists to
prevent.

## What to do when the workload ships

Run the analyzer code fix (`Add to public API`) over `src/Maui.Tizen.Core`, remove the `RS0016`
suppression from both projects, and commit the populated files. See `docs/net11-status.md`.
