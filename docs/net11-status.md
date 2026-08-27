# .NET 11 status: blockers and required MAUI API gaps

Status of the **core standalone backend vertical slice** (application, window, page, content view,
layout, label) in `src/Maui.Tizen.Core`.

Baselines are frozen in [`eng/baselines.json`](../eng/baselines.json); package versions are
centrally configurable in [`eng/Maui.props`](../eng/Maui.props).

---

## 1. What is verified, and how

The product assembly targets `net11.0-tizen11.0` only. It **cannot be restored or built anywhere**
until Samsung publishes the 11.0.100 workload manifest (see blocker B1). Rather than weaken that
contract with a neutral fallback, verification is split across two **complementary** lanes. They do
not compile identical sets - that would be impossible, since the platform sources need real TizenFX:

| Lane | Compiles | Assembly | `TIZEN` |
| --- | --- | --- | --- |
| `Maui.Tizen.Core.UnitTests` | portable + handler | test host | no |
| `Maui.Tizen.Core.RefPackCompile` | portable + handler + platform | `Maui.Tizen.Core` | yes |
| `Maui.Tizen.Sample.RefPackCompile` | sample only, references the above | `Maui.Tizen.Sample` | yes |
| `Maui.Tizen.Core` (product) | portable + handler + platform | `Maui.Tizen.Core` | yes |

Between them every owned source is compiled by at least one lane, and everything the product
compiles is also compiled by the ref-pack lane. `SourceLaneCoverageTests` pins that invariant.

The sample gets its **own** lane rather than being folded into the backend's, and that separation is
load-bearing in two ways an MSBuild review had to point out:

* The sample must cross a real assembly boundary. Compiled into the backend lane it produced one
  merged Core+sample assembly, so a sample that reached for a backend internal - or for anything
  invisible across a package reference - compiled clean. It now reaches the backend through a
  `ProjectReference` to an assembly carrying the real product `AssemblyName`.
* PublicAPI ownership is only meaningful while each compilation is checked against its own baseline.
  With both pairs attached to one merged surface, moving `TizenFlyoutView` out of the backend
  baseline and into the *sample's* still built successfully. It now fails RS0016.

The real `samples/Maui.Tizen.Sample` separately evaluated `Compile=[]` - `TizenPackage.props`
defaults `EnableDefaultCompileItems` to false for the not-yet-ported projects and the sample never
opted back in, so it was an application head that built successfully while containing no code.
`PackageBoundaryTests` asserts the evaluated item lists of the real sample and its lane are
identical, so neither can drift from the other.

| Lane | Command | What it proves |
| --- | --- | --- |
| Unit tests | `dotnet test tests/Maui.Tizen.Core.UnitTests` | Mapper + command-mapper registration, DI/handler registration, hosting, dispatcher/timer/provider semantics, density conversion, layout z-index ordering, `IMauiContext` scoping. **102 tests, all passing.** |
| Compile validation | `dotnet build tests/Maui.Tizen.Core.RefPackCompile` | Every `#if TIZEN` source - including `TizenMauiApplication`, the NUI view groups and all the ported platform extensions - type-checks against the **real** TizenFX reference assemblies from `Samsung.Tizen.Ref.API15` (`ref/net8.0`), plus the sample head's managed code. **Builds clean.** |
| Product | `dotnet build src/Maui.Tizen.Core` | Fails with actionable `MAUITIZEN0001` from `Directory.Build.targets`. This is the intended behaviour. |

Both lanes are wired into `eng/build-workload-free.sh`, so they run in the workload-free CI lane
under Release + `ContinuousIntegrationBuild=true` (i.e. with `TreatWarningsAsErrors`).

The compile-validation lane is *not* a neutral TFM fallback: it references reference-only
assemblies, is `IsPackable=false`, and produces nothing that could ever be shipped or executed. It
exists so `#if TIZEN` code is checked by a compiler rather than by inspection.

> Note: a device build has **not** been performed and must not be claimed. Nothing here validates
> runtime behaviour on Tizen.

---

### G10. Controls owns several Tizen bindings, and upstream never implemented them

This affects accessibility **and** three Label properties. The shape is identical in every case:
the value lives on a Controls type, so a backend package cannot read it without referencing
Controls and inverting the dependency direction.

