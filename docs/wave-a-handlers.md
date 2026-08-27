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

## Handler identity and mapper composition

Each handler implements **MAUI's real handler interface** - `IButtonHandler`, `IEntryHandler`,
`ISearchBarHandler` and so on - not a backend-only `ITizen*Handler`. That is what makes the
backend substitutable for MAUI's own handler: Controls' remapped mappings hard-cast the handler to
the interface they were declared against, so a backend-only interface would throw
`InvalidCastException` the moment a chained mapping ran.

This is possible because **MAUI ships no Tizen asset**. `Microsoft.Maui.Core` publishes
`net11.0`, `-android`, `-ios`, `-maccatalyst` and `-windows`; a `net11.0-tizen11.0` project
therefore resolves the neutral `net11.0` assembly, where every handler interface types
`PlatformView` as `object`. The per-TFM alias mismatch that would otherwise cause CS9333 simply
does not arise, so the interfaces can be implemented explicitly. `ImageSourcePartLoader` also
became public in MAUI 11, which is what unblocked `IButtonHandler` specifically.

Each mapper is then composed in three layers, lowest precedence first:

1. **MAUI's static `XHandler.Mapper`**, chained. This is what carries Controls' `RemapForControls`
   additions - `FormattedText`, `TextType`, `LineBreakMode`, `MaxLines`, `TextTransform`,
   `CheckBox.Color` and the accessibility keys. Chaining is *live* rather than a snapshot, so a
   mapper built before the remap still picks it up.
2. **The Tizen view mappings**, re-applied over the top. Chaining MAUI's mapper also inherits its
   *bodies*, which in the neutral assembly are the `Standard` no-ops. Without this layer every
   common view property would resolve and do nothing.
3. **The handler's own keys**, which must win - `Entry.Background` re-evaluates the container
   before painting, so its override has to sit above the generic implementation.

`TizenHandlerMappers.Chain` builds layers 1-2; the handler's initializer supplies layer 3.
`TizenHandlerMapperTests` pins all of it, including that every chained mapping dispatches without
a cast failure.

### Chaining alone is not enough: every chained key must be overridden

MAUI's static mappers are *declared* as `IPropertyMapper<IView, IXHandler>` but *constructed* as
`PropertyMapper<IView, XHandler>` - closed over MAUI's **concrete** handler:

```csharp
public static IPropertyMapper<IPicker, IPickerHandler> Mapper =
    new PropertyMapper<IPicker, PickerHandler>(ViewMapper) { ... };
```

`PropertyMapper<TVirtualView, TViewHandler>.Add` dispatches through a hard `(TViewHandler)h` cast,
so a key that is only reachable *through the chain* throws `InvalidCastException` the moment it is
dispatched to a handler that is not MAUI's own. Layer 3 is therefore not a stylistic preference:
**every key a chained mapper contributes has to be overridden here**, or it is worse than missing -
it is a runtime crash on a property set.

This was found by the per-key dispatch test rather than by reading, and it had actually bitten:
`Picker.ItemsSource` and `Stepper.Increment`, both added by `RemapForControls`, threw until they
were given Tizen bodies (each mirrors what Controls does - forwarding to `IPicker.Items` and
`IStepper.Interval` respectively). `EveryChainedMappingInvokesWithoutCastFailure` enumerates every
key of every Wave A mapper and invokes it, so a future MAUI package that adds a key fails there
rather than on a device.

## Mapper completeness

Every handler's mapper is a **complete** replacement for MAUI's equivalent — see
[`mapper-parity-matrix.md`](mapper-parity-matrix.md), which is generated from the shipped
mappers rather than maintained by hand. There are no `MISSING` entries.

"Complete" includes properties Tizen cannot honour. Those are mapped to a method with an empty
body and a `<remarks>` block saying why, rather than being left out of the mapper. The
difference matters: an absent key means an application that replaces the mapping silently gets
nothing, whereas a present no-op is discoverable, documented, and can be overridden.

Two keys are deliberately excluded, both inherited from the core slice's base mapper and reported
as `excluded` rather than `MISSING` in the matrix:

- `Border` - the obsolete `IBorder.Border` mapping. MAUI marks the property `[Obsolete]` and
  states it will be removed; border rendering is driven by the stroke and shape properties that
  replaced it.
- `ContainerView` - cannot be honoured at all, because `ViewHandler.ContainerView` has a
  `private protected` setter. See the extensibility blockers below.

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

### Fixes from code review

A second pass found seven further defects, all of which shared the property of leaving the build
green while misbehaving at runtime:

- **Stepper bounds were applied one property at a time.** MAUI drives mapper keys in declaration
  order, so `Minimum` landed before `Maximum` and `Value`. For min 5 / max 30 / value 25 that
  clamped the value into `[5, 10]` - 10 being the native default maximum - and reported a change,
  which the handler wrote back onto the virtual view, destroying the application's bound value
  before the real one was applied. A minimum above 10 was worse: `Math.Clamp` threw
  `ArgumentException` from inside a property mapper. Bounds and value are now applied atomically
  by `TizenStepperRange`, which is platform-independent and therefore directly unit tested.
- **`ConfigureAwait(false)` before touching the UI.** `TizenImageSource`, Button, Slider, Picker,
  DatePicker and TimePicker resumed on a thread-pool thread and then touched NUI or wrote the
  virtual view - the latter re-enters MAUI's property system, which runs the mapper and touches
  NUI in turn. Continuations are now marshalled back through `TizenDispatchExtensions`, and a
  source-level test keeps the pattern from returning.
- **Image loads had no lifecycle.** No supersession, so a slow earlier load could overwrite a
  newer one; no source or view identity check; no clearing on failure; and the service result -
  which owns a native image buffer - was never disposed on replacement or disconnect.
  `TizenImageLoader<T>` now owns all of it, with regressions for each case.
