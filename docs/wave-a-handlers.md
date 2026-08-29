# Wave A — common view behaviour and the simple controls

This is the second migration wave. The [core vertical slice](architecture.md) established the
application, window, layout and label path; Wave A adds the controls that sit inside it.

The checklist for merging this wave onto a stable Core head — image-service composition with
Wave B, the Core-owned test delta, the upstream adoption guards and the mapper behaviour bar — is
in [`wave-a-integration-plan.md`](wave-a-integration-plan.md).

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

Registration happens in the composition root. `ConfigureTizen()` wires all of it:

```csharp
builder.ConfigureTizen();   // handlers + control services + image sources
```

which internally performs:

```csharp
builder.ConfigureMauiHandlers(handlers =>
{
    handlers.AddTizenHandlers();          // core slice: application, window, layout, label
    handlers.AddTizenControlHandlers();   // Wave A: the fourteen simple controls
});
builder.Services.AddTizenControlServices();
builder.ConfigureImageSources(sources => sources.AddTizenImageSources());
```

The three `AddTizen*` methods stay public and separate so a host can adopt either half
independently while the migration is in flight - but **the composition root calls them**, and that
is not a detail. An earlier revision declared them and left the calls to the host, with the snippet
above presented as something an app would write. The result was that all fourteen control handlers,
their font manager and modal host, and both image source services were **never registered in a real
app**. Every registration test passed, because each one invoked the `AddTizen*` method itself and so
verified the method rather than the wiring.

Two tests now close that gap, both of which fail if the calls are removed:
`EveryControlHandlerResolvesFromTheCompositionRoot` resolves each control through an app built with
`ConfigureTizen()` alone, and `EveryTizenRegistrationExtensionHasACaller` fails on any public
`AddTizen*` registration extension that no compiled source calls - which is how the second and third
instances were found rather than reviewed for.

The image source case deserves its own note, because it fails *silently*: MAUI's neutral package
already registers `FileImageSourceService`, `StreamImageSourceService`, `FontImageSourceService` and
`UriImageSourceService`, so every source type resolves whether or not `AddTizenImageSources` ever
runs. Nothing throws and no service is reported missing - images are simply blank. A test asserting
"an image source service is registered" passes on an app that can never display an image, so
`CompositionRootTests` asserts *which* implementation wins.

