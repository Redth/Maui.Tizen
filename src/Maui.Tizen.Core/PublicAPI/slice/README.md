# PublicAPI baselines for the ported Tizen slice

These files **replace** dotnet/maui's inherited `Microsoft.Maui.*` baselines under
`../net-tizen/`, which `eng/targets/TizenPackage.props` attaches to every project by convention.

## Why the swap is necessary

The inherited baselines describe ~3,270 members of a completely different assembly. This project
exports `Microsoft.Maui.Platforms.Tizen.*`. With `Microsoft.CodeAnalysis.PublicApiAnalyzers` also
referenced, leaving them attached makes the real product build fail twice over: **RS0017** for
every inherited entry that does not exist here, and **RS0016** for every type this assembly does
export.

The inherited files are deliberately kept on disk: they are the imported baseline for the sources
that have not been ported yet, and the API-comparison tooling in `eng/api-baselines/` consumes
them. They are simply detached from the analyzer for this project.

## How these were generated

Not by hand. They are the output of the analyzer's own code fix, applied to the real compiled
assembly:

```bash
dotnet format analyzers tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj \
  --diagnostics RS0016 --severity warn
```

`tests/Maui.Tizen.Core.RefPackCompile` compiles the exact product sources against the real TizenFX
reference assemblies, so the entries are the genuine emitted surface - not a transcription that no
build could verify. RS0016 is **not** suppressed anywhere.

## What is enforced

The analyzer runs over the real product sources in the ref-pack lane on every CI run, with
**RS0016**, **RS0017** and **RS0036** all active. `ProjectEvaluationTests` additionally pins that
the inherited baselines stay detached, and `PublicApiBaselineTests` pins the content of these files
against the compiled assembly so a public API change cannot land without updating them.

The sample's API lives in `samples/Maui.Tizen.Sample/PublicAPI/`, kept separate so this file
describes only what the package would actually ship.

## Regenerating

Re-run the command above after any intentional public API change, then review the diff - an
unexpected entry is the signal this baseline exists to give you.
