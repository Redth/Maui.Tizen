# BlazorWebView on the standalone Tizen backend

`Maui.Tizen.BlazorWebView` provides `BlazorWebView` support for Tizen after Tizen was removed from
`dotnet/maui`. It is a standalone package: it plugs into the shared MAUI BlazorWebView control through the
public extensibility contract added by [dotnet/maui#36658][pr36658] and never redefines any MAUI type.

- **Package id:** `Maui.Tizen.BlazorWebView`
- **Assembly / namespace:** `Microsoft.Maui.Platforms.Tizen.BlazorWebView`
- **Target framework:** `net11.0-tizen11.0` (single TFM; .NET 11 is the floor, .NET 10 is not supported)
- **Depends on:** `Maui.Tizen.Core` — the handler derives from its `TizenViewHandler<,>`
- **Requires:** a MAUI build that contains dotnet/maui#36658 — pinned in `Directory.Packages.props`
  to the development baseline in `eng/baselines.json`

## Registration

```csharp
using Microsoft.Maui.Platforms.Tizen.BlazorWebView;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();

builder.Services.AddTizenBlazorWebView();
```

`AddTizenBlazorWebView()` is exactly:

```csharp
builder.Services
    .AddMauiBlazorWebView()                          // shared MAUI Blazor services
    .UsePlatformHandler<TizenBlazorWebViewHandler>(); // replaces the default handler
```

If you already hold an `IMauiBlazorWebViewBuilder`, use `builder.UseTizenBlazorWebView()`.

### Ordering matters

Handler registration is **last-registration-wins**. `UsePlatformHandler` replaces the `IBlazorWebView`
handler registration made by `AddMauiBlazorWebView()`, so it must run *after* it. If a downstream library
calls `AddMauiBlazorWebView()` later in the pipeline, that call silently restores the default handler and
your Blazor content will never render on Tizen. Register the Tizen handler last, after every other MAUI
Blazor configuration.

Both behaviors are covered by tests in `tests/Maui.Tizen.BlazorWebView.Tests/RegistrationTests.cs`
(`RegisteringBeforeAddMauiBlazorWebViewLosesToTheDefaultHandler`,
`RegisteringLastWinsOverADownstreamAddMauiBlazorWebView`).

## Migrating from the built-in Tizen Blazor handler

Up to and including .NET 9 / MAUI 9, Tizen Blazor support shipped inside
`Microsoft.AspNetCore.Components.WebView.Maui` as the Tizen half of the partial
`Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler` class
(`src/BlazorWebView/src/Maui/Tizen/BlazorWebViewHandler.Tizen.cs` upstream). It was picked up implicitly
whenever you targeted a `-tizen` TFM and called `AddMauiBlazorWebView()`.

That handler no longer exists for Tizen: the .NET 11 MAUI Blazor package ships no Tizen TFM at all.

| Before (built-in) | After (this package) |
| --- | --- |
| `net8.0-tizen8.0` / `net9.0-tizen7.0` | `net11.0-tizen11.0` |
| `builder.Services.AddMauiBlazorWebView();` | `builder.Services.AddTizenBlazorWebView();` |
| Handler type `Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler` | `Microsoft.Maui.Platforms.Tizen.BlazorWebView.TizenBlazorWebViewHandler` |
| Manager type `Microsoft.AspNetCore.Components.WebView.Maui.TizenWebViewManager` | `Microsoft.Maui.Platforms.Tizen.BlazorWebView.TizenWebViewManager` |
| File provider `TizenMauiAssetFileProvider` (internal) | `TizenAssetFileProvider` (internal) |
| Handler resolved implicitly by TFM | Handler registered explicitly through `UsePlatformHandler` |

### Step by step

1. Retarget the app to `net11.0-tizen11.0`. There is no .NET 10 path.
2. Add a `PackageReference` to `Maui.Tizen.BlazorWebView`.
3. Replace `AddMauiBlazorWebView()` with `AddTizenBlazorWebView()` — or append
   `.UsePlatformHandler<TizenBlazorWebViewHandler>()` to your existing call — and make sure it is the last
   MAUI Blazor registration in the pipeline.
4. Replace any `typeof(BlazorWebViewHandler)` / `ConfigureMauiHandlers` customization that referenced the
   built-in Tizen handler with `TizenBlazorWebViewHandler`.
5. Replace `BlazorWebViewInitializedEventArgs.WebView` usages (see below).

### Behavior that is unchanged

Everything above the native boundary was ported as-is from `dotnet/maui` `net11.0`:

- app origin `http://0.0.0.0/` and the `Blazor.start()` init script;
- the `BlazorWebView:<key>` user-agent tag plus the process-wide
  `WebContext.RegisterHttpRequestInterceptedCallback` routing (Tizen registers interception per web
  context, not per web view, so requests must be routed back to the owning handler);
- static content responses: query strings stripped before lookup, host-page fallback only for URLs ending
  in `/`, raw `HTTP/1.0 <status> <message>` header blocks, and `Ignore()` for anything else;
- caching disabled by default (dotnet/maui#8279) with opt-in through
  `BlazorWebView.StaticContentCacheControlProvider`, backed by the same bounded LRU cache, the same
  `Range` / `Authorization` / `no-store` exclusions and the same `no-cache` / `max-age=0` / `Pragma`
  revalidation rules;
- the `BlazorHandler` JavaScript message bridge and `__dispatchMessageCallback` message dispatch;
- root component add/remove including collection-change handling;
- `TryDispatchAsync` returning `false` before Blazor starts.

### Behavior that changed

- **`BlazorWebViewInitializedEventArgs.WebView` is not populated.** That property is declared only in the
  platform-specific builds of `Microsoft.AspNetCore.Components.WebView.Maui`, and the package no longer
  produces a Tizen build, so on the neutral assembly the property does not exist. Read the native control
  from the handler instead:

  ```csharp
  var nuiWebView = ((TizenBlazorWebViewHandler)blazorWebView.Handler!).PlatformView;
  ```

  A platform-neutral property on `BlazorWebViewInitializedEventArgs` would remove this gap; see
  [Upstream API assumptions](#upstream-api-assumptions).
- **Static content hot reload is not wired up.** The built-in handler called
  `StaticContentHotReloadManager.AttachToWebViewManagerIfEnabled`, which is `internal` to
  `Microsoft.AspNetCore.Components.WebView.Maui` and unreachable from a third-party handler. Component hot
  reload is unaffected; only hot reload of `wwwroot` assets is lost.
- **Commands are mapped.** The handler supplies a `CommandMapper` chained to
  `ViewHandler.ViewCommandMapper`, so focus, unfocus, invalidate-measure, frame and z-index commands
  reach the platform view. A handler constructed with a null command mapper silently drops every
  `IView.Invoke`.
- **Connect and disconnect are symmetric.** Tizen exposes no way to remove a JavaScript message handler
  or unregister the request-interception callback, so the handler installs the bridge at most once per
  NUI WebView and restores the original user agent on disconnect. Without that, a
  disconnect/reconnect cycle would deliver every JS message twice and append a second routing suffix to
  the user agent.
- **Routing keys are unique.** The key that routes an intercepted request back to its owning handler
  comes from a monotonic counter, not `GetHashCode` — two live handlers can share a hash code, which
  would serve one BlazorWebView's content into another. Key parsing reads only the key, so another
  component appending to the user agent cannot break routing.
- **`WebResourceRequested` is not raised.** `IBlazorWebView` inherits it from
  `IWebRequestInterceptingWebView`, but `WebResourceRequestedEventArgs` has only `internal` constructors
  and no Tizen shape, so a third-party backend cannot construct the argument. Static content is still
  fully served through the Tizen interception path; only the public notification is unavailable.
  `WebResourceRequestedTests` pins this and fails if MAUI ever makes the type constructible.
- **Disposal is now explicit.** `DisconnectHandler` disposes the `TizenWebViewManager` (fire-and-forget by
  default, blocking when the `BlazorWebView.UseBlockingDisposal` `AppContext` switch is set), unsubscribes
  the root-component collection, drops the handler from the routing table and clears the response cache.
  The upstream Tizen handler leaked the manager.
- **Every host-page route is bootstrapped, not just `/`.** Blazor's `Blazor.start()` is injected on any
  document load the request processor answered with the host page — so deep client-side routes
  (`/CustomStart/SomeData`) and URLs carrying a query string (`/?returnUrl=…`) initialize correctly.
  Routes are classified on the *path*, with the query stripped first, so a query string can neither make
  a document look like an asset nor the reverse. Injecting only at the exact origin — the previous
  behavior — left every non-root start path with a blank page.
- **The interception registration is rooted for the process lifetime.** Registering an interception
  callback stores it on a `WebContext` and hands native a pointer to a proxy *owned by that context*, so
  rooting only our own callback is not enough: if the context is collected the proxy dies with it and
  every request silently 404s under GC pressure. Both the context and the callback are held strongly and
  permanently — the platform offers no way to unregister, and a process has only a handful of contexts.
  Individual handlers are still routed weakly, so a disconnected BlazorWebView is collected normally.
- **Root-component changes are reconciled, not applied as deltas.** Each change previously started its
  own asynchronous pass carrying a snapshot taken when the event was raised; because those passes await,
  they interleaved, and `Add` immediately followed by `Clear` could leave a component mounted in a
  collection the application had emptied. Passes are now serialized and coalesced by
  `CoalescingReconciler`, and each pass re-reads the desired collection, so the last pass always observes
  the final state. Reconciliation stays on the Blazor dispatcher throughout.


## Repository layout

| Path | Purpose |
| --- | --- |
| `src/Maui.Tizen.BlazorWebView/Handlers` | Handler, web view manager, file provider, dispatcher |
| `src/Maui.Tizen.BlazorWebView/Hosting` | `AddTizenBlazorWebView` / `UseTizenBlazorWebView` |
| `src/Maui.Tizen.BlazorWebView/Internal` | User-agent routing, request processing, logging, query strings |
| `src/Maui.Tizen.BlazorWebView/StaticContent` | Response cache, cache policy, `Cache-Control` resolution |
| `src/Maui.Tizen.BlazorWebView/Tizen` | Raw dotnet/maui import, kept for provenance and **not compiled** |
| `src/Maui.Tizen.BlazorWebView/PublicAPI` | Imported net-tizen API baselines, consumed by `eng/api-baselines/` |
| `samples/BlazorWebView/Maui.Tizen.BlazorWebView.Sample` | Minimal Blazor sample |
| `tests/Maui.Tizen.BlazorWebView.Tests` | Host-side verification harness |

Compile items come from `eng/Maui.Tizen.BlazorWebView.Sources.props`, following the same pattern as
`eng/Maui.Tizen.Core.Sources.props`: the default glob from `eng/targets/TizenPackage.props` stays off, so
the imported `Tizen/` sources — the Tizen half of the partial `BlazorWebViewHandler` that now ships
complete in the shared MAUI package — can never be pulled into the build by accident.

### Relationship to `Maui.Tizen.Core`

`TizenBlazorWebViewHandler` derives from `Microsoft.Maui.Platforms.Tizen.Handlers.TizenViewHandler<,>`
rather than directly from MAUI's `ViewHandler<,>`. That is a **correctness requirement**, not a
convention: `TizenLayoutHandler` and `TizenContentViewHandler` reach a child through
`ITizenPlatformViewHandler` when adding it to the native tree, so a `BlazorWebView` whose handler did not
implement that interface would simply never be parented. Deriving also inherits Tizen measurement,
arrangement, focus propagation and disposal, and the dispatcher is reached through the public
`IDispatcher` the core registers — no Tizen-specific dispatcher type is referenced.

The package cannot be published yet for a reason that has nothing to do with Blazor: through
`Maui.Tizen.Core` it depends on `Tizen.UIExtensions.NUI` 0.9.2, which transitively declares a .NET 6-era
`Microsoft.Maui.Graphics`. It therefore carries the same `MAUITIZEN0101` pack guard as
`Maui.Tizen.Core`.

`Internal`, `StaticContent` and the dispatcher exist because the upstream equivalents (`QueryStringHelper`,
`StaticContentResponseCache`, `StaticContentCacheControl`, `MauiDispatcher`, `Log`) are all `internal` to
`Microsoft.AspNetCore.Components.WebView.Maui`. They were re-implemented here rather than accessed through
reflection or `InternalsVisibleTo`, so this package depends only on public MAUI API.

## Building and testing

### Static assets

A Blazor Hybrid app's `wwwroot` — and the framework's own `_framework/blazor.webview.js` — reach the app
as `StaticWebAsset` items. Something has to turn those into `MauiAsset` items before the Tizen resource
pipeline can package them into `res/`.

Upstream does that in `Microsoft.AspNetCore.Components.WebView.Maui`'s
`ConvertStaticWebAssetsToMauiAssets` target — but that package ships it in **`build/` only, with no
`buildTransitive/`**. NuGet does not flow `build/` past a direct reference, so an app that acquires the
MAUI Blazor package transitively (exactly what referencing `Maui.Tizen.BlazorWebView` does) never gets it.
The build succeeds and the app 404s on every asset, including `blazor.webview.js`, so Blazor never starts.

`Maui.Tizen.BlazorWebView` therefore carries the conversion itself, in `buildTransitive/` so it actually
flows. **Apps need no asset wiring of their own** — in particular, copying `wwwroot` to the output
directory does nothing, because `TizenAssetFileProvider` reads from the application's *resource*
directory, and it could never have covered `blazor.webview.js`, which is not in the project at all.

The full chain, and who owns each hop:

| Hop | Owner |
| --- | --- |
| `StaticWebAsset` → `MauiAsset` (with `Link` / `TargetPath`) | **this package**, `buildTransitive/Maui.Tizen.BlazorWebView.targets` |
| `MauiAsset` → `MauiProcessedAsset` | Resizetizer (`ProcessMauiAssets`) |
| `MauiProcessedAsset` → `TizenResource` (`TizenTpkFileName` from `%(Link)`) | `Maui.Tizen.Build.Tasks` |

`Link` is populated because `MauiTizenProcessAssets` derives `TizenTpkFileName` from it. The `wwwroot`
path prefix is load-bearing: it is the `contentRootDir` the handler derives from `BlazorWebView.HostPage`,
so `HostPage = "wwwroot/index.html"` requires `res/wwwroot/index.html` on device.

The conversion is idempotent — an app that *also* references the MAUI Blazor package directly gets the
upstream conversion as well, and duplicate `MauiAsset` entries would produce duplicate resources. Set
`TizenBlazorWebViewConvertStaticWebAssets=false` to opt out entirely.

The conversion is reached two ways, and both are tested. It registers through
`MauiTizenAssetProviderTargets`, the provider contract `Maui.Tizen.Build.Tasks` publishes, and it also
carries a direct `BeforeTargets` hook as a fallback for graphs that do not include that package at all.
MSBuild runs a target at most once per build, so being reached both ways is harmless.

`AssetPipelineTests` runs MSBuild against a real Razor project and asserts that `index.html`, a nested
app asset and `_framework/blazor.webview.js` all arrive as `MauiAsset` with the right `TargetPath`, with
no duplicates and no `.gz`/`.br` variants. Because the fixture imports only this package, those
assertions reach the conversion through the fallback — so the fixture also defines a stand-in for
`MauiTizenCollectProvidedAssets` and drives it directly, which exercises the registration in isolation
(none of the targets named in `BeforeTargets` are scheduled by that entry point). A further test asserts
both entry points yield identical assets, since an application gets one or the other depending on
whether `Maui.Tizen.Build.Tasks` is in the graph.

#### Pre-compressed variants

Assets are served from local storage by the request interceptor, which performs no content negotiation,
so SDK-generated `.gz`/`.br` copies are never requested and are pure TPK bloat. The conversion drops any
asset with `AssetRole='Alternative'`.

The filter deliberately keys on `AssetRole` **alone**, not on the file extension:

- a user file that merely ends in `.gz` is a `Primary` asset and is kept, and
- a compressed variant is dropped even when its `Identity` is an SDK-generated temp file whose name does
  not end in `.gz` or `.br`.

Testing this required care, and the first version of the test was worthless. A plain Razor build emits
**no** compressed assets at all — compression is a Blazor WebAssembly publish-time concern, and the
fixture is deliberately a plain Razor app so it runs without the Samsung workload. So "no `.gz` survived"
held trivially, and the exclusion was in fact completely broken (its condition was OR-ed with
`'$(CompressionEnabled)' != 'false'`, which is true whenever the property is unset — i.e. almost always —
short-circuiting the entire filter) while the suite stayed green.

The fixture therefore seeds `Alternative` variants shaped like the SDK's behind
`-p:SeedCompressedAssets=true`, and three tests hold the line: one asserts the variants really are
present in the conversion's *input*, one asserts they are absent from its output, and one asserts that
`wwwroot/data/archive.gz` — a genuine user file discovered as `Primary` — still ships. That last one is
what pins the `AssetRole` semantics; without it a filter keyed on the extension would pass.

Asset file names must not be hard-coded: the SDK fingerprints static web assets in some configurations
(`blazor.webview.<hash>.js`), so the tests match by prefix and extension and assert on the `wwwroot`
content-root prefix, which is the property that actually determines runtime reachability.

### Release gates

Two things are **not** proven by the host-side suite and must be verified before this package ships.
They are recorded here as gates rather than described as existing coverage:

1. **Real end-to-end asset flow.** The tests above stop at `MauiAsset`, which is exactly where
   `Maui.Tizen.Build.Tasks` takes over. The full `StaticWebAsset → MauiAsset → MauiProcessedAsset →
   TizenResource → res/wwwroot` chain can only be exercised once that package is in the base branch;
   until then the fixture supplies the provider contract itself, so it proves this package's half only.
2. **Produced package layout.** No test installs an actual `.nupkg` from an isolated cache and verifies
   the `buildTransitive/` layout and dependency closure a real consumer resolves. Doing so needs the
   Samsung workload, since the package's own TFM cannot be restored without it.


### The Tizen workload gate

`net11.0-tizen11.0` **cannot be restored or built anywhere today**. No 11.0.100-band Samsung workload
manifest (`Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7`) is published — only the 9.0.100 and
10.0.100 bands exist (`eng/baselines.json` → `target.workloadManifest.status: "unavailable"`). On top of
that, the .NET 11 `maui-tizen` workload is now an empty alias that only extends `maui-blazor` and carries
no Samsung packs, so the SDK reports:

```
error NETSDK1139: The target platform identifier tizen was not recognized.
```

Both the product project and the sample set `IsTizenProject`, so the repository-wide
`ValidateTizenWorkloadAvailable` target in `Directory.Build.targets` fails them fast with `MAUITIZEN0001`
rather than letting the build degrade to a neutral TFM. Neither project defines its own gate.

Once Samsung publishes the manifest, install the workload and build normally, or force the gate open with
`-p:TizenWorkloadAvailable=true`.

### Host-side verification harness

Everything above the native NUI boundary is verified today:

```bash
dotnet test tests/Maui.Tizen.BlazorWebView.Tests
```

It also runs as part of the workload-free lane, `eng/build-workload-free.sh`.

The harness targets `net11.0`, defines `TIZEN`, and compiles the *same* BlazorWebView sources, the core
slice sources it derives from, and the sample head, against the pinned `Samsung.Tizen.Ref.API15`
reference assemblies (fetched with `PackageDownload`, since the pack is published with package type
`DotnetPlatform` and cannot be consumed through `PackageReference`). Those assemblies carry full metadata
and are not marked with `ReferenceAssemblyAttribute`, so Tizen-derived types load and can be reflected
over.

It combines the two roles the core backend splits across `Maui.Tizen.Core.RefPackCompile` and
`Maui.Tizen.Core.UnitTests`. The core needs inert stand-ins for its unit lane because it executes code
paths that live behind `#if TIZEN`; nothing here does, so real reference assemblies serve both purposes
and no stubs are required.

It is **not** a neutral fallback for the product: it never packs and never produces a shippable assembly.

What is covered: registration and ordering, the asset file provider, request mapping, static content
response shaping and caching, user-agent routing, handler construction and lifecycle (including that the
handler really does implement `ITizenPlatformViewHandler`), root component validation, package/TFM
contract, and the sample's own `MauiProgram` wiring.

What cannot be covered without a device or emulator: creating a real `Tizen.NUI.BaseComponents.WebView`,
`WebContext.RegisterHttpRequestInterceptedCallback`, `LoadUrl`, `EvaluateJavaScript` and the JavaScript
bridge round trip. Those cross into native Tizen libraries.

## Public API baseline

The package's baseline lives in `src/Maui.Tizen.BlazorWebView/PublicAPI/slice/` and describes **the
assembly this package actually emits** — the handler, the web view manager and the registration
extensions. The project opts in explicitly with `EnablePublicApiAnalyzer` and `AdditionalFiles`, per the
contract documented in `eng/targets/TizenPackage.props`; baselines are deliberately never attached by a
glob.

The `PublicAPI/net-tizen/` baseline that came across with the raw dotnet/maui import describes
`Microsoft.AspNetCore.Components.WebView.Maui` instead: types this package deliberately does not define.
Pointing the analyzer at it would make it demand members the assembly never declares while ignoring every
member it does. It stays where the import put it, as a provenance fixture feeding the API diffing in
`eng/api-baselines/`.

Because the shipping project is workload-gated, the analyzer it references never actually runs.
`tests/Maui.Tizen.BlazorWebView.PublicApi` compiles the same sources on a plain `net11.0` host with
`RS0016`/`RS0017` and friends escalated to errors, so adding, removing or changing a public member fails
the build until the baseline is updated. It consumes the core backend as a compiled reference rather than
as sources, so it validates the BlazorWebView surface only.

## Upstream API assumptions

This package assumes the following about the MAUI core it builds against. All hold in the pinned
development baseline (`eng/baselines.json`), verified at `11.0.0-preview.7.26426.4`.

1. `IBlazorWebViewHandler` is public and exposes `CreateFileProvider(string)` and
   `TryDispatchAsync(Action<IServiceProvider>)` (dotnet/maui#36658).
2. `IMauiBlazorWebViewBuilder.UsePlatformHandler<THandler>()` exists and replaces the `IBlazorWebView`
   handler registration on a last-registration-wins basis.
3. `BlazorWebView.CreateFileProvider` / `TryDispatchAsync` delegate to the handler through
   `IBlazorWebViewHandler` rather than casting to the concrete `BlazorWebViewHandler`.
4. `Microsoft.Maui.Core`'s neutral `net11.0` assembly exposes a usable
   `ViewHandler<TVirtualView, TPlatformView>` for third-party platform backends.
5. `WebViewManager.AddRootComponentAsync` / `RemoveRootComponentAsync` and the `RootComponent` properties
   are public, which is what allows root components to be attached without
   `RootComponent.AddToWebViewManagerAsync` (still `internal`).

### Package pins

`Microsoft.AspNetCore.Components.WebView.Maui` tracks the MAUI development baseline in
`eng/baselines.json`. Its ASP.NET Core dependencies do **not**: `Microsoft.AspNetCore.Components.WebView`,
`Microsoft.AspNetCore.Authorization` and `Microsoft.JSInterop` ship out of `dotnet/aspnetcore` on their own
schedule, and are pinned to the floor declared in the `net11.0` dependency group of the MAUI Blazor nuspec.
With `CentralPackageTransitivePinningEnabled` those pins have to be resolvable, or every transitive
`Microsoft.Extensions.*` dependency fails with `NU1100`.

`AspNetCorePinsMatchTheMauiBlazorNuspecFloor` guards this: it asserts the MAUI Blazor pin tracks the
baseline and that the three ASP.NET Core pins deliberately do not.

There is a trap here worth knowing when the baseline is next bumped. If one of those pins is set to a
version that does not exist for its package, NuGet resolves *upward* to the nearest available build and
reports it through `NU1603`. That resolved version is not the declared dependency — it merely satisfies
`>=` — and pinning to it can drag in packages from a different band, which then appears to justify a
package source mapping that was never actually needed. **Read the nuspec, not the resolution.**

Known gaps worth raising upstream:

- `BlazorWebViewInitializedEventArgs` has no platform-neutral way to surface the native control, so
  third-party backends cannot populate it.
- `StaticContentHotReloadManager` is `internal`, so third-party backends cannot support static asset hot
  reload.
- `RootComponent.AddToWebViewManagerAsync` / `RemoveFromWebViewManagerAsync` are `internal`; their
  validation logic has to be duplicated by every third-party backend.
- `WebResourceRequestedEventArgs` has only `internal` constructors and a per-platform shape with no
  extensibility point, so a third-party backend cannot raise
  `IWebRequestInterceptingWebView.WebResourceRequested` at all.
- `Microsoft.AspNetCore.Components.WebView.Maui` ships `ConvertStaticWebAssetsToMauiAssets` in `build/`
  rather than `buildTransitive/`, so it does not reach transitive consumers. Moving it would let this
  package drop its own copy of the conversion.

All three are being tracked on an upstream net11 lane. When they land, the duplicated helpers under
`Internal/` and `StaticContent/` can collapse back onto the shared implementations, and
`BlazorWebViewInitializedEventArgs.WebView` can be populated normally.

[pr36658]: https://github.com/dotnet/maui/pull/36658
