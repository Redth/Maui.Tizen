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

**dotnet/maui#37853 is still open.** Those interfaces are therefore not in the
`11.0.0-preview.7` package this repository builds against and cannot be implemented yet.

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
- `UpstreamHasNotShippedTheseTypesYet` **fails** the moment
  `Microsoft.Maui.Controls.Platform.IModalNavigationPlatform` appears in the referenced assembly,
  with instructions to delete the provisional file.

Until the PR merges, .NET MAUI will not resolve
`IModalNavigationPlatformFactory` — the registration is real and tested, but it binds the
provisional interface, not one the framework knows about.

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
  Keeping them would fire the page lifecycle events twice.
- `_platformModalPages.Add/Remove` — the framework owns the platform stack and updates it *before*
  awaiting the platform, which is why `PushModalAsync` receives a page that is already on
  `IModalNavigationHost.PlatformModalStack`.

Two behaviours worth calling out:

- **Batch pop.** `PopModalAsync` passes `animated && !host.IsBatchPopping`. A `Shell` pop-to-root
  dismisses several modals at once; animating the intermediate ones makes them flash on screen.
- **Back button.** The handler resolves `host.CurrentPage` on every press rather than capturing it,
  because the current page changes as modals come and go.

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
happens at host-build time, before any window exists. They are registered **scoped** as holders
that the Tizen window handler fills in:

```csharp
TizenNuiHostingExtensions.AttachTizenWindow(mauiContext, nativeWindow, navigationStack);
```

The two holders behave differently on purpose:

- `TizenScopedNavigationStack` **throws** when used before attachment. A modal that reports success
  without appearing is worse than a clear failure.
- `TizenScopedWindowBackButton` **records and replays** the handler. The modal platform installs
  its handler on `PageAttached`, which can run before the window handler attaches the native
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
| Dialog placeholder balance, including the fault and buried-placeholder paths | `tests/Controls.UnitTests/TizenModalHostTests.cs` |
| Window-scoped holder semantics | `TizenScopedWindowServiceTests` |
| Provisional contract shape, namespace and expiry | `ProvisionalModalNavigationContractTests` |
| DI registration and lifetimes | `tests/Controls.UnitTests/TizenServiceRegistrationTests.cs` |
| `NuiNavigationStack` | Compile-checked by `eng/verify-nui-sources.sh`; behaviour needs a device |

Placeholder balance in particular used to be device-only. It now runs on the host, which matters
because the failure mode — a stack left permanently unbalanced — wedges every subsequent modal in
the app.
