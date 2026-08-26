# Wave B — mapper parity and native behaviour

Wave B covers the standalone content, container, image, shape, graphics, scroll, refresh, swipe
and indicator handlers of the Tizen backend.

The machine-readable companion to this document is
[`wave-b-mapper-parity.json`](wave-b-mapper-parity.json). **It is generated, not hand-maintained.**
`Maui.Tizen.SourceTests` rebuilds it from the migrated sources plus reflection over the real MAUI
assemblies and fails if the committed copy has drifted:

```bash
dotnet test tests/Maui.Tizen.SourceTests                          # verify
MAUI_TIZEN_UPDATE_PARITY=1 dotnet test tests/Maui.Tizen.SourceTests  # regenerate
```

## Why these handlers are named `Tizen*`

Upstream, each of these files was the Tizen half of a `partial` class such as
`Microsoft.Maui.Handlers.ScrollViewHandler`. That arrangement no longer works here, for a reason
worth stating plainly:

**.NET MAUI 11 ships no Tizen target framework.** `Microsoft.Maui.Core` 11.x contains
`net11.0`, `net11.0-android37.0`, `net11.0-ios26.5`, `net11.0-maccatalyst26.5`,
`net11.0-windows*` and `netstandard*` — and nothing for Tizen. A `partial` declaration cannot span
assemblies, so the Tizen half can never be reunited with its neutral half.

The neutral handler names, however, *do* still exist in `Microsoft.Maui.Core`. Declaring
`Microsoft.Maui.Handlers.ScrollViewHandler` here would produce two types with the same full name in
any app referencing both assemblies. Every migrated handler is therefore a standalone type with a
`Tizen` prefix that owns its own property and command mappers, declared in the
`Microsoft.Maui.Platforms.Tizen.Handlers` namespace and deriving from the core vertical slice's
`TizenViewHandler<TVirtualView, TPlatformView>`.

`SourceIntegrityTests.MigratedHandlerNamesDoNotCollideWithNeutralMauiTypes` enforces this by
reflecting over the shipped MAUI assemblies rather than trusting a hard-coded list, and
`MigratedTypesLiveInTheReservedTizenNamespace` enforces the namespace contract.

## Parity summary

Every mapper key declared by the corresponding neutral MAUI handler is accounted for. Keys supplied
by the shared `ViewMapper`/`ElementMapper`, and by a migrated base handler's mapper (the shape
handlers chain `TizenShapeViewHandler.Mapper`), are inherited through mapper chaining and are not
re-declared.

| Handler | Property mappers | Command mappers | Unmapped neutral keys |
|---|---:|---:|---:|
| `TizenBorderHandler` | 10 (8 unsupported, see below) | 0 | 0 |
| `TizenGraphicsViewHandler` | 3 | 1 | 0 |
| `TizenImageHandler` | 4 | 0 | 0 |
| `TizenImageButtonHandler` | 7 | 0 | 0 |
| `TizenIndicatorViewHandler` | 8 | 0 | 0 |
| `TizenRefreshViewHandler` | 5 | 0 | 0 |
| `TizenScrollViewHandler` | 4 | 1 | 0 |
| `TizenShapeViewHandler` | 10 | 0 | 0 |
| `TizenSwipeViewHandler` | 7 | 2 | 0 |
| `TizenSwipeItemViewHandler` | 2 | 0 | 0 |
| `TizenSwipeItemMenuItemHandler` | 8 | 0 | 0 |
| `TizenBoxViewHandler` | inherited | inherited | 0 |
| `TizenLineHandler` | 4 | 0 | 0 |
| `TizenPathHandler` | 2 | 0 | 0 |
| `TizenPolygonHandler` | 2 | 0 | 0 |
| `TizenPolylineHandler` | 2 | 0 | 0 |
| `TizenRectangleHandler` | 2 | 0 | 0 |
| `TizenRoundRectangleHandler` | 1 | 0 | 0 |

## Intentional no-ops

A mapper with an empty body is treated as a deliberate no-op. `MapperParityTests.EveryNoOpMapperDocumentsWhy`
fails the build if one of them lacks a documented justification in its XML doc comment, so this list
cannot silently grow.

| Handler | Key | Why |
|---|---|---|
| `TizenBorderHandler` | `Shape`, `Stroke`, `StrokeThickness`, `StrokeLineCap`, `StrokeLineJoin`, `StrokeDashPattern`, `StrokeDashOffset`, `StrokeMiterLimit` | **Unsupported, not merely unimplemented.** Upstream drew border strokes on the container `WrapperView`. This backend cannot create a container — MAUI exposes no settable container hook to an out-of-repo assembly, so `TizenViewHandler` pins `NeedsContainer` to `false`. Border strokes do not render. See `docs/net11-status.md`. |
| `TizenImageButtonHandler` | `Padding` | The Tizen image button draws its image edge to edge and exposes no content-inset API. Carried over from dotnet/maui. |
| `TizenRefreshViewHandler` | `IsRefreshEnabled` | Tizen's `RefreshLayout` cannot disable the pull gesture while leaving the control enabled. Disabling the whole view via `IsEnabled` still works. |
| `TizenSwipeItemMenuItemHandler` | `CharacterSpacing` | The swipe menu button renders its label through a fixed style with no per-character tracking. |
| `TizenSwipeItemMenuItemHandler` | `Font` | The swipe menu button does not expose the font family/size/slant of its embedded label. |
| `TizenSwipeViewHandler` | `LeftItems`, `TopItems`, `RightItems`, `BottomItems` | `MauiSwipeView` reads the item collections directly from the virtual view when a swipe begins, so there is no native state to push on change. |

## Other unsupported native behaviour

- **`ISwipeItemMenuItem.IconColor` does not exist in MAUI 11.** A mapper had been written for it on
  the assumption that it did; compiling against the real assemblies proved otherwise and it was
  removed.
- **`TizenFontImageSourceService`** returns an empty `TizenImageSource`: Tizen has no glyph
  rasterisation path wired up, so font images render blank rather than throwing. Behaviour is
  unchanged from upstream.
- **Border strokes do not render at all** while containers are unavailable (see the table above).
  `ShapeView` is unaffected: it draws through Skia on its own platform view, so each stroke mapper
  runs a full `InvalidateShape` pass — Tizen has no incremental native stroke API.
- **Indicator appearance** (size, colours, shape) rebuilds the indicator set via `ResetIndicators`
  for the same reason.

## Measurement and arrangement

No layout algorithm is reimplemented. Handlers forward `CrossPlatformMeasure` and
`CrossPlatformArrange` to the native container and only translate platform geometry
(`ToDP`/`ToPixel`). `TizenScrollViewHandler` additionally syncs the native content container size
after arrange, which is scroll-extent bookkeeping rather than layout.
