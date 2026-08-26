# Wave B — handler migration

Wave B migrates the standalone content/container, image, shape, graphics, scroll, refresh, swipe
and indicator handlers from the raw imported upstream sources into handlers that can actually work
in this repository.

For mapper-by-mapper detail see [`wave-b-mapper-parity.md`](wave-b-mapper-parity.md).

## What changed, and why it had to

The import brought the upstream files across unmodified. Those files are *halves* of `partial`
classes whose other half lives in `Microsoft.Maui.Core` — and, as established in the parity
document, **MAUI 11 ships no Tizen target framework**, so the other half no longer exists on any
target this repository can build.

Each Wave B file therefore became a standalone, `Tizen`-prefixed handler that declares its own
property and command mappers and its own constructor triple. Renames were done with `git mv` and the
diff is rename-plus-edit, so `git log --follow` still reaches the Xamarin.Forms-era history.

Per `PROVENANCE.md`, removing the now-redundant `.Tizen.cs` suffix was explicitly left to the
handler workstream; migrated files drop it, and a test enforces that they do.

### Handlers migrated

| Area | Handlers |
|---|---|
| Core content/container | `TizenContentViewHandler`, `TizenBorderHandler`, `TizenSwipeItemViewHandler` |
| Core scrolling/refresh | `TizenScrollViewHandler`, `TizenRefreshViewHandler` |
| Core image | `TizenImageHandler`, `TizenImageButtonHandler` |
| Core drawing | `TizenGraphicsViewHandler`, `TizenShapeViewHandler` |
| Core swipe/indicator | `TizenSwipeViewHandler`, `TizenSwipeItemMenuItemHandler`, `TizenIndicatorViewHandler` |
| Controls shapes | `TizenBoxViewHandler`, `TizenLineHandler`, `TizenPathHandler`, `TizenPolygonHandler`, `TizenPolylineHandler`, `TizenRectangleHandler`, `TizenRoundRectangleHandler` |

### Image source services

`IImageSourceService` declared its Tizen `GetImageAsync` overload under `#if TIZEN` upstream, so the
neutral interface in MAUI 11 has no Tizen member at all. Wave B introduces
`ITizenImageSourceService`, its generic marker, and a `TizenImageSourceService` base, then migrates
the four services onto them: `TizenFileImageSourceService`, `TizenUriImageSourceService`,
`TizenStreamImageSourceService` and `TizenFontImageSourceService`.

`ImageSourcePartExtensions.UpdateSourceAsync` — the existing Tizen loading path, including the
`ResourceReady` await for image views — now resolves services through
`GetRequiredTizenImageSourceService`. Handlers use that path directly instead of MAUI's
`ImageSourcePartLoader`, whose platform setter has no Tizen shape in MAUI 11.

### Polygon and polyline point subscriptions

`UpdatePointsSubscription`/`ClearPointsSubscription` lived in the neutral half upstream and did not
come across with the import. They are reimplemented here: `PointCollection` is mutable, so the
handler has to redraw on `CollectionChanged`, not merely when `Points` is reassigned.

## Layout

No layout algorithm is reimplemented. Handlers assign `CrossPlatformMeasure`/`CrossPlatformArrange`
to the native containers and translate platform geometry only.

## Verification, and what is genuinely blocked

**The backend cannot be compiled here, and that is not a shortcut.** Two independent blockers:

1. **No Tizen platform SDK.** `dotnet workload install maui-tizen` succeeds, but that workload only
   supplies MAUI's Tizen packs. The `tizen` `TargetPlatformIdentifier` comes from Samsung's separate
   Tizen SDK workload, without which any `net*-tizen*` TFM fails with `NETSDK1139: The target
   platform identifier tizen was not recognized`. `Directory.Build.props` in the foundation records
   that no Samsung .NET 11 workload exists yet.
2. **`Tizen.UIExtensions.NUI` has no matching asset.** The published package supports
   `net6.0-tizen7.0` and `tizen10.0` only, so it cannot even be restored against a host TFM.

No device or emulator run was performed and none is claimed.

What *is* verified runs in `tests/Maui.Tizen.SourceTests` (12 tests, host-side, no Tizen SDK needed).
It parses the migrated sources with Roslyn and checks them against the **real MAUI assemblies via
reflection**, so the expectations are not transcribed by hand:

- every migrated source parses with zero syntax errors;
- no migrated type name collides with a public MAUI type name;
- every migrated handler is `Tizen`-prefixed and has dropped the `.Tizen.cs` suffix;
- no private reflection (`BindingFlags.NonPublic`, `UnsafeAccessor`, …);
- no `ProjectReference` into a dotnet/maui source tree;
- **every mapper key on the corresponding neutral MAUI handler is implemented or recorded as a gap**,
  accounting for keys inherited through mapper chaining;
- every empty mapper body has a documented justification;
- the committed parity manifest matches what the sources actually declare.

These tests found three real defects during development: two missing command mappers
(`new(...)` initialisers were not being read), and `ISwipeItemMenuItem.IconColor` having no mapper
at all. All three are fixed.

## Dependencies on predecessor waves

Wave B is stacked on the foundation import and assumes core/Wave A lands the shared platform
abstractions. Confirmed by reflection against `Microsoft.Maui.Core` 11.0.0-preview.7.26418.3:

| Symbol | Present in MAUI 11 neutral? | Owner |
|---|---|---|
| `ViewHandler.SetupContainer`, `NeedsContainer`, `ContainerView`, `PlatformArrange` | Yes | MAUI package |
| `ViewHandler<,>.CreatePlatformView` | Yes | MAUI package |
| `IPlatformViewHandler` | **No** | **core / Wave A must supply it** |
| `ContentViewGroup`, `MauiScrollView`, and the other `Platform/Tizen` helpers | **No** | this repository (already imported) |

Wave B references `IPlatformViewHandler` throughout, exactly as upstream did. Until core supplies
it, the backend has an unresolved symbol independent of the SDK blockers above.

One naming risk for core to settle: `Microsoft.Maui.Platform.WrapperView` **also exists in MAUI 11**,
and this repository's imported `Platform/Tizen/WrapperView.cs` declares the same full name. Within
this assembly ours wins, but consumers referencing both could hit ambiguity if both are public. That
is a Platform-layer decision and is deliberately not resolved by Wave B.

## Deliberately out of scope

- **`CarouselViewHandler`** is still `CarouselViewHandler.Tizen.cs`. It derives from
  `ItemsViewHandler<T>` and depends on `MauiCarouselView`, the items adaptors and the rest of the
  collection stack, which Wave C owns. Migrating it here would have meant migrating that hierarchy
  and colliding head-on with Wave C, so it is left for the wave that owns its dependencies.
- Simple controls (Wave A) and navigation/Shell/collection adapters (Wave C) are untouched.
- `Platform/Tizen` helper *contents* are untouched apart from the one-line image-source resolution
  change, to keep the diff reviewable and avoid churn against other waves.