- **Editor and SearchBar never proxied cursor or selection events.** Only Entry did, so moving the
  caret in an editor left MAUI believing it was still wherever it was last told, and the next
  programmatic edit landed at the wrong offset. NUI reports selections in drag order, so a
  right-to-left drag produced a negative length; `TizenTextSelection` normalises it.
- **Composite controls swallowed focus.** SearchBar and Stepper are groups; the group draws no
  caret and accepts no input, so focus requests appeared to succeed while doing nothing, and child
  focus was never reflected onto the virtual view. Both now forward to the interactive child and
  report its focus back.
- **Unset values did not restore native defaults.** `CornerRadius = -1` and a null or non-solid
  CheckBox `Foreground` both simply skipped the write, so clearing either kept whatever was last
  applied and the control could never return to its themed appearance. Both defaults are now
  captured at construction and restored.

One of these was found by the new tests rather than by reading: because the composite `MapFocus`
overrides were written inside an `#if TIZEN` block, on the host lane the name silently bound to the
*inherited* `ViewHandler.MapFocus` - MAUI's no-op. It compiled, and the behaviour differed by target
framework. The overrides are now unconditional, with only their bodies guarded.

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
   type, so a backend cannot detect one. `IView.Background` carrying an image therefore flattens
   through `Paint.ToColor()` and the image never renders. The code to apply one exists and works;
   it simply cannot be reached from the `Background` mapping.

   The gap is worked around by degrading honestly - falling through to the colour path - and
   deliberately **not** by reflecting over the internal type, which would bind this backend to
   MAUI implementation detail and break silently on any servicing update.

   Tracked upstream by [dotnet/maui#37864](https://github.com/dotnet/maui/pull/37864), which adds
   a public read-only `IImageSourcePaint`. Once a package contains it the adoption is an
   image-first pattern match on `IImageSourcePaint.ImageSource`, matched *before* the solid/
   `ToColor` fallback, with no reflection and no internal types.
   `UpstreamGapExpiryTests.ImageSourcePaintIsStillInternal` asserts the gap still exists and
   **fails when the contract ships**, so the workaround cannot outlive its justification.

Note that blocker 1 was re-examined against MAUI 26426.4 and still holds, while the old CS9333
objection to implementing MAUI's handler interfaces does **not** - see "Handler identity" above.

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
| `tests/Maui.Tizen.Core.UnitTests` | Mapper composition and Controls parity, command dispatch, DI registration, the naming/collision rules, and the platform-independent logic extracted from the handlers - stepper bounds, image-load lifecycle, selection normalisation, dispatching - are executed against host stand-ins. | passing |

Parity is measured **against MAUI Controls, not Core alone**. The test project references
`Microsoft.Maui.Controls`, and `ControlsRemap.Force()` performs the remaps before any mapper is
read, because `RemapForControls` mutates MAUI's static mappers in place. Measuring before that has
happened would report a parity the backend does not actually have.

*How* the remaps are forced matters, and the obvious way is wrong. Only `Label` and `CheckBox` call
`RemapForControls` from a **static constructor**; the other thirteen Wave A controls are remapped by
`Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions.ConfigureControls`, which runs when a
`MauiApp` is **built**. An earlier version of `ControlsRemap` only ran class constructors, so most
mappers were never remapped - and because everything shares one process, the numbers silently
depended on whether some unrelated test had built a Controls app first. `Force()` now builds a real
Controls host, and `ControlsRemapTests` asserts on keys that only a built host can produce
(`Picker.ItemsSource`, `Stepper.Increment`) so the regression cannot come back quietly.

`docs/mapper-parity-matrix.md` distinguishes four states rather than two: `tizen` (the backend
supplies an implementation), `controls` (implemented by the Controls layer, exactly as upstream
does), `inherited` (the key resolves through MAUI's chain but its body is the off-platform no-op,
so nothing happens on Tizen) and `excluded`. That distinction is the whole point of generating it -
chaining makes every key *resolve*, so a table reporting mere presence would show total parity
while most properties did nothing.

The `controls` state covers `TextTransform`, `ContentLayout` and `Button.LineBreakMode`. These are
properties of Controls types, not of the `Microsoft.Maui.*` interfaces this package consumes, and
upstream applies them from `Microsoft.Maui.Controls.Platform` rather than from a Core handler -
implementing them here would mean referencing Controls from the product package, which this
repository deliberately does not do. The sources already exist under `src/Maui.Tizen.Controls`,
awaiting the wave that owns that project, and `MapperParityMatrixTests` verifies each excused key
is genuinely implemented there so the state cannot be used to wave a gap away.

`RadioButton.TextTransform` is deliberately absent rather than excused: upstream guards that remap
with `#if ANDROID || WINDOWS`, so the key does not exist on this package at all.

Several pieces of logic were deliberately extracted out of the NUI-typed classes
(`TizenStepperRange`, `TizenImageLoader<T>`, `TizenTextSelection`, `TizenDispatchExtensions`)
precisely so they land in the host lane and can be executed rather than merely reviewed. That is
where the subtle defects were, and it is the difference between a test that asserts a mapper key
exists and one that proves the behaviour is right.

Exact blockers, unchanged by this wave:

- `Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7` is not published. Neither that form nor
  the plain `-11.0.100` one exists on nuget.org; only the `9.0.100` and `10.0.100` bands
  publish, and the newest `Samsung.Tizen.Sdk` is `10.0.128`. Until one ships, `dotnet restore`
  on any `net11.0-tizen11.0` project fails. `eng/baselines.json` is authoritative here.
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
