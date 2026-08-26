# Wave A — common view behaviour and the simple controls

This is the second migration wave. The [core vertical slice](architecture.md) established the
application, window, layout and label path; Wave A adds the controls that sit inside it.

## What landed

Fourteen handlers, in `Microsoft.Maui.Platforms.Tizen.Handlers`:

| Control | Handler | Platform view |
|---|---|---|
| `IActivityIndicator` | `TizenActivityIndicatorHandler` | `TizenActivityIndicatorView` |
| `IButton` | `TizenButtonHandler` | `TizenButtonView` |
| `ICheckBox` | `TizenCheckBoxHandler` | `TizenCheckBoxView` |
| `IDatePicker` | `TizenDatePickerHandler` | `TizenPickerView` |
| `IEditor` | `TizenEditorHandler` | `TizenEditorView` |
| `IEntry` | `TizenEntryHandler` | `TizenEntryView` |
| `IPicker` | `TizenPickerHandler` | `TizenPickerView` |
| `IProgress` | `TizenProgressBarHandler` | `TizenProgressBarView` |
| `IRadioButton` | `TizenRadioButtonHandler` | `TizenRadioButtonView` |
| `ISearchBar` | `TizenSearchBarHandler` | `TizenSearchBarView` |
| `ISlider` | `TizenSliderHandler` | `TizenSliderView` |
| `IStepper` | `TizenStepperHandler` | `TizenStepperView` |
| `ISwitch` | `TizenSwitchHandler` | `TizenSwitchView` |
| `ITimePicker` | `TizenTimePickerHandler` | `TizenPickerView` |

Plus the supporting pieces those handlers need: a Tizen font manager
(`ITizenFontManager`/`TizenFontManager`), file and stream image sources
(`ITizenImageSourceService` and friends), the native controls Tizen does not provide
(`TizenSearchBarView`, `TizenStepperView`, `TizenPickerView`, `TizenDateTimePicker`) and the
per-control property mappings under `Platform/Tizen/Tizen*Extensions.cs`.

Registration is explicit:

```csharp
builder.ConfigureMauiHandlers(handlers => handlers.AddTizenControlHandlers());
builder.Services.AddTizenControlServices();
builder.ConfigureImageSources(sources => sources.AddTizenImageSources());
```

`AddTizenControlHandlers` is separate from the core slice's `AddTizenHandlers` so a host can
adopt either half independently while the migration is in flight.

## Mapper completeness

Every handler's mapper is a **complete** replacement for MAUI's equivalent — see
[`mapper-parity-matrix.md`](mapper-parity-matrix.md), which is generated from the shipped
mappers rather than maintained by hand. There are no `MISSING` entries.

"Complete" includes properties Tizen cannot honour. Those are mapped to a method with an empty
body and a `<remarks>` block saying why, rather than being left out of the mapper. The
difference matters: an absent key means an application that replaces the mapping silently gets
nothing, whereas a present no-op is discoverable, documented, and can be overridden.

The one deliberate omission is the obsolete `IBorder.Border` key. MAUI marks the property
`[Obsolete]` and states it will be removed; border rendering is driven by the stroke and shape
properties that replaced it.

### Properties Tizen cannot honour

| Property | Why |
|---|---|
| `IView.AutomationId` | NUI's `View` has no automation identifier. |
| `IView.Semantics` | NUI exposes no equivalent accessibility surface. |
| `IView.FlowDirection` | NUI resolves direction from the platform locale; no per-view override. |
| `IView.MaximumWidth`/`MaximumHeight` | NUI's `MaximumSize` is not honoured by the layout pass, so writing it mis-arranges the view rather than clamping it. |
| `IToolTipElement.ToolTip` | Tizen has no tooltip presenter. |
| `IContextFlyoutElement.ContextFlyout` | Tizen has no context-menu concept. |
| `ITextInput.IsSpellCheckEnabled` | Tizen's IME has no spell-check toggle independent of text prediction. |
| `IEntry.ClearButtonVisibility` | NUI's entry has no clear affordance and offers no drawing surface inside the control. |
| `ISearchBar.CancelButtonColor` | The Tizen search bar has no cancel affordance to tint. |
| `ISearchBar.SearchIconColor` | The icon is drawn by `TizenSearchBarView`; tinting needs a public property on that drawable. |
| `IPicker`/`IDatePicker`/`ITimePicker` `IsOpen` | Declared `internal` by MAUI, so it cannot be read from out of tree, and Tizen's dialogs have no programmatic dismiss. |
| `IRadioButton` text/`IsChecked` | Both are expressed by the templated content, which MAUI re-renders itself; there is no separate native indicator. |

