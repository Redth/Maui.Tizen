# Contributing assets to a Tizen application

`Maui.Tizen.Build.Tasks` turns MAUI single-project resources into the inputs the Samsung workload
packs into a TPK. This document describes the one extension point another package needs in order to
contribute its own files, and the contract each side owns.

## The pipeline

```
MauiAsset            declared by the app, or contributed by a provider package
  -> MauiProcessedAsset   Resizetizer, via the public contract from dotnet/maui PR 36653
  -> TizenResource        Maui.Tizen.Build.Tasks, mapping Link onto TizenTpkFileName
  -> res/<path>           Samsung workload, packing TizenResource into the TPK
```

A `MauiAsset` whose `Link` (or `LogicalName`) is `wwwroot/index.html` is packed to
`res/wwwroot/index.html` inside the TPK.

## The extension point

Append a target name to `MauiTizenAssetProviderTargets`. It is guaranteed to run before Tizen asset
processing, in both the early and late Resizetizer opt-in orders:

```xml
<PropertyGroup>
  <MauiTizenAssetProviderTargets>
    $(MauiTizenAssetProviderTargets);
    MyPackageCollectAssets;
  </MauiTizenAssetProviderTargets>
</PropertyGroup>

<Target Name="MyPackageCollectAssets" DependsOnTargets="SomethingThatProducesMyFiles">
  <ItemGroup>
    <MauiAsset Include="@(MyFiles)" Link="%(MyFiles.TargetPath)" />
  </ItemGroup>
</Target>
```

Register through this property rather than hooking a Resizetizer target such as
`ResizetizeCollectItems` directly: the property is expanded at execution time, so it works
regardless of import order, and it does not couple your package to Resizetizer's internal target
names.

### Two things provider authors get wrong

**Asset file names may be fingerprinted.** In some configurations the SDK rewrites a static web
asset's name to include a content hash - `blazor.webview.js` is declared upstream as the relative
path `blazor.webview#[.{fingerprint}]?.js`. Pinning the unfingerprinted name produces an item that
looks right and resolves to nothing. Derive the name from the SDK rather than hard-coding it; for
static web assets that means letting `ComputeStaticWebAssetsTargetPaths` compute the target path.

**Drop precompressed variants.** The SDK emits `.gz` and `.br` alternatives alongside the original,
marked `AssetRole='Alternative'`. A Tizen application serves its assets from local storage, so those
are pure TPK bloat. Filter them unless `$(CompressionEnabled)` is explicitly true:

```xml
Condition="'$(CompressionEnabled)' != 'false' or '%(_Asset.AssetRole)' != 'Alternative' or ('%(_Asset.Extension)' != '.gz' and '%(_Asset.Extension)' != '.br')"
```

### Duplicates are handled here

Tizen resources are de-duplicated by destination path before packing, so two providers contributing
the same file cannot produce a duplicate TPK entry. Providers do not need to coordinate.

De-duplication compares destinations **ordinally**. Tizen is Linux: `Foo.js` and `foo.js` are two
different files, and an application may legitimately ship both. That rules out MSBuild's
`RemoveDuplicates` task and the `Distinct()` item function, which both compare with
`OrdinalIgnoreCase` and silently discarded one of the pair — a green build and a 404 on the device.
`Maui.Tizen.Build.Tasks.SelectDistinctTizenResources` does the comparison instead;
`DistinctWithCase()` is ordinal but strips metadata, so it cannot be used where the source path has
to survive.

### Prefer `Link` over `LogicalName` for case-distinct destinations

There is one case-sensitivity limitation this backend cannot fix, so it is written down rather than
left to be rediscovered.

`Microsoft.Maui.Resizetizer`'s `ProcessMauiAssets` normalizes `LogicalName` into `Link` through an
`ItemGroup` that **batches on `%(MauiAsset.LogicalName)`**, and MSBuild's metadata batching is
itself case insensitive. Two assets whose `LogicalName` values differ only in case therefore land in
one batch bucket and are given a single shared destination *before* any Tizen target sees them. By
the time they reach this backend they genuinely are duplicates, and they are collapsed correctly.

Setting `Link` directly avoids that path entirely and both files survive to the TPK:

```xml
<!-- Both of these reach the package. -->
<MauiAsset Include="wwwroot/Foo.js" Link="wwwroot/Foo.js" />
<MauiAsset Include="wwwroot/other.js" Link="wwwroot/foo.js" />
```

Covered by `TizenTargetsTests.ResourcesWhoseDestinationsDifferOnlyInCaseAreBothPackaged`.

## Ownership

| Concern | Owner |
|---|---|
| Producing `MauiAsset` items from format-specific inputs | the provider package |
| `MauiAsset` -> `MauiProcessedAsset` | `Microsoft.Maui.Resizetizer` |
| `MauiProcessedAsset` -> `TizenResource` and TPK layout | `Maui.Tizen.Build.Tasks` |
| Packing, signing and deploying the TPK | Samsung workload |

Resources are packaged through `TizenResource`; they are deliberately absent from `res.xml`, which
describes only the DPI-variant image buckets under `res/contents`. Assets are addressed by path, not
by screen density, so there is nothing for it to select between.

Each package ships its own half, so an application only ever references packages - there are no
globs or MSBuild snippets to copy into the app project.

## Worked example: Blazor static web assets

`Maui.Tizen.BlazorWebView` ships the provider for Blazor, in
`src/Maui.Tizen.BlazorWebView/buildTransitive/Maui.Tizen.BlazorWebView.targets`. It converts Razor
`StaticWebAsset` items into `MauiAsset` items using the SDK's own
`ComputeStaticWebAssetsTargetPaths` task, so that `wwwroot/**` content and the package-provided
`_framework/blazor.webview.js` both arrive at `res/wwwroot/...`, which is where
`TizenAssetFileProvider` serves from.

`tests/UnitTests/fixtures/BlazorAssetProvider.targets` is a **test fixture** that mimics it, so
`tests/UnitTests/TizenBlazorAssetHandoffTests.cs` can exercise this side of the boundary against
the real Razor SDK and the real WebView package without depending on another package's sources. It
is not a second implementation: where the two differ, the shipping one wins.

### Why the Blazor package has to ship it

`Microsoft.AspNetCore.Components.WebView.Maui` already contains an equivalent
`ConvertStaticWebAssetsToMauiAssets` target, but it ships under `build/`. NuGet applies `build/`
only to the project that references a package **directly**, so an application referencing
`Maui.Tizen.BlazorWebView` never receives it, and its web content silently never becomes an
application asset. Re-declaring the conversion in a `buildTransitive` file of the package the
application *does* reference is what closes that gap.