| Property | Declared on | Upstream Tizen implementation |
| --- | --- | --- |
| `AutomationProperties.IsInAccessibleTree` | Controls | `//TODO : Need to impl` |
| `AutomationProperties.ExcludedWithChildren` | Controls | `//TODO : Need to impl` |
| `Label.LineBreakMode` | Controls | implemented, via a Controls-side extension |
| `Label.MaxLines` | Controls | `[MissingMapper]`, empty |
| `Label.FormattedText` | Controls (`FormattedString`) | not mapped |

`ILabel` carries only `TextDecorations` and `LineHeight` - verified by reflection over the shipped
`Microsoft.Maui` assembly - so none of the three Label properties is reachable from this backend.

**These are now bound**, in `src/Maui.Tizen.Controls/Platform/TizenControlsMappings.cs` - a real
product assembly that legitimately references Controls, compiled by
`tests/Maui.Tizen.Controls.RefPackCompile` against real TizenFX. It appends to the static Controls
mappers, which is the same public mechanism Controls' own `RemapForControls` uses, because an
out-of-repo backend cannot contribute to Controls' per-platform partial classes.

`LineBreakMode` and both accessibility annotations are bound and working - the accessibility pair
through a **single** mapper action, since both write to the same two NUI flags and binding them
separately let whichever ran last overwrite the other.

`MaxLines` and `FormattedText` are **not** bound, and no inert key is registered for either.
`MaxLines` has no native equivalent at all. `FormattedText` requires converting Controls'
`FormattedString` into the native markup form; upstream does not implement it on Tizen and neither
does this backend yet - it is an explicit Wave A requirement before the sample can claim Label
parity.

Core ships the native halves it owns: `UpdateAccessibility` (both annotations at once) and
`UpdateLineBreakMode`. The last one matters because the two `LineBreakMode` enums are **not**
ordinal-compatible - `Microsoft.Maui.LineBreakMode.NoWrap` is 0 while
`Tizen.UIExtensions.Common.LineBreakMode.NoWrap` is 1 - so casting between them turns NoWrap into
None and shifts every value after it.

`MaxLines` has deliberately **no** Core primitive: there is no native equivalent. `TextLabel`
exposes `LineCount` (read-only), `MultiLine` and `Ellipsis`, none of which caps rendered lines, and
`Tizen.UIExtensions.NUI.Label` exposes only `LineBreakMode`. That is almost certainly why upstream
marks its mapper `[MissingMapper]`. A resolver here would be dead code dressed up as coverage.

Closing the gap needs a Controls-side change or an explicit owner in a later wave.

### G10a. Original accessibility note

`AutomationProperties.IsInAccessibleTree` and `ExcludedWithChildren` arrive as their own mapper
keys, and the action behind those keys lives in **Controls'** per-platform code
(`src/Controls/src/Core/Element/Tizen.cs`), not in Core. A backend package cannot supply it without
referencing Controls, which would invert the dependency direction.

Upstream both methods are empty:

```csharp
public static void MapAutomationPropertiesIsInAccessibleTree(IElementHandler handler, Element element)
{
    //TODO : Need to impl
}
```

So these annotations have never worked on Tizen, and the stub disappears entirely when dotnet/maui
drops its Tizen target. Core now ships the native half - `UpdateIsInAccessibleTree` and
`UpdateExcludedWithChildren`, built on NUI's `AccessibilityHighlightable` and
`AccessibilityHidden` - so whoever ends up owning the Controls binding has something correct to
call. Closing the gap needs either a Controls-side change or an explicit owner in a later wave.


## 2. External blockers

### B1 - Samsung Tizen workload manifest for the 11.0.100 SDK band does not exist

`samsung.net.sdk.tizen.manifest-11.0.100` is not published. Only the `-9.0.100` and `-10.0.100`
bands exist, and the newest `Samsung.Tizen.Sdk` is `10.0.128`.

Consequence: the `net11.0-tizen11.0` target framework cannot be resolved by the SDK at all, so
neither `Maui.Tizen.Core` nor `Maui.Tizen.Sample` can restore, build, package or deploy.

Measured on SDK `11.0.100-preview.7.26381.103`, with the gate deliberately overridden:

```
$ dotnet build src/Maui.Tizen.Core -p:TizenWorkloadAvailable=true
error NETSDK1013: The TargetFramework value 'net11.0-tizen11.0' was not recognized.
```

Note that MAUI's own `maui-tizen` workload **does** exist in this band and reports as installed:

```
maui-tizen   11.0.0-preview.7.26406.9/11.0.100-preview.7   SDK 11.0.100-preview.7
```

