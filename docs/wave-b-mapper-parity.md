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
`Maui.Tizen.SourceTests` rebuilds it from the migrated sources, the effective finalized
`TizenViewMappers` chain, and reflection over the real MAUI assemblies. It records inherited Tizen
implementations separately from explicit unsupported bodies and fails if the committed copy drifts:

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

Every mapper key declared by the corresponding neutral MAUI handler is accounted for. View handlers
chain `TizenViewMappers.ViewMapper` and `ViewCommandMapper`; shape handlers additionally chain
`TizenShapeViewHandler.Mapper`. The JSON includes these effective inherited entries instead of
reporting only directly-declared keys.

| Handler | Property mappers | Command mappers | Unmapped neutral keys |
|---|---:|---:|---:|
| `TizenBorderHandler` | 10 (8 unsupported, see below) | 0 | 0 |
| `TizenGraphicsViewHandler` | 2 | 1 | 0 |
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
| `IconColor` | `SwipeItemMenuItemHandler.Mapper` | `TizenSwipeItemMenuItemHandler.MapIconColor` tints `Button.Icon` through `ImageView.ImageColor`. |

Neither can be written with `nameof`: there is no `IShapeView.StrokeDashArray` and no
`ISwipeItemMenuItem.IconColor`. They are declared as named constants instead. An earlier revision
wrote `nameof(ISwipeItemMenuItem.IconColor)`, failed to compile, and concluded the key did not
exist — it does; only the Core interface member does not.

## Dispatch, not just key presence

`WaveBConcreteMapperTests` resolves every concrete Controls type through the production
`UseMauiAppTizenControls<TApp>` factory, creates the real Tizen handler source against host platform
stubs, and executes visibility, enabled state, opacity, background, transformations, sizing, input,
focus and invalidation mappings. This catches a neutral no-op chain or concrete-handler cast while
also proving the handler is reachable without manual registration.

## Inherited keys that silently do nothing

The effective manifest classifies empty production bodies as `Unsupported` and requires a reason.
The finalized shared mapper implements transformations and the other common view mappings. Remaining
unsupported entries are explicit platform/API gaps such as `ToolTip`, maximum size, the obsolete
`IBorder.Border` key and the unavailable external container hook.

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
| `TizenImageButtonHandler` | `Padding` | `View.Padding` does exist, so "no content-inset API" was imprecise — but it is the wrong tool: NUI padding insets a view's *children*, and an `ImageView` renders its image as a visual rather than a child, so writing it would move nothing while inflating the measured size. Insetting the image itself needs a container view, which this backend cannot create (`NeedsContainer` is pinned to `false`). Not verified on a device. |

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


## Corrected no-op claims

Three keys were previously recorded as unsupported no-ops on the strength of a platform limitation
that does not exist. The claims were checked against the TizenFX API15 reference assemblies and
found to be wrong; all three are now real mappings.

| Key | Claim that was wrong | What the platform actually offers |
|---|---|---|
| `IconColor` | "the icon is a plain image view with no tint or colour-filter API" | `Tizen.NUI.Components.Button.Icon` is an `ImageView`, and `ImageView.ImageColor` (a `Tizen.NUI.Color`) multiplies the image — exactly a tint. |
| `CharacterSpacing` | "a fixed style with no per-character tracking" | `Button.TextLabel.CharacterSpacing` exists, and the core slice already drives it for `IButton` via `UpdateCharacterSpacing`. |
| `Font` | "does not expose the font family/size/slant of its embedded label" | `Tizen.UIExtensions.NUI.Button` exposes `FontFamily`, `FontSize` and `FontAttributes`; the core slice drives them via `UpdateTizenFont`. |

In each case upstream's Tizen backend marked the mapper `[MissingMapper]` — that is, *not yet
implemented*. Carrying those forward as "unsupported" restated an upstream gap as a platform
limitation, which is the more damaging error: an unimplemented mapper invites a fix, whereas one
documented as unsupported does not.

`IconColor` deserves one note on null handling. `ImageColor` multiplies, so white is the identity:
an unset colour resets the tint to white rather than to transparent, which would erase the icon.

### Note on the colour-conversion helpers

`ImageColor` takes a `Tizen.NUI.Color`, and neither available `ToTizen` helper returns one —
`Tizen.UIExtensions.NUI`'s and the core slice's both return `Tizen.UIExtensions.Common.Color`. The
names are actively misleading here: `TizenPlatformExtensions.cs` aliases `NColor` to the
*UIExtensions* type, while Wave B files alias the same name to `Tizen.NUI.Color`. The conversion is
therefore written out rather than routed through a helper.

## Effective keys versus declared keys

A handler answers far more keys than it declares, because mappers chain. All seven concrete shape
handlers answer `StrokeDashArray` without one of them declaring it: six chain
`TizenShapeViewHandler.Mapper` explicitly and `TizenBoxViewHandler` inherits it. A report that
counts only directly-declared keys therefore shows seven gaps where there are none.

`Ellipse` is the eighth concrete Controls shape and resolves directly to
`TizenShapeViewHandler`; it needs no specialized subclass.

`EffectiveMapperTests` resolves each key to the Tizen handler and method that will actually run,
nearest declaration first, matching `PropertyMapper` lookup. It asserts that:

- every concrete shape handler resolves `StrokeDashArray` to `TizenShapeViewHandler.MapStrokeDashArray`, and that it is not a no-op;
- `IconColor` resolves to a real body;
- **no Wave B mapper chains a neutral MAUI *concrete* handler's mapper.**

That last one is the crash-safety invariant.
`PropertyMapper<TVirtualView, TViewHandler>.Add` wraps every mapping in a closure that casts the
handler to `TViewHandler`, guarded only by the virtual-view type. When `TViewHandler` is a concrete
upstream class such as `LineHandler`, dispatching that key onto a Tizen handler throws
`InvalidCastException` — and such keys are typically reachable only through chaining, so nothing in
the source names them. Wave B view handlers chain finalized `TizenViewMappers`; the swipe menu item
chains only the interface-typed element mapper; and shapes chain `TizenShapeViewHandler.Mapper`.
Each of these assertions was mutation-tested: removing the base `StrokeDashArray`
mapping fails all seven shape tests, and pointing one handler at neutral `LineHandler.Mapper` fails
the invariant.
