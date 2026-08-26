# Imported samples and test app heads

**These directories contain imported Tizen *assets*, not buildable projects.**

## What is here

The history import retained every path in `dotnet/maui` whose name mentions Tizen. For
samples and test applications that means the Tizen platform folders came across —
`tizen-manifest.xml`, `Platforms/Tizen/**`, splash and icon resources — but the projects
that consume them did not, because their filenames (`Maui.Controls.Sample.csproj`,
`Essentials.Sample.csproj`, and so on) contain no "tizen".

That is the filter behaving correctly: those project files are shared, multi-platform
MAUI projects that this repository does not own.

| Directory | Contents | Buildable |
|---|---|---|
| `samples/Controls/Controls.Sample` | `Platforms/Tizen` assets only | No — no project file |
| `samples/Controls/Controls.Sample.Sandbox` | `Platforms/Tizen` assets only | No — no project file |
| `samples/Essentials/Samples` | `Platforms/Tizen` assets only | No — no project file |
| `samples/Graphics/GraphicsTester.Skia.Tizen` | Full imported project | No — see below |
| `tests/Controls/ManualTests` | `Platforms/Tizen` assets only | No — no project file |
| `tests/Controls/TestCases.HostApp` | `Platforms/Tizen` assets only | No — no project file |

## Why `GraphicsTester.Skia.Tizen.csproj.orphan` is renamed

That one *did* have a Tizen-named project file, so it was imported — but it cannot load
here. It declares:

```xml
<TargetFramework>$(_MauiDotNetTfm)-tizen</TargetFramework>
<ProjectReference Include="..\..\src\Graphics.Skia\Graphics.Skia.csproj" />
<ProjectReference Include="..\GraphicsTester.Portable\GraphicsTester.Portable.csproj" />
```

`$(_MauiDotNetTfm)` is a dotnet/maui property that does not exist in this repository, so
the target framework evaluates to the malformed `-tizen`; and both project references
point at projects that were never imported.

Left as `.csproj`, a folder-level `dotnet build` or an IDE that scans for projects would
load it and fail with confusing errors that have nothing to do with this repository. The
`.orphan` suffix keeps the file and its history intact while making it invisible to
project discovery.

An invariant test asserts that every `.csproj` in the repository is referenced by
`Maui.Tizen.slnx`, so a future orphan cannot slip in unnoticed.

## Disposition

These are recorded in the source disposition manifest under
[`eng/manifests/`](../eng/manifests/). Restoring any of them to working order means
authoring a project file here — a Phase 5 task (samples and device tests), not a
foundation one.

To rebuild one, take the missing project file from the pinned baseline:

```bash
git show upstream/9.0.120 -- src/Graphics/samples/GraphicsTester.Skia/
```