but it is an empty shim - its manifest entry is `{"extends": ["maui-blazor"]}` with no packs, and
it no longer references Samsung's workload at all. Installing it therefore does nothing for this
repository. `~/.dotnet11/sdk-manifests/11.0.100/` contains no `samsung.*` manifest.

Two things follow: the gate is genuinely external, and `maui-tizen` being present must not be
mistaken for the Tizen SDK being present - see B3.

Modelled as an explicit gate (`MAUITIZEN0001`) in both projects. Override with
`-p:TizenWorkloadAvailable=true` once the workload is installed.

**Owner: Samsung. No workaround.**

### B2 - `Tizen.UIExtensions.NUI` 0.9.2 must not ship

Its nuspec declares:

* `Microsoft.Maui.Graphics` **6.0.300-rc.3.1336**
* `Microsoft.Maui.Graphics.Skia` **6.0.300-rc.3.1336**
* `SkiaSharp.Views` 2.88.6
* `Tizen.NET` 10.0.0.17508

and it only publishes `lib/tizen10.0` and `lib/net6.0-tizen7.0`. Shipping `Maui.Tizen.Core` on top
of it would drag a .NET 6-era MAUI Graphics into every .NET 11 Tizen app.

Mitigations in place:

* The version is centrally managed in `Directory.Packages.props`, mirrored by
  `$(TizenUIExtensionsPackageVersion)` in `eng/Maui.props`.
* `$(TizenUIExtensionsIsShippable)` (in `eng/Maui.props`) is `false` while that version is
  `0.9.2`, and `MAUITIZEN0101` **refuses to pack** in that state.
* `Microsoft.Maui.Graphics` is referenced explicitly at the central version so the 6.x package
  cannot win resolution.

`eng/baselines.json` records that the republish is needed *solely* to drop the .NET 6-era Graphics
dependencies, with no API surface change expected - so bumping the one property should be enough.

**Owner: Tizen.UIExtensions maintainers.**

### B3 - `eng/build-workload-free.sh` will mis-report the gate once MAUI's workload is installed

```bash
if "$DOTNET" workload list 2>/dev/null | grep -qi tizen; then
  pass "Samsung Tizen workload is installed - the Tizen lane can now be made required"
```

`grep -qi tizen` also matches MAUI's own **`maui-tizen`** workload id, which - per B1 - exists in
the 11.0.100 band as an empty shim that provides nothing. On a developer machine with the MAUI
workload installed the script announces that the Tizen lane can be promoted to required, while
`dotnet build src/Maui.Tizen.Core` still fails with `MAUITIZEN0001`. Reproduced locally.

CI currently reports correctly only because its agents have no workloads installed at all, so this
is latent rather than active.

The MSBuild-side detection in `Directory.Build.props` is correct - it probes for the
`samsung.net.sdk.tizen/WorkloadManifest.json` file - so nothing builds that should not; only the
script's advisory line is wrong. Suggested fix: match the manifest id rather than a substring, e.g.
`grep -qE '^\s*samsung\.net\.sdk\.tizen'`, or reuse the same file probe.

Foundation-owned; flagged rather than changed here.

### B4 - No public MAUI package contains the required API floor (resolved via the dev feed)

dotnet/maui#36657 (`0b3bb76d2d`) merged 2026-08-18. The newest .NET 11 MAUI build on nuget.org is
`11.0.0-preview.7.26406.9`, which predates it.

Resolved, not blocked: the repository consumes `11.0.0-preview.7.26426.4` from the `dotnet11` dnceng
feed, pinned centrally in `Directory.Packages.props` and mapped in `nuget.config` via
`packageSourceMapping`.

Note that these packages are built from `bedd1b18b7`, which is 46 commits *ahead* of the frozen
source baseline `ee4d06cde6` that the history import is pinned to. `eng/baselines.json` records the
consequence in full: the gap is one-directional and applies to imported **source**, not to the
assemblies this slice compiles against. Nothing in this backend depends on the three Tizen-touching
commits in that gap.

### B5 - Runtime packs

Not evaluated. `Samsung.Tizen.Ref.API15` provides *reference* assemblies only; the corresponding
runtime packs come from the workload and are therefore blocked behind B1.

---

## 3. Required public MAUI API gaps

Each of these forced this backend to own code that dotnet/maui already has. They are the concrete
asks for making MAUI genuinely extensible by out-of-repo platform backends.

### G1 - `ViewHandler.ContainerView` cannot be set from outside MAUI

