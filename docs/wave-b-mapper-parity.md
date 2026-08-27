# Wave B — mapper parity and native behaviour

Wave B covers the standalone content, container, image, shape, graphics, scroll, refresh, swipe
and indicator handlers of the Tizen backend.

> **Parity is measured against a real Controls host.** `ConfigureControls` contributes mapper keys
> through the app builder, and not every remap is static-constructor driven, so reading mapper state
> without building a host under-reports it: `ViewHandler.ViewMapper` has 29 keys before the host is
> built and 36 after. Two genuine gaps — `StrokeDashArray` on every shape handler and `IconColor` on
> the swipe menu item — were invisible until the host was introduced.

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

## Keys contributed by Microsoft.Maui.Controls

Some keys exist only because `ConfigureControls` adds them to the **neutral** handler's mapper. Wave B
handlers do not chain the neutral mappers, so these have to be re-declared or they silently do
nothing:

| Key | Declared on | Wave B |
|---|---|---|
| `StrokeDashArray` | `ShapeViewHandler.Mapper` | `TizenShapeViewHandler.MapStrokeDashArray` invalidates the shape, matching upstream `Shape.Tizen.cs`. Inherited by every shape handler through chaining. |
| `IconColor` | `SwipeItemMenuItemHandler.Mapper` | Mapped to a documented no-op (see below). |

Neither can be written with `nameof`: there is no `IShapeView.StrokeDashArray` and no
`ISwipeItemMenuItem.IconColor`. They are declared as named constants instead. An earlier revision
wrote `nameof(ISwipeItemMenuItem.IconColor)`, failed to compile, and concluded the key did not
exist — it does; only the Core interface member does not.

## Dispatch, not just key presence

`MapperDispatchTests` **invokes** every reachable key against a handler that implements
`IViewHandler` and derives from no built-in handler — the position every Tizen handler is in.

This targets a failure key-presence parity cannot see. `PropertyMapper<TVirtualView, TViewHandler>.Add`
wraps each mapping in a closure that performs `(TViewHandler)h`, guarded **only** by a check on the
virtual view type. When `TViewHandler` is a concrete built-in handler, any other handler reaching
that key through chaining throws `InvalidCastException` at runtime while every key-presence
assertion still passes.

Such mappings really do exist in MAUI — 28 of them, on `ApplicationHandler`, `PickerHandler`,
`ProgressBarHandler`, `StepperHandler` and `CarouselViewHandler`. **Wave B chains none of them**:
it chains only `ViewMapper` and `ElementMapper`, whose every entry is `Action<IViewHandler, IView>`.
A test asserts that and fails if it ever stops being true.

`CarouselViewHandler` being on that list is a direct warning for Wave C, which owns it.

## Inherited keys that silently do nothing

Dispatching a key proves it does not crash. It does not prove it does anything. `InertMapperTests`
reads the IL of every mapper target reachable through `ViewMapper` and flags the ones whose body is
a bare `ret`.

Eleven are, and the cause is structural rather than an oversight in this port: **MAUI 11 ships no
Tizen target framework**, so this repository consumes the neutral `net11.0` assembly, in which
`PlatformView` is `object` and the platform half of each mapper does not exist.

| Keys | Status |
|---|---|
| `TranslationX`, `TranslationY`, `Scale`, `ScaleX`, `ScaleY`, `Rotation`, `RotationX`, `RotationY`, `AnchorX`, `AnchorY` | **Regression against upstream.** Upstream's Tizen build routed all ten through `TransformationExtensions.UpdateTransformation`, which really did move, scale and rotate the NUI view. Today they do nothing. |
| `ToolTip` | Not a regression — upstream's own Tizen `UpdateToolTip` is an empty body, so Tizen has never shown tooltips. |

**`ViewMapper` is chained by core, Wave A and Wave B alike, so this is not a Wave B defect and the
fix does not belong in a single wave.** It belongs in the shared Tizen view handler, where one
implementation serves every handler in the repository. Raised for core rather than patched here,
because duplicating it per wave would guarantee a conflict the moment core does it properly.

The test records the current set rather than merely asserting it is empty, so a *new* inert key —
MAUI moving something else behind a platform guard — fails the build instead of quietly joining the
list.

## Intentional no-ops

A mapper with an empty body is treated as a deliberate no-op. `MapperParityTests.EveryNoOpMapperDocumentsWhy`
fails the build if one of them lacks a documented justification in its XML doc comment, so this list
cannot silently grow.

| Handler | Key | Why |
|---|---|---|
| `TizenBorderHandler` | `Shape`, `Stroke`, `StrokeThickness`, `StrokeLineCap`, `StrokeLineJoin`, `StrokeDashPattern`, `StrokeDashOffset`, `StrokeMiterLimit` | **Unsupported, not merely unimplemented.** Upstream drew border strokes on the container `WrapperView`. This backend cannot create a container — MAUI exposes no settable container hook to an out-of-repo assembly, so `TizenViewHandler` pins `NeedsContainer` to `false`. Border strokes do not render. See `docs/net11-status.md`. |
| `TizenSwipeItemMenuItemHandler` | `IconColor` | The icon is a plain image view with no tint or colour-filter API. Upstream's Tizen backend supplies no implementation either. Mapped explicitly so the gap is documented rather than silent. |
| `TizenImageButtonHandler` | `Padding` | The Tizen image button draws its image edge to edge and exposes no content-inset API. Carried over from dotnet/maui. |
| `TizenRefreshViewHandler` | `IsRefreshEnabled` | Tizen's `RefreshLayout` cannot disable the pull gesture while leaving the control enabled. Disabling the whole view via `IsEnabled` still works. |
| `TizenSwipeItemMenuItemHandler` | `CharacterSpacing` | The swipe menu button renders its label through a fixed style with no per-character tracking. |
| `TizenSwipeItemMenuItemHandler` | `Font` | The swipe menu button does not expose the font family/size/slant of its embedded label. |
| `TizenSwipeViewHandler` | `LeftItems`, `TopItems`, `RightItems`, `BottomItems` | `MauiSwipeView` reads the item collections directly from the virtual view when a swipe begins, so there is no native state to push on change. |

## Other unsupported native behaviour

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
