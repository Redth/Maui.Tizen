# .NET 11 status: blockers and required MAUI API gaps

Status of the **core standalone backend vertical slice** (application, window, page, content view,
layout, label) in `src/Maui.Tizen.Core`.

Baselines are frozen in [`eng/baselines.json`](../eng/baselines.json); package versions are
centrally configurable in [`eng/Maui.props`](../eng/Maui.props).

---

## 1. What is verified, and how

The product assembly targets `net11.0-tizen11.0` only. It **cannot be restored or built anywhere**
until Samsung publishes the 11.0.100 workload manifest (see blocker B1). Rather than weaken that
contract with a neutral fallback, verification is done by two projects that compile the *same*
sources.

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

Resolved, not blocked: the repository consumes `11.0.0-preview.7.26418.3` from the `dotnet11` dnceng
feed, pinned centrally in `Directory.Packages.props` and mapped in `nuget.config` via
`packageSourceMapping`. The frozen source baseline (`ee4d06cde6`) is later than that package, so a
newer coherent dev build can be dropped in without touching anything else.

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

### G2 - Handler interfaces bind `PlatformView` to a per-TFM alias

`ILabelHandler`, `IContentViewHandler`, `ILayoutHandler`, `IWindowHandler` and friends each
re-declare:

```csharp
new PlatformView PlatformView { get; }   // PlatformView = System.Object | MAUI's own platform type
```

An explicit interface implementation must match that type *exactly*, so a backend that owns its
platform view types cannot implement them - verified as `CS9333` and `CS0738`. Worse,
`ILayoutHandler`/`IContentViewHandler` bind to `Microsoft.Maui.Platform.LayoutViewGroup` /
`ContentViewGroup`, i.e. MAUI's own Tizen types, which an extracted backend must not depend on.

*Impact:* this backend declares `ITizenLabelHandler`, `ITizenContentViewHandler`,
`ITizenPageHandler`, `ITizenLayoutHandler`, `ITizenWindowHandler` and `ITizenApplicationHandler`.
Interop with MAUI Controls is preserved because Controls raises layout operations by *command-mapper
key string* (`Handler.Invoke(nameof(ILayoutHandler.Add), ...)`), and this backend deliberately uses
identical key names - there is a test that locks those strings down.

*Ask:* make `PlatformView` on these interfaces `object`, or make the interfaces generic in the
platform view type.

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

Controls-level remapping (`Layout.RemapForControls` and friends) appends to MAUI's *static*
`LayoutHandler.Mapper`, not to this backend's mappers, so Controls-specific mappings do not reach
these handlers. Wiring that up belongs with the `Maui.Tizen.Controls` layer.

All other handlers (button, entry, image, scroll view, web view, navigation, shell, ...) remain as
raw imported sources and are not yet ported.