```csharp
public PlatformView? ContainerView { get; private protected set; }
protected abstract void SetupContainer();
protected abstract void RemoveContainer();
```

`SetupContainer` is abstract and therefore *must* be implemented, but the property it is supposed to
populate has a `private protected` setter. An external backend can be asked to build a container and
then has no way to publish it.

*Impact:* `TizenViewHandler<,>` overrides `NeedsContainer => false` and implements both methods as
no-ops. Gradient/image backgrounds, clipping and shadows that dotnet/maui renders through
`WrapperView` are therefore not available in this slice; solid colours are applied directly to the
platform view.

*Ask:* a `protected` setter, or a `protected virtual PlatformView? CreateContainerView()` hook.

### G2 - ~~Handler interfaces bind `PlatformView` to a per-TFM alias~~ **WITHDRAWN**

**This gap was reported in error and is retracted.** It never existed on the package this
repository consumes.

The claim was that `ILabelHandler`, `IContentViewHandler`, `ILayoutHandler` and `IWindowHandler`
redeclare `new PlatformView PlatformView { get; }` against a per-TFM alias, and so cannot be
implemented by an out-of-repo backend (CS9333 / CS0738).

Verified by reflection over `Microsoft.Maui.dll` from `Microsoft.Maui.Core`
**11.0.0-preview.7.26426.4** (`lib/net11.0`), which is what this repository actually compiles
against:

```
Microsoft.Maui.Handlers.ILabelHandler:        PlatformView -> System.Object
Microsoft.Maui.ILayoutHandler:                PlatformView -> System.Object
Microsoft.Maui.Handlers.IContentViewHandler:  PlatformView -> System.Object
Microsoft.Maui.Handlers.IWindowHandler:       PlatformView -> System.Object
```

`PlatformView` is `System.Object`, not an alias, and the interfaces are perfectly implementable -
confirmed by compiling a handler against the published package.

**What actually happened.** The original CS9333 came from the explicit interface implementation
returning the *concrete* platform type:

```csharp
TizenLabelView ILabelHandler.PlatformView => PlatformView;   // CS9333
object ILabelHandler.PlatformView => PlatformView;           // correct
```

An explicit implementation must match the declared type exactly. The compiler was reporting a
mistake in this backend, not a limitation in MAUI; the conclusion drawn from it was wrong.

**Consequences of the correction.** The parallel `ITizenLabelHandler` / `ITizenContentViewHandler` /
`ITizenPageHandler` / `ITizenLayoutHandler` / `ITizenWindowHandler` hierarchy has been removed. The
handlers implement MAUI's `ILabelHandler`, `IContentViewHandler`, `IPageHandler`, `ILayoutHandler`
and `IWindowHandler` directly.

That is not merely tidier - the parallel hierarchy actively blocked MAUI Controls. Controls'
`RemapForControls()` mutates the **static** `LabelHandler.Mapper` and friends, so a backend can only
observe those entries by chaining that same mapper, which requires the mapper to be typed against
MAUI's handler interface. `ControlsRegistrationTests` now pins that Controls types resolve to these
handlers and that Controls-only mapper keys are reachable through the chain.

Two backend-owned interfaces remain, each for a stated reason:

* `ITizenApplicationHandler` - MAUI Core ships **no** `IApplicationHandler`, so there is nothing to
  implement instead.
* `ITizenPlatformViewHandler` - native parenting and disposal, which has no MAUI equivalent.

### G3 - `MauiContext.AddSpecific` / `AddWeakSpecific` are internal

There is no supported way for a backend to publish the platform window or platform application into
a `IMauiContext` scope.

*Impact:* `TizenMauiContext` implements the public `IMauiContext` (plus `IServiceProvider`) directly
and overlays specific instances itself.

*Ask:* make the `AddSpecific` family public, or expose a public `IMauiContext` builder.

### G4 - `LifecycleEventServiceExtensions.InvokeLifecycleEvents` is internal

The public members take an `ILifecycleEventService`; the `IServiceProvider` overloads that a platform
backend actually calls (`InvokeLifecycleEvents<TDelegate>`, `GetLifecycleEventDelegates<TDelegate>`)
are `internal`.

*Impact:* ported as `TizenLifecycleEventExtensions.InvokeTizenLifecycleEvents`.

*Ask:* make both `public`.

### G5 - `LayoutExtensions.OrderByZIndex` / `GetLayoutHandlerIndex` are internal

