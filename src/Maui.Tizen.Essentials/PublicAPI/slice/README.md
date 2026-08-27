# PublicAPI baselines for the ported Tizen Essentials backend

These files are this project's real API contract. They **replace** dotnet/maui's inherited
baselines under `../net-tizen/` for analyzer purposes, following the same convention as
`src/Maui.Tizen.Core/PublicAPI/slice/`.

## Why the swap is necessary

The inherited `../net-tizen/` baselines describe the whole of upstream's
`Microsoft.Maui.Essentials` assembly - the `Microsoft.Maui.ApplicationModel.*`,
`Microsoft.Maui.Devices.*` and `Microsoft.Maui.Storage.*` public surface. This assembly exports
none of that: it exports `Microsoft.Maui.Platforms.Tizen.Essentials.*` plus one
`Microsoft.Maui.Hosting` extensions class, and consumes the rest from the published
`Microsoft.Maui.Essentials` package.

Attaching the inherited files would fail the build twice over: **RS0017** for each of the 1,318
inherited entries that does not exist here, and **RS0016** for every type this assembly actually
does export.

The inherited files stay on disk untouched. They are the imported provenance record of what
upstream shipped for `net-tizen`, and the API-comparison tooling in `eng/api-baselines/` reads
them. They are simply detached from the analyzer for this project.

## How these were generated

Not by hand - by the analyzer's own code fix, applied to the real compiled assembly:

```bash
dotnet format analyzers tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj \
  --diagnostics RS0016 --severity warn
```

`tests/Maui.Tizen.Essentials.RefPackCompile` compiles the exact product sources against the real
`Samsung.Tizen.Ref.API15` reference assemblies, so every entry is the genuine emitted surface
rather than a transcription no build could verify.

That lane is also where the baseline is currently *enforced*: `src/Maui.Tizen.Essentials` cannot be
built at all until Samsung publishes the .NET 11 workload (`MAUITIZEN0001`), so without it the
contract would sit unchecked until that day. Verified by negative test - adding an undeclared
public member fails the lane with RS0016 under CI semantics.

`PublicAPI.Shipped.txt` is empty because nothing has shipped from this repository yet.