### Behaviour improved over the upstream Tizen backend

Not everything was ported verbatim. These were fixed because the verification lanes made the
defects visible:

- **`Tizen.UIExtensions.NUI.Entry` already implements `IMeasurable`, sealed.** Upstream carries a
  `MauiEntry`/`MauiEditor` subclass that overrides `Measure` as a "workaround". Because the base
  implementation is `final`, that override is never reached through the interface, so the
  workaround has no effect. Removed.
- **`EditorExtensions.UpdateVerticalTextAlignment` read `HorizontalTextAlignment`** upstream.
  Now reads the vertical one.
- **Slider bounds** are written as a pair, so mapper key order is no longer load-bearing when
  the new range does not overlap the old one.
- **`MaxLength`, `CursorPosition` and `SelectionLength`** are clamped to the current text, so a
  stale value from a previous string cannot throw from inside a property mapper.
- **`Stepper` value changes** no longer raise a change notification when the clamped value is
  unchanged, so holding the button at a bound stops feeding redundant writes back through MAUI.
- **`Button.Padding` and `SearchBar.ReturnType`** are implemented; upstream leaves both as
  `[MissingMapper]` no-ops.
- **`DatePicker.MinimumDate`/`MaximumDate`** are enforced by clamping on display and on dialog
  accept. Upstream leaves them unimplemented; Tizen's dialog cannot express a range, so the
  limits have to be applied around it.
- **`Window.Instance`** (deprecated in API12) replaced with `Window.Default`.

## MAUI extensibility blockers

Two gaps in MAUI's public surface shaped this wave. Both are additions to the list in
[`net11-status.md`](net11-status.md).

1. **`ViewHandler.ContainerView` has a `private protected` setter.** An out-of-repo backend
   cannot publish a container view it constructs in `SetupContainer`, and
   `IElement.ToPlatform(IMauiContext)` returns `IViewHandler.ContainerView` when one exists — so
   a third-party container is invisible to MAUI's own view-realisation path. The core slice
   resolves this by rendering background, clip and shadow directly onto the platform view
   (`NeedsContainer => false`); Wave A inherits that decision. The cost is that gradient and
   image backgrounds, clip shapes and shadows are limited to what NUI can paint on the view
   itself.

2. **`ImageSourcePaint` is `internal`.** MAUI models an image background with an internal paint
   type, so a backend cannot detect one. `IView.Background` carrying an image therefore renders
   nothing rather than the image. `TizenPlatformExtensions.UpdateBackgroundImageSourceAsync`
   exists and works; it simply cannot be reached from the `Background` mapping.

Also worth recording: `IFontManager` and `IImageSourceService` are marker-only in the neutral
assembly — the members that actually resolve a font or load an image exist solely in each
platform's own build of MAUI. Wave A declares `ITizenFontManager` and `ITizenImageSourceService`
to fill that in, which is why a host must call `AddTizenControlServices`.

## Validation

The Samsung .NET 11 workload is still unpublished (`eng/baselines.json` →
`target.workloadManifest.status == "unavailable"`), so `net11.0-tizen11.0` cannot be restored or
built by anyone, and no device or emulator test can run. Wave A is validated by the two
workload-free lanes the core slice established:

| Lane | What it proves | Status |
|---|---|---|
| `tests/Maui.Tizen.Core.RefPackCompile` | Every `#if TIZEN` branch type-checks against the real `Samsung.Tizen.Ref.API15` reference assemblies. | passing |
| `tests/Maui.Tizen.Core.UnitTests` | Mappers, command dispatch, DI registration and the naming/collision rules are executed against host stand-ins. | passing |

Exact blockers, unchanged by this wave:

- `samsung.net.sdk.tizen.manifest-11.0.100` is not published; only the `9.0.100` and `10.0.100`
  bands exist. Until it is, `dotnet restore` on any `net11.0-tizen11.0` project fails.
- Consequently there is no device or emulator coverage for any of this: no NUI main loop runs in
  either lane, so rendering, focus behaviour, IME interaction and dialog dismissal are verified
  by construction and review only.
- `Tizen.UIExtensions.NUI` 0.9.2 still declares a .NET 6-era `Microsoft.Maui.Graphics`
  dependency, so packing remains blocked by `MAUITIZEN0101`.

## Not in this wave

Image, graphics-view, border/shape, scroll, collection, navigation, shell and gesture handlers,
and the modal navigation stack. `ITizenModalHost` is the seam the navigation wave should
implement so the picker dialogs join the back stack; until then `TizenDirectModalHost` opens
them directly on the window.