`Microsoft.Maui.Handlers.LayoutExtensions` is an `internal static class`, yet every platform backend
needs exactly this z-index ordering logic to place child platform views.

*Impact:* ported verbatim as `TizenLayoutExtensions` (and unit tested here).

*Ask:* make the class `public`.

### G6 - `HandlerNotFoundException` is internal

*Impact:* replaced with `InvalidOperationException` carrying an equivalent message.

*Ask:* make it `public`.

### G7 - The whole `Microsoft.Maui.Platform.*Extensions` Tizen surface is TFM-locked

`DPExtensions`, `ViewExtensions`, `LabelExtensions`, `ColorExtensions`, `WindowExtensions`,
`ElementExtensions` are `public`, but only inside the `net*-tizen` build of `Microsoft.Maui.dll`.
Depending on them would defeat the extraction and would break the moment MAUI drops its Tizen TFM.

*Impact:* the members this slice needs are ported into `TizenPlatformExtensions`.

*Ask:* none for MAUI - this is expected migration work - but it is the bulk of the remaining port
for the non-slice handlers.

### G8 - TizenFX API15 deprecations that dotnet/maui has not taken

The compile-validation lane found that dotnet/maui's Tizen sources still call
`Tizen.NUI.Window.Instance`, which TizenFX deprecated in **API12** in favour of `Window.Default`.
Under `TreatWarningsAsErrors` this is a hard failure at API15.

*Impact:* this backend uses `Window.Default`. Expect more of these as the remaining handlers are
ported - the API15 reference pack is the cheapest way to find them, and is exactly why the
compile-validation lane exists.

### G9 - `IFontManager` has no cross-platform `GetFontFamily`

dotnet/maui's Tizen `LabelExtensions.UpdateFont` calls a Tizen-only `IFontManager.GetFontFamily`
extension that is not part of the cross-platform `IFontManager` surface.

*Impact:* `TizenPlatformExtensions.UpdateFont` uses `label.Font.Family` directly. Font aliasing
registered through `IFontRegistrar` will not be resolved until this is addressed.

*Ask:* expose font-family resolution on the public `IFontManager`.

---

## 4. Constraints honoured

* No private reflection, no `DispatchProxy`.
* No MAUI source-project references; `Microsoft.Maui.Core` is consumed as a NuGet package.
* No `InternalsVisibleTo` from MAUI.
* No neutral MAUI type names re-declared - `MauiApplication` &rarr; `TizenMauiApplication`,
  `PlatformTicker` &rarr; `TizenTicker`, `Dispatcher` &rarr; `TizenDispatcher`,
  `IPlatformViewHandler` &rarr; `ITizenPlatformViewHandler`, all under
  `Microsoft.Maui.Platforms.Tizen`. No CS0433 risk for consumers that also reference MAUI's Tizen
  build.
* `MainThread` is **not** ported. `MainThread.tizen.cs` is deliberately absent; `MainThread` flows
  through the .NET 11 dispatcher bridge because `UseMauiAppTizen` registers
  `TizenDispatcherProvider` as `IDispatcherProvider`.

## 5. Not in this slice

**Modal navigation.** dotnet/maui's `WindowExtensions.Initialize` creates a per-window
`NavigationStack` and routes window content through it. This backend ports the orientation
registration and the hardware back-key wiring from that method, but not the modal stack: window
content is parented directly and replaced in place. `GetModalStack` / `IToolbarContainer` and
anything built on them are therefore absent.

**Container-backed decoration.** See G1 - gradient/image backgrounds, clip and shadow are not
rendered, because the container hook is not reachable from outside MAUI.

**Controls-level remapping.** `Layout.RemapForControls` and friends append to MAUI's *static*
`LayoutHandler.Mapper`, not to this backend's mappers, so Controls-specific mappings do not reach
these handlers. Wiring that up belongs with the `Maui.Tizen.Controls` layer.

**Everything else.** All other handlers (button, entry, image, scroll view, web view, navigation,
shell, ...) remain raw imported sources and are not yet ported.

### Core-owned platform primitives for Wave C

Three NUI primitives are owned by this package even though the handlers that drive them belong to
Wave C, because they are platform surfaces rather than handlers. Porting them here is what allows
the raw imported originals to stay uncompiled.

