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

### Duplicates are handled here

Tizen resources are de-duplicated by destination path before packing, so two providers contributing
the same file cannot produce a duplicate TPK entry. Providers do not need to coordinate.

## Ownership

| Concern | Owner |
|---|---|
| Producing `MauiAsset` items from format-specific inputs | the provider package |
| `MauiAsset` -> `MauiProcessedAsset` | `Microsoft.Maui.Resizetizer` |
| `MauiProcessedAsset` -> `TizenResource` and TPK layout | `Maui.Tizen.Build.Tasks` |
| Packing, signing and deploying the TPK | Samsung workload |

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