`AddTizenImageSources` is deliberately in the portable compile group, separated from the
NUI-dependent services it registers, so the call in `ConfigureTizen` compiles on both lanes and the
image workstream has a seam to extend. Font and URI sources are still MAUI's neutral defaults;
`FontAndUriSourcesAreNotYetTizenOwned` records that and fails, with instructions, when that wave
lands.

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
- **Picker `IsOpen` is the popup state contract.** Programmatic true/false, touch and key
  activation, acceptance, cancellation and disconnect all flow through the same generation-owned
  popup lifecycle; stale completions cannot update a replacement view.
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

   Tracked upstream by [dotnet/maui#37854](https://github.com/dotnet/maui/pull/37854), which adds
   a `ValidateContainerView` hook for external backends. **The signature is the load-bearing part:**

   ```csharp
   protected override void ValidateContainerView(object containerView)
   {
       // narrow to TizenWrapperView / the NUI type HERE, in the body
   }
   ```

   The parameter must be `object`. Narrowing it to the backend's own wrapper type overrides
   nothing and fails with **CS0115** — an override has to match the base signature exactly, and on
   the neutral package MAUI types platform things as `object`.

   That is worth stating plainly because it is the **third** appearance of one root cause on this
   branch. The neutral package types platform-facing things as `object`, and every attempt to be
   more specific than the base declaration fails — sometimes loudly, sometimes silently:

   | Where | Symptom |
   |---|---|
   | `IXHandler.PlatformView` explicit implementation | `CS9333` — produced the retired `ITizen*Handler` hierarchy |
   | Mapper entries typed against the concrete handler | silent rebinding to MAUI's inherited no-op |
   | `ValidateContainerView(TizenWrapperView)` | `CS0115` |

   The rule for Waves B/C: **match the base or interface signature exactly, and narrow inside the
   body.** `UpstreamGapExpiryTests.ContainerViewIsStillUnsettableByAnExternalBackend` asserts the
   gap still exists, reports the shipped signature when it lands (including a warning if it is
   *not* the shape this plan assumed), and fails so the workaround cannot outlive its
   justification. Adoption is core's, since `NeedsContainer` is core-owned.

2. **`ImageSourcePaint` is `internal`.** MAUI models an image background with an internal paint
   type, so a backend cannot detect one. `IView.Background` carrying an image therefore flattens
   through `Paint.ToColor()` and the image never renders. The code to apply one exists and works;
   it simply cannot be reached from the `Background` mapping.

   The gap is worked around by degrading honestly - falling through to the colour path - and
   deliberately **not** by reflecting over the internal type, which would bind this backend to
   MAUI implementation detail and break silently on any servicing update.

   Tracked upstream by [dotnet/maui#37864](https://github.com/dotnet/maui/pull/37864), which adds
   a public read-only `IImageSourcePaint`. Upstream's guidance is **consumption only**: external
   implementation is unsupported and the interface may gain members, so this backend must only
   pattern match MAUI's own built-in paint and must never declare `Paint : IImageSourcePaint`.

   The adoption, once a package contains the contract:

   - match `IImageSourcePaint` **first**, ahead of the gradient *and* solid branches, or an image
     paint keeps flattening to a colour exactly as it does today;
   - treat `ImageSource == null` as *clear any previously applied image and return* - not as
     "leave things alone", which would strand the old image under a new source;
   - clear on a null async result too: a resolution can succeed and still yield no image;
   - do it in the `IView` overload of `UpdateBackground`, since resolving an image source needs an
     `IImageSourceServiceProvider` reached through `view.Handler`.

   That last point is why Wave A's background mappings pass the **view** rather than
   `view.Background`. The two overloads behave identically today, so the distinction is invisible -
   and a mapping that discards the view would silently keep failing after the upstream fix landed.
   `UpstreamGapExpiryTests.BackgroundMappingsKeepTheViewInScope` pins it.

   Three tests hold this together: `ImageSourcePaintIsStillInternal` asserts the gap still exists
   and **fails when the contract ships** (reporting the shipped member shape, so the adopter learns
   in one run whether the plan still applies); `BackendDoesNotReachImageSourcePaintByName` forbids
   reflecting over the internal type; and `BackendDoesNotImplementImageSourcePaint` forbids
   implementing the contract - the one guard that must outlive the others.

   `TizenImageLoader` already provides the cancellation, supersession, failure-clearing and
   disposal semantics the eventual load needs, including the "resolved successfully but yielded no
   image" case (`ALoadResolvingToNoImageClearsThePrevious`).

Note that blocker 1 was re-examined against MAUI 26426.4 and still holds, while the old CS9333
objection to implementing MAUI's handler interfaces does **not** - see "Handler identity" above.

Also worth recording: `IFontManager` and `IImageSourceService` are marker-only in the neutral
assembly — the members that actually resolve a font or load an image exist solely in each
platform's own build of MAUI. Wave A declares `ITizenFontManager` and `ITizenImageSourceService`
to fill that in, and `ConfigureTizen` registers them.

`IFontManager` is registered with **`Replace`**, not `TryAdd`, and that matters. `useDefaults: true`
means MAUI's `ConfigureFonts` has already registered `Microsoft.Maui.FontManager` by the time this
runs, so a `TryAdd` is a silent no-op — and the failure is invisible rather than loud, because
`GetTizenFontFamily` pattern matches the resolved manager to `ITizenFontManager` and falls back to
the raw family name when it does not match. Every font alias then reaches NUI unresolved and text
renders in the wrong font with nothing thrown. This is the same trap that governs the dispatcher,
ticker and animation manager: **any service MAUI registers by default must be replaced**; `TryAdd`
is correct only for contracts MAUI does not know about, where a host substituting its own should
win.

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

`docs/mapper-parity-matrix.md` distinguishes three states rather than two: `tizen` (the backend
supplies an implementation), `inherited` (the key resolves through MAUI's chain but its body is the
off-platform no-op, so nothing happens on Tizen) and `excluded`. That distinction is the whole
point of generating it - chaining makes every key *resolve*, so a table reporting mere presence
would show total parity while most properties did nothing.

`TextTransform`, `ContentLayout` and `Button.LineBreakMode` are reported as `inherited`. They are
properties of **Controls** types that upstream applies from `Microsoft.Maui.Controls.Platform`
rather than from a Core handler, so implementing them here would mean referencing Controls from the
product package. Matching sources do exist under `src/Maui.Tizen.Controls` - but on this branch
**that project is in no compiled lane**, so they are unbuilt, unexecuted and untested. An earlier
revision gave these keys a distinct `controls` state on the strength of the files existing; that
overstated reality, because source nobody compiles cannot be known to work.

**This is expected to change at the rebase, and the guard is what will say so.** Core `efd759ea`
adds a `Maui.Tizen.Controls.RefPackCompile` lane that builds a real `Maui.Tizen.Controls` assembly,
so `MapperParityMatrixTests.ControlsLayerFollowUpIsNotMistakenForCoverage` - which asserts the
project is *not* in a lane - will fail on the first rebase onto that head. That is the test doing
its job rather than a regression: the evidence behind the demotion has changed, so each key must be
**re-measured** against what the new lane actually compiles and binds, and promoted only where that
holds. Note the lane compiled only `TizenControlsMappings.cs` as of `efd759ea`, so a key is covered
only if that file binds it - `LineBreakMode` is bound there, `TextTransform` and `ContentLayout`
are not.

`RadioButton.TextTransform` is deliberately absent rather than excused: upstream guards that remap
with `#if ANDROID || WINDOWS`, so the key does not exist on this package at all.

### Resolution is not implementation

The review found Label's `FormattedText`, `LineBreakMode`, `MaxLines` and accessibility keys
*reachable* - key present, no cast failure - yet behaviourally inert, because the body they resolved
to was MAUI's off-platform no-op. Every test written at the time passed. `ControlsRemapBehaviorTests`
therefore asserts the Controls remaps by **observable effect**:

- `Picker.ItemsSource` really does raise `IPicker.Items` (verified by removing the override and
  watching the test fail);
- `Stepper.Increment` really does raise `IStepper.Interval`;
- `SemanticProperties.Description`/`Hint`/`HeadingLevel` really do reach the backend's `Semantics`
  mapping.

Measuring this also turned up two keys that are **reachable but genuinely inert**:
`IsInAccessibleTree` and `ExcludedWithChildren` resolve through the chain and do nothing observable.
That is recorded by `KnownInertAccessibilityKeysAreStillInert` rather than quietly tolerated - the
mapping is core-owned and is reported there, and the test fails (with instructions) if someone
implements them.

What a host lane can prove has a real limit, and the docs should not overstate it either: a
control-specific body such as `TizenEntryHandler.MapBackground` is entirely inside `#if TIZEN`, so
off-device it genuinely does nothing and no host test can claim otherwise. What *is* verifiable
here is dispatch logic - whether a Controls key forwards to the backend key that implements it -
which is exactly where the remaps live. Everything past that needs the device lane the unpublished
workload still blocks.

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