| Imported source | Owned by this package | Why renamed |
| --- | --- | --- |
| `Platform/Tizen/MauiToolbar.cs` | `TizenToolbarView` | `Microsoft.Maui.Platform.MauiToolbar` exists in MAUI's Tizen build |
| (same file) `IToolbarContainer` | `ITizenToolbarContainer` | same |
| `Platform/Tizen/StackNavigationManager.cs` | `TizenStackNavigationManager` | `Microsoft.Maui.Platform.StackNavigationManager` exists in MAUI's Tizen build |
| `Platform/Tizen/NaviPage.cs` | `TizenNaviPage` | `Microsoft.Maui.NaviPage` sits in the **neutral** namespace - the highest-risk collision |
| `Platform/Tizen/MauiFlyoutView.cs` | `TizenFlyoutView` | `Microsoft.Maui.Platform.MauiFlyoutView` exists in MAUI's Tizen build |
| `Platform/Tizen/MauiTVFlyoutView.cs` | `TizenTVFlyoutView` | same |
| `Platform/Tizen/FlyoutViewExtensions.cs` | `TizenFlyoutViewExtensions` + `TizenFlyoutBehaviorExtensions` | same; the `ToPlatform(FlyoutBehavior)` overload became `ToTizenDrawerBehavior` to avoid ambiguity with MAUI's large `ToPlatform` family |
| `Platform/Tizen/ToolbarExtensions.cs` | instance methods on `TizenToolbarView` | same |

`NaviPage` was not in the assigned list; it came in because `StackNavigationManager` cannot compile
without it. Porting it was the only way to avoid compiling the raw original.

`ToolbarExtensions.UpdateTitle` / `UpdateMenuButton` became **instance methods** on
`TizenToolbarView` rather than extension methods. They only ever applied to that one type, and as
extensions they would have been ambiguous at any call site that also imported MAUI's
`Microsoft.Maui.Platform`.

All three are type-checked against real TizenFX by the reference-pack lane and pinned by
`CorePlatformPrimitiveTests`, which also asserts that no Wave C handler has leaked into this
package.

## 6. Additional MAUI API gaps found during review

* **`MauiContextExtensions.InitializeScopedServices` is a public method on an `internal` class.**
  A backend cannot run `IMauiInitializeScopedService` implementations when it creates a window
  scope, which is required - MAUI's own dispatcher registers one. Ported as
  `TizenMauiContextExtensions.InitializeTizenScopedServices`.
* **`ViewHandler.ViewMapper` is unusable as a base for an out-of-repo backend.** Off-platform it is
  compiled with `PlatformView` aliased to `object` and dispatches to the `Standard` no-op
  extensions, so chaining it yields *no behaviour at all* for every generic `IView` property while
  still reporting every key as present. This backend therefore owns `TizenViewMappers.ViewMapper`,
  and its tests assert behaviour rather than key presence.
* **`PublicApiAnalyzers` baselines are attached by directory convention.** The imported
  `Microsoft.Maui.*` baselines describe a different assembly; see
  `src/Maui.Tizen.Core/PublicAPI/slice/README.md` for how they are scoped and what remains
  suppressed until the workload ships.

## 7. What is wired for a real device

Recorded explicitly because none of it can be executed by the host lanes, so it rests on comparison
against the dotnet/maui reference rather than on a passing test:

* Hardware **back key** - `InitializePlatformWindow` subscribes to `Window.KeyEvent` and routes a
  decline key to `IWindow.BackButtonClicked()`; if the cross-platform window does not handle it, the
  registered close-request handler runs `CoreApplication.Exit()`.
* **Rotation** - all four orientations are registered on the platform window.
* **Window frame** - `UpdateX/Y/Width/Height` call `IWindow.FrameChanged` with the real device
  geometry rather than trying to move or resize the window, which Tizen does not permit.
* **Content replacement** - `SetMainContent` removes the previous content before adding the new
  one, so re-assigning `IWindow.Content` does not stack views.
* **Page background** - a null `Paint` is a no-op for a page, so its opaque white default survives
  the first mapper pass, while an ordinary view's background *is* cleared on transition to null so
  a stale colour cannot persist.
* **Window lifecycle** - `TizenWindowLifecycleBridge` maps `CoreApplication`'s
  `OnCreate`/`OnResume`/`OnPause`/`OnTerminate` onto `IWindow.Created`/`Activated`/`Deactivated`/
  `Stopped`/`Resumed`/`Destroying`, keeping activate/deactivate balanced.
* **Window scope** - `MakeWindowScope` runs registered `IMauiInitializeScopedService` instances.
* **Content replacement** - the previous content's *handler* is disconnected and disposed, not just
  its native view unparented.
