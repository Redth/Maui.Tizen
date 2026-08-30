# Tizen modal navigation

How modal pages and modal dialogs are presented on Tizen, and why the code is shaped the way it is.

Two different things are called "modal" here and they are handled separately:

| | Modal **pages** | Modal **dialogs** |
|---|---|---|
| Raised by | `Navigation.PushModalAsync` / `PopModalAsync` | `DisplayAlert`, `DisplayActionSheet`, `DisplayPromptAsync` |
| Presented as | A page pushed onto the Tizen `NavigationStack` | A native NUI popup floating above the page |
| Owned here by | `TizenModalNavigationPlatform` | `TizenAlertManagerSubscription` + `TizenModalHost` |

Both end up driving the same window-level `NavigationStack`, which is why both go through the
single Tizen-owned `ITizenNavigationStack` contract.

---

## Modal pages

### The upstream problem

`ModalNavigationManager.Tizen.cs` in dotnet/maui is an **internal partial-class completion**. The
framework's `ModalNavigationManager` declares `PushModalPlatformAsync` / `PopModalPlatformAsync` /
`IsModalPlatformReady` and each platform supplies its half, compiled into
`Microsoft.Maui.Controls` itself. The neutral `Standard` partial only updates logical state, so an
out-of-tree backend gets no rendering at all and there is no DI, factory or provider hook to
replace it.

That makes modal navigation the one area of this migration that could not be delivered against the
shipped .NET MAUI 11 public surface, unlike alerts (`IAlertManager`, dotnet/maui#36633) and
gestures (`IGesturePlatformManagerFactory`, dotnet/maui#36655).

### The seam

[dotnet/maui#37853](https://github.com/dotnet/maui/pull/37853) adds the missing extensibility
point, following the shape already established by the alert and gesture seams:

```csharp
public interface IModalNavigationPlatform : IDisposable
{
    bool IsReady { get; }
    Task PushModalAsync(Page modal, bool animated);
    Task PopModalAsync(Page modal, bool animated);
    void PageAttached();
}

public interface IModalNavigationPlatformFactory
{
    IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host);
}
```

The framework keeps ownership of everything shared and difficult — the cross-platform modal stack,
`Appearing`/`Disappearing`/`NavigatedTo`/`NavigatedFrom`, `Window.ModalPushing`/`ModalPopped`,
`Shell` batch-pop semantics, and the reconciliation loop. Only the visual presentation of a single
push or pop is delegated.

### ⚠️ Provisional status

**dotnet/maui#37853 has not been adopted in a published MAUI package.** Those interfaces are not
in the `11.0.0-preview.7` package this repository builds against and cannot be implemented yet.

`Core/Platform/Modal/ProvisionalModalNavigationContracts.cs` carries copies of the three
interfaces, with member shapes taken verbatim from the PR, so the Tizen implementation is written
against the final contract today. Adopting the real interfaces is then a namespace change on two
types plus deleting that file — no logic moves.

The copies live in `Microsoft.Maui.Platforms.Tizen`, **not** `Microsoft.Maui.Controls.Platform`.
Re-declaring a MAUI type name inside a MAUI namespace would collide (`CS0433`) for any consumer
that also references MAUI's own build once the PR lands.

`ProvisionalModalNavigationContractTests` keeps this honest:

- the member shape of each provisional interface is asserted against the PR's shape, so a copy
  cannot silently drift;
- the namespace rule is asserted, so a copy cannot silently become a collision;
- `PinnedMauiPackageDoesNotContainTheProvisionalTypes` **fails** the moment
  `Microsoft.Maui.Controls.Platform.IModalNavigationPlatform` appears in the referenced assembly,
  with instructions to delete the provisional file.

Until the seam is adopted and published, .NET MAUI will not resolve
`IModalNavigationPlatformFactory` — the registration is real and tested, but it binds the
provisional interface, not one the framework knows about.

Packing `Maui.Tizen.Controls` is also blocked with `MAUITIZEN0104` while the three contracts are
absent from the actual pinned `Microsoft.Maui.Controls.dll`. This is a metadata inspection of the
consumed binary, not an assumption based on upstream source status. Availability alone is
insufficient: the local provisional contract file must be absent and the compiled platform/factory
and service registration must reference the real `Microsoft.Maui.Controls.Platform` contracts.

### What the Tizen implementation does

`TizenModalNavigationPlatform` is the port of `ModalNavigationManager.Tizen.cs` reshaped onto the
seam:

| Upstream member | Here | Note |
|---|---|---|
| `IsModalPlatformReady => true` | `IsReady => true` | Tizen has no deferred readiness, so `RequestSync` is never needed. |
| `PushModalPlatformAsync` | `PushModalAsync` | Realize the page, push the native view. |
| `PopModalPlatformAsync` | `PopModalAsync` | Pop the stack, release the page's handler. |
| `OnPageAttachedHandler` | `PageAttached` | Install the back-button handler. |
| `OnBackButtonPressed` | back-button delegate | Resolved through the host on every press. |

Deliberately **absent** from the port:

- `SendDisappearing()` / `SendAppearing()` — the framework raises these itself under the seam.

Animated push and pop transitions retain explicit operation ownership and generations. Window
teardown invalidates in-flight work but does not release a view still owned by a native transition;
the completing operation observes invalidation, confirms native removal, and releases exactly
once. Failed identity removal leaves the live entry tracked for reconciliation or a later
best-effort teardown retry.

Dialog placeholders use the same rule. A pending `PushAsync` owns its placeholder until its
post-await generation check, so window disposal cannot free a view that native code may still
insert. Concurrent placeholder pushes also share a reference-counted `ShownBehindPage` lease: the
first push captures the original value and the final push restores it once, including failure and
out-of-order completion paths.

Framework-owned `Dispose` attempts every tracked cleanup and reports failures through the
configured logger (or diagnostics fallback) without throwing into `Window.Destroying` or handler
replacement. Caller-owned push/pop operations still surface their own transition and cleanup
failures.

Page release also distinguishes handler ownership from native-view ownership. If a replaced
`IDisposable` handler no longer references the captured platform view, the handler is disposed
first and the orphaned captured view is then released independently. A stack-disposed flag prevents
double release, and a newer `Page.Handler` is never cleared.

That ownership is recorded when realization chooses either `IViewHandler.ContainerView` or
`IElementHandler.PlatformView`. Release checks both live handler properties: a distinct container
still owned by a disposable handler is left to that handler, while a disconnected handler that no
longer references the capture cannot leak it.
  Keeping them would fire the page lifecycle events twice.
- `_platformModalPages.Add/Remove` — the framework owns the platform stack and updates it *before*
  awaiting the platform, which is why `PushModalAsync` receives a page that is already on
  `IModalNavigationHost.PlatformModalStack`.

Two behaviours worth calling out:

- **Batch push.** `PushModalAsync` passes `animated && !host.IsBatchPushing`, suppressing animation
  while the framework applies several modal presentations as one batch.
- **Batch pop.** `PopModalAsync` passes `animated && !host.IsBatchPopping`, suppressing animation
  while the framework dismisses several modal presentations as one batch.
- **Back button.** Core's per-window back route is the sole page-navigation owner; the modal
  platform does not register a second callback that could dispatch the top page twice.

### Cross-window page reuse

A page can be popped from one window and pushed modally on another. Its existing handler is bound
to the *originating* window's `IMauiContext`, and reusing it would realize the page into the wrong
window's view tree.

`TizenModalPageRealizer` therefore disconnects and discards any handler whose `MauiContext` is not
the target one, and builds a fresh handler from the target window's handler factory. When the
handler already belongs to the target window it is reused, but the context is re-applied
unconditionally so a handler created without one — or whose context was cleared on disconnect — is
always realized against the right window.

### Awaiting the navigation stack

`ITizenNavigationStack.PushAsync` and `PopAsync` are awaited, never fire-and-forget. Discarding
those tasks swallows the fault and lets a dialog open over a stack that has not actually taken the
placeholder, which then unbalances the pop. `TizenModalHostTests` covers both the ordering and the
push/pop failure paths.

### Realizing a page without `ToPlatform`

`ModalNavigationManager.Tizen.cs` called `modal.ToPlatform(WindowMauiContext)`.
`Microsoft.Maui.Platform.ElementExtensions.ToPlatform` is compiled per platform and has **no Tizen
build** now that Tizen left the MAUI repository.

`TizenModalPageRealizer` does the same work using only public, platform-neutral API — resolve a
handler from `IMauiContext.Handlers`, give it the context and the virtual view, then take its
container or platform view. A useful side effect is that modal page realization is unit testable on
the host.

---

## Modal dialogs

Alert, action sheet and prompt dialogs are native NUI popups, not stack entries. The stack still
has to know something modal is on screen so that back-button handling and page ordering stay
correct, so a placeholder entry is pushed for the duration of the dialog.

This is the port of `NavigationStackExtensions.PushDummyPopupPage`, with one deliberate deviation:
**exceptions are not swallowed**. The original swallowed everything, which was survivable only
because it published the dialog result from inside that scope; a swallowed failure left the
awaiting `DisplayAlertAsync` caller pending forever. The placeholder is still always popped, but
the failure now propagates so `TizenAlertManagerSubscription` can fault the caller.

If the placeholder is no longer on top when the dialog closes — something else was pushed while it
was open — it is removed by identity rather than popped, matching the original.

---

## Window-scoped services

`ITizenNavigationStack` and `ITizenWindowBackButton` wrap objects the window owns, but registration
happens at host-build time, before any window exists. They are registered **scoped** as holders.
Core publishes the native window into the window `IMauiContext`. The Controls scoped initializer
creates its own navigation overlay, attaches it lazily on the first modal operation so it remains
above Core's root content, and fills the holders:

```csharp
TizenNuiHostingExtensions.AttachTizenWindow(mauiContext, nativeWindow, controlsNavigationStack);
```

The two holders behave differently on purpose:

- `TizenScopedNavigationStack` **throws** when used before attachment. A modal that reports success
  without appearing is worse than a clear failure.
- `TizenScopedWindowBackButton` **records and replays** the handler. The modal platform installs
  its handler on `PageAttached`, which can run before the scoped initializer attaches the native
  window, and a missing back button is not fatal — presses just fall through to the platform
  default.

### Back button ownership

No back-button implementation is supplied by this layer. Upstream, the handler registry lives in
`Microsoft.Maui.Platform.WindowExtensions` and is consumed by `MauiApplication`, both of which
belong to the Tizen **Core** layer rather than Controls. Duplicating that registry here would
create a second, competing source of truth for back-button routing, so
`AttachTizenWindow` takes the Core layer's implementation as an optional argument instead.

---

## Verification

| Layer | How it is verified |
|---|---|
| Modal page push/pop, animation flags, batch pop, back button, disposal | `tests/Controls.UnitTests/TizenModalNavigationPlatformTests.cs` |
| Factory behaviour and per-window isolation | `TizenModalNavigationPlatformFactoryTests` |
| Dialog placeholder balance, push/pop failure propagation, async completion ordering, buried placeholder | `tests/Controls.UnitTests/TizenModalHostTests.cs` |
| Cross-window page reuse and target-context application | `TizenModalNavigationPlatformTests` |
| Window-scoped holder semantics | `TizenScopedWindowServiceTests` |
| Provisional contract shape, namespace and expiry | `ProvisionalModalNavigationContractTests` |
| DI registration and lifetimes | `tests/Controls.UnitTests/TizenServiceRegistrationTests.cs` |
| `NuiNavigationStack` | Type-checked against TizenFX by `tests/Maui.Tizen.Controls.RefPackCompile`; behaviour needs a device |

Placeholder balance in particular used to be device-only. It now runs on the host, which matters
because the failure mode — a stack left permanently unbalanced — wedges every subsequent modal in
the app.
