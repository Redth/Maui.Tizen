# Wave C — navigation and advanced Controls handlers

Status of the Tizen handler migration for navigation, Shell, CollectionView, toolbar and menus.

Wave C is the third handler wave. It is conceptually stacked on the foundation import,
Wave A and Wave B; see [Predecessor dependencies](#predecessor-dependencies).

## What Wave C owns

| Area | Handlers |
| --- | --- |
| Navigation | `TizenNavigationViewHandler`, `TizenFlyoutViewHandler` |
| Tabs | `TizenTabbedPageHandler` |
| Toolbar | `TizenToolbarHandler` |
| Menus | `TizenMenuBarHandler`, `TizenMenuBarItemHandler`, `TizenMenuFlyoutHandler`, `TizenMenuFlyoutItemHandler`, `TizenMenuFlyoutSubItemHandler`, `TizenMenuFlyoutSeparatorHandler` |
| Items | `TizenItemsViewHandler<T>`, `TizenStructuredItemsViewHandler<T>`, `TizenSelectableItemsViewHandler<T>`, `TizenGroupableItemsViewHandler<T>`, `TizenReorderableItemsViewHandler<T>`, `TizenCollectionViewHandler`, `TizenCarouselViewHandler` |
| Shell | `TizenShellHandler`, `TizenShellItemHandler`, `TizenShellSectionHandler` |

All code lives in `src/Maui.Tizen.Controls/Navigation/`.

Per-key mapper coverage, including every explicit no-op classification and every neutral MAUI
mapper key that Tizen does not implement, is in
[`docs/wave-c-mapper-parity.json`](../wave-c-mapper-parity.json) with a readable companion in
[`docs/wave-c-mapper-parity.md`](../wave-c-mapper-parity.md).

Following the convention Wave B established, the manifest is **generated from source**, not
hand-maintained, and `WaveCMapperParityTests.ParityManifestMatchesSource` fails if the two disagree.
Regenerate after an intentional change with:

```bash
MAUI_TIZEN_UPDATE_PARITY=1 dotnet test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj
```

## Naming and namespaces

The in-tree backend compiled *inside* `Microsoft.Maui.Controls`, so it could use the neutral
handler names (`ShellHandler`, `CollectionViewHandler`, ...) and extend Controls types with
`partial class`. Out-of-tree neither is possible, and reusing the neutral names would collide with
the handlers the MAUI package already ships.

Every Wave C type is therefore `Tizen`-prefixed and lives under the policy namespace
`Microsoft.Maui.Platforms.Tizen` (see `eng/baselines.json` >
`policy.newImplementationNamespacePrefix`):

- `Microsoft.Maui.Platforms.Tizen.Handlers` — handlers
- `Microsoft.Maui.Platforms.Tizen.Platform` — platform views, adaptors, extensions
- `Microsoft.Maui.Platforms.Tizen.Adapters` — replacements for internal Controls APIs

A consequence worth stating plainly: because the handlers no longer mutate the neutral static
mappers through the internal `RemapForControls` hook, each Tizen handler declares one complete
mapper and is registered directly (`AddHandler<Toolbar, TizenToolbarHandler>()`). That also removes
an ordering hazard — `RemapForControls` had to run before the first handler instance was
constructed.

## Internal API coupling: what was removed and what remains

The in-tree Tizen backend depended on nine `internal` members of `Microsoft.Maui.Controls`. Each
was verified against the shipped public assembly by compiling a probe, not by reading source.
Eight now have behaviour-preserving public-API replacements; one has no public equivalent at all.

| ID | Internal member | Resolution |
| --- | --- | --- |
| 0001 | `Shell.GetBindableObjectWithFlyoutItemTemplate` | Reimplemented in `ShellTemplateResolver` — **partially**, see below |
| 0002 | `ViewExtensions.FindParentOfType<T>` | Reimplemented in `ShellElementTree` over the public `IElement.Parent` chain |
| 0003 | `Shell.GetCurrentShellPage()` | Reimplemented in `ShellElementTree` |
| 0004 | `Shell.GetEffectiveValue<T>` | Reimplemented in `ShellElementTree` |
| 0005 | `Internals.BooleanBoxes` | Dropped; plain `bool`. Allocation detail with no behavioural meaning |
| 0006 | `View.IsItemSelected` | Replaced by `ItemSelectionState` driving `VisualStateManager` |
| 0007 | `DataTemplateExtensions.SelectDataTemplate` | Reimplemented in `ShellTemplateResolver` |
| 0008 | `Shell.Toolbar` | Accessibility only — `IToolbarElement.Toolbar` is public |
| 0009 | `Toolbar.DrawerToggleVisible` | Provisional `ToolbarDrawerToggle`; upstream dotnet/maui#37863 adds `IToolbarDrawerToggleVisible` **additively** (open) |

`Adapters/UpstreamApiRequests.cs` carries the same list in machine-readable form, with the concrete
API being requested upstream in each case. The source tests fail if an adapter exists without a
matching entry, so the list cannot rot.

### Remaining coupling and known behavioural gaps

Two items are not fully resolved and should not be described as "done":

1. **`MenuShellItem` flyout templates (request 0001, upstream dotnet/maui#37862 — OPEN).** Upstream's helper has a second redirect:
   for a `MenuShellItem` it forwards to the wrapped `MenuItem` when that item sets
   `Shell.MenuItemTemplateProperty`. `MenuShellItem` *and* its `MenuItem` property are both
   `internal`, so an out-of-tree backend cannot express this branch at all — not awkwardly, not at
   all, short of reflection. A bare `MenuItem` in a flyout therefore falls back to the shell-level
   item template.

   Upstream [dotnet/maui#37862](https://github.com/dotnet/maui/pull/37862) proposes a public
   contract for this — `Shell.IsFlyoutItemTemplateSet`, `Shell.GetFlyoutItemTemplateSource` and
   `Shell.GetFlyoutItemTemplateProperty`, used with the already-public
   `IShellController.GetFlyoutItemDataTemplate`. Note the shape differs from the internal helper
   Wave C reimplemented, so adoption is a rewrite of the adapter rather than a rename.

   **The PR is open and still being designed, so nothing here adopts it.** The adapter stays
   provisional until the design merges *and* ships in a referenced package.

   The expiry test is deliberately **name-agnostic**, and that is worth explaining. The proposed
   API has now changed shape twice while the adapter sat here: the internal
   `GetBindableObjectWithFlyoutItemTemplate`, then a three-method contract
   (`IsFlyoutItemTemplateSet` / `GetFlyoutItemTemplateSource` / `GetFlyoutItemTemplateProperty`),
   and now a single resolve-style call. Each time the test named members explicitly it silently
   stopped detecting anything — which is worse than no test, because a green build then *implies*
   the adapter is still needed. It now matches the concept (any new public `Shell` member about a
   flyout item template) and is itself covered by table-driven tests proving it fires for the
   resolve-style shape, the three-method shape and plausible alternatives, while ignoring
   pre-existing members.

2. **`Toolbar.DrawerToggleVisible` (request 0009).** `IToolbar` publishes `BackButtonVisible` and
   `IsVisible` but not `DrawerToggleVisible`, which is the third member of the same concept. Wave C
   keeps the flag in a `ConditionalWeakTable` attached to the toolbar instance. Tizen is the only
   reader and the only writer, so behaviour is equivalent *for this backend*; it would not be
   equivalent for a backend where Controls also needs to observe the flag.

Neither was worked around with reflection, and there is no reflection anywhere in Wave C — the
source tests enforce that.

### Blocked: modal navigation

`ModalNavigationManager` is an `internal partial class` in `Microsoft.Maui.Controls`, and its
per-platform half is supplied by the in-tree backend. There is no public seam that lets an
out-of-tree backend provide modal push/pop.

Wave C therefore **does not port** `ModalNavigationManager.Tizen.cs`. This is a genuine blocker, not
an omission: modal navigation cannot be implemented off-tree with the current public API, and the
correct fix is an upstream seam rather than anything this repository can do. Secondary toolbar
items, which upstream presented by pushing onto the modal stack, are routed through the
`IToolbarSecondaryActionPresenter` seam instead so that the alerts/dialogs area can own that
presentation without Wave C duplicating it.

## Verification

### Current acceptance and shipping lanes

Wave C now ships in `Maui.Tizen.Controls`; there is no `Maui.Tizen.Controls.Navigation` assembly and
no second public startup extension. `eng/Maui.Tizen.WaveC.Sources.props` is imported by both the
shipping Controls project and `tests/Maui.Tizen.Controls.RefPackCompile`, so the API15 lane compiles
the exact sources and assembly boundary that will ship. Catalog pages compile through
`Maui.Tizen.Controls.ConsumerCompile`.

The former `net9.0-tizen7.0` Wave C validation project and `MauiTizenWaveCAcceptance` switch were
removed. MAUI 9 remains only as the repository's historical package/API provenance baseline; it is
not a shipping or fallback lane.

The integration surfaced and fixed the method-body errors the old disabled gate hid: legacy Core
primitive names, object-typed neutral `PlatformView` results, neutral color/image helpers,
`ViewHandler.MapToolbar`, and the separate empty-adaptor type. Wave C now uses
`TizenToolbarView`, `TizenStackNavigationManager`, `TizenFlyoutView`, `ToPlatformView`,
`ToTizen`, `ToTizenDrawerBehavior`, and the shared typed image service result.

The source suite executes the NUI-free selection, cache, template and toolbar ownership algorithms.
The Core host suite builds the real `UseMauiAppTizenControls<TApp>` production path and verifies all
15 Wave C concrete Controls types replace MAUI's neutral registrations. The RefPack lane proves the
actual handlers and mapper bodies compile against net11 plus Samsung API15, and PublicAPI is owned by
`Maui.Tizen.Controls`.

### Neutral mappers are mutated at runtime

Controls types call `RemapForControls()` when a MAUI host is built, which **adds keys to the static
neutral mappers** — `FlyoutPage` adds `FlyoutLayoutBehavior` to `FlyoutViewHandler.Mapper`. Two
consequences, both of which bit this wave:

**Parity generation was order-dependent.** Reading a neutral mapper before those remaps ran gave a
smaller key set than reading it after, so the generated manifest differed between a local run and CI
on the *same commit and packages* — purely because of test order.

The first fix was **incomplete in a way a green full suite could not reveal**: it forced the remaps
from a static constructor, but C# runs static *field initializers* before the static constructor
body, so `ViewMapperKeys` still snapshotted the un-remapped mapper. The suite passed only because
some earlier test had already initialized Controls; run alone in a fresh process, the parity tests
failed with false `BackgroundColor` gaps.

Every mapper-derived value is therefore **lazy** and routed through
`NeutralMaui.EnsureRemapsBeforeReadingMappers()`, which is idempotent and called from each
mapper-reading entry point rather than from type initialization. Three tests pin it —
`ControlsRemapsAreDeterministic` (the remaps ran), `TheSharedViewMapperSnapshotIncludesControlsLevelKeys`
(stated positively, so it cannot pass by both sides being equally stale) and
`CapturedMapperSnapshotsMatchTheLiveSharedMapper` (snapshot vs. an independent live read).

All three were validated by **removing the guard and confirming each fails** — the first draft of the
third test passed even with the bug present, because it read the live side through a helper that
itself forced the remaps, so both sides went stale together.

Because a green full suite is not sufficient evidence here, `eng/run-parity-isolation-checks.sh`
runs each parity-sensitive test *alone in a fresh process*, and is wired into
`eng/build-workload-free.sh`.

**Three real behaviours were missing.** Wave C declares its own mappers rather than chaining the
neutral ones (the `RemapForControls` hook is unreachable out-of-tree), so every key Controls adds at
runtime must be declared here too or it is silently never dispatched. Once generation was
deterministic, three surfaced — and none of them was a no-op:

| Key | Why it matters |
| --- | --- |
| `FlyoutLayoutBehavior` | A Controls-level property projected into `IFlyoutView.FlyoutBehavior`. Without the mapping, switching Popover↔Split at runtime leaves the drawer in its previous mode. Re-dispatches `FlyoutBehavior`, exactly as upstream does. |
| `ItemsView.IsVisible` (CollectionView, CarouselView) | Controls routes this through the items handler rather than the chained view mapper, because the platform view is the scrolling container. The port had dropped it, so hiding a `CollectionView` did nothing. |

`WaveCNeutralKeyCoverageTests` pins all of it, including a test that proves
`FlyoutLayoutBehavior` genuinely changes the projected `FlyoutBehavior` — so the mapping is
demonstrably necessary rather than merely present. `WaveCLeavesNoNeutralMapperKeyUncovered` asserts
the uncovered set is **empty**, so a newly added neutral key fails the build instead of being
quietly appended to the manifest on the next regeneration.

### Executable verification, where it is possible

Most of Wave C touches Tizen.NUI and so is source-verified plus API15 type-checked. Two pieces are
pure `Microsoft.Maui.Controls` code with no NUI dependency, and those are compiled into
`Maui.Tizen.SourceTests` and **actually executed**:

- **Flyout template resolution.** This is where a real regression lived: a pre-resolved template
  owner reaching `IShellController.GetFlyoutItemDataTemplate`, which picks between
  `MenuItemTemplateProperty` and `ItemTemplateProperty` *from its argument's own type*, silently
  dropping `MenuItemTemplate`. Source analysis cannot catch that class of bug. The tests pin the
  authored-on-item and authored-on-parent cases, that a Shell-level `ItemTemplate` does **not**
  capture a menu item, that no authored template returns `null` so Tizen keeps its own flyout item
  view, that an explicit `null` is a per-item opt-out, and that selectors are returned unresolved.
  The suite was validated by reintroducing the bug and confirming the parent-level test fails.

- **Toolbar ownership.** See below.

### The drawer toggle is a read-only capability

Upstream settled this as an **additive** interface — `IToolbarDrawerToggleVisible` with a get-only
`DrawerToggleVisible`; `IToolbar` is unchanged (dotnet/maui#37863, open). Two consequences that are
behavioural, not cosmetic:

**No write, no latch.** The in-tree backend *wrote* the flag onto the toolbar, and an earlier
revision here reproduced that with a `ConditionalWeakTable`. Upstream removed the write path, so
Wave C now computes the value on read. That also deletes a genuine staleness hazard: a stored flag
is only as fresh as the last code path that remembered to update it, so any state change that did
not route through that path left the toolbar drawing a stale icon.

**Back-precedence, not mutual exclusivity.** The latch stored `drawerToggle && !backButton`,
conflating "a drawer toggle is available" with "a drawer toggle is what we draw". Those are
different questions. The capability stays `true` while a back button is showing; only the renderer
applies precedence, because only one icon fits. `WaveCToolbarDrawerToggleTests` pins exactly that —
the test asserting the capability survives `BackButtonVisible = true` fails under the old latch.

**One slot, three claimants, and an async race.** Back button, drawer toggle and title icon all
render into the same navigation slot, and title icons load asynchronously. A completion callback
that lands after the navigation state moved on would silently overwrite whichever icon is now
correct, and two racing loads could land out of order. `TizenToolbarNavigationSlot` gives every
update a generation and rejects any callback that is superseded — by a newer update, or by the title
icon itself having been replaced. Reviewing the approved upstream head surfaced this: upstream added
the same guard, and Wave C's port had the unguarded callback. Four of the eleven
`WaveCToolbarNavigationSlotTests` fail without it.

One off-tree wrinkle worth recording: `ShellToolbar` is not an `Element`, so there is **no public
path from a toolbar to its shell**. Rather than re-introduce a latch to bridge that, the caller that
already knows the owner passes it in, and a caller that does not gets `false`. On adoption every
call site collapses to `toolbar is IToolbarDrawerToggleVisible { DrawerToggleVisible: true }` and
the owner parameter disappears with the adapter.

### Toolbar ownership is a transfer, not a reference

Core's `ITizenToolbarContainer.SetToolbar` **disposes the toolbar it replaces**. That makes the
inherited implementation — a cached `_toolbar` field, unsubscribed during teardown — unsafe: it can
touch an instance that was already disposed by a later transfer.

`ToolbarOwnership<TToolbar>` centralises the three rules that make it safe: unsubscribe the outgoing
toolbar *before* the transfer, subscribe exactly once to the incoming one, and release idempotently.
It is deliberately generic and NUI-free so those rules are executed on a plain host, including the
disposed-instance case, which a stand-in toolbar reports by throwing `ObjectDisposedException`.

Repeated transfer of the same instance is a no-op rather than a second subscription — a duplicate
would fire the icon handler twice per press, which on a flyout toggle cancels out and presents as
"the toolbar button does nothing".

Runtime disposal and visual behaviour remain device-gated; what is pinned here is the bookkeeping
that decides whether a disposed instance is ever touched.

### Mapper dispatch and handler composition

Every Wave C view handler derives from `TizenViewHandler<,>` and chains
`TizenViewMappers.ViewMapper` / `ViewCommandMapper`; it no longer inherits the neutral platform
no-op mapper bodies. Source tests verify mapper delegate signatures and execute every NUI-free
adapter. The API15 compile binds every platform mapper body, while the production host test verifies
that the one Controls startup path resolves every Wave C concrete type.

NUI views still cannot be instantiated on this macOS host, so actual native mapper execution and
visual behavior remain device-only. That limitation is reported rather than replaced by a neutral
fallback.

### What is still unverified

Runtime behaviour. There is no Tizen emulator or device here, so item recycling, virtualization
performance, navigation animation and Shell lazy content are **compile-verified only**.

## Shell content is actually mounted

Several methods survived the port as empty bodies whose only content was a comment claiming some
other layer did the work. Nothing did. `TizenShellView.UpdateCurrentItem`,
`TizenShellItemView.UpdateCurrentItem` and `TizenShellSectionView.UpdateCurrentItem` were all no-ops,
so the Shell hierarchy was never mounted and **the root rendered blank**. They now implement the real
Shell -> ShellItem -> ShellSection -> current ShellContent chain, disposing the previous item handler
and mounting the new platform view.

Content is created lazily: a section's platform view is built the first time that section becomes
current and reused afterwards, because rebuilding it would silently discard that section's navigation
stack and scroll position. Switching **unmounts without disposing**; disposal happens when a section
is removed or the shell item goes away.

Those rules live in `ShellSectionViewCache<TSection, TPlatformView>`, deliberately generic so it
holds no NUI reference. `TizenShellItemView` is an `NView` and cannot be instantiated off-device, so
without that split lazy creation and content switching could only ever be asserted about - and this
whole class of defect is precisely what "the method exists, so it must work" produced the first time.
`WaveCShellSectionViewCacheTests` executes them.

`TabBarIsVisible` had the same shape of bug one level up: the mapper called `UpdateCurrentItem()`,
which was itself one of the no-ops, so toggling the tab bar at runtime did nothing. It now calls
`UpdateTabBar`, which shows or hides the bar and respects `ShellItemController.ShowTabs`.

The Shell item adaptors now register created views through the same native-to-MAUI lifecycle the
items adaptors use, so recycled rows rebind and `UpdateViewState` drives selection through
`ItemSelectionState`. The default flyout, top and bottom item views author `Normal`/`Selected`
visual states with appearance bindings - without them every item rendered identically whether or not
it was current - and active tab bars use `SingleAlways`, so exactly one tab is selected and tapping
the selected tab cannot empty the selection.

The mounting call sites themselves are NUI and stay device-only; they are pinned by source
invariants in `WaveCShellContentSourceTests`, each with a negative control.

## Selection synchronisation

Selection is a set held on both sides, and the port had two structural defects in keeping them equal.

**Unguarded feedback.** A native selection change wrote `VirtualView.SelectedItem`, whose property
change ran the mapper, which pushed back into the native view, which raised the native event again.
Nothing broke the cycle, so it either recursed until the stack overflowed or churned the collection
mid-enumeration. `ItemSelectionSynchronizer` records the direction of travel and drops the echo -
the same `_updateSelection`/`_updateFromUI` pairing upstream uses.

**Add-only synchronisation.** The old code walked `SelectedItems` requesting a select for each.
Nothing was ever deselected, so removing an item left it selected natively, clearing the selection
was invisible, and `SelectedItem = null` did nothing at all. Synchronisation is now a set difference
in both directions using the native `RequestItemUnselect`, which exists and was simply unused.

**Group headers were selectable.** `IsItemSelectableAt` was defined on the grouped adaptor and
**never called from anywhere** - dead code. Grouped sources interleave headers and footers with real
items in one flat index space, so a header could be tapped and propagated. The filter is now applied
on both sides: positions are dropped before a push, and a header selected natively is rejected *and*
deselected, so the two sides cannot disagree. Rejecting after the event - which is where the unused
filter would have run - is too late, because the header is already highlighted.

The rules live behind `ITizenNativeSelection` so they execute in host tests; the NUI half
(`TizenNativeCollectionSelection`) is a thin forwarder with no decisions in it.

## Selected, focused and enabled interact

A view holder moving to `Selected` is selected and *not* focused, so the stored focus has to be
cleared in the same recomputation. Two calls would recompute twice and, in between, produce a state
that is both selected and focused - which resolves to `Focused` and repaints the row.
`SetItemSelectedAndUnfocused` does it atomically. A test pins that plain `SetItemSelected` on a
focused row really does resolve to `Focused`, so the two cannot be silently swapped back.

`IsEnabled` is observed for tracked views, and here Wave C hit a real upstream limit worth stating
plainly rather than papering over.

`VisualElement.ChangeVisualState` runs from the `IsEnabled` property-changed callback, which fires
**after** both `PropertyChanging` and `PropertyChanged` - measured with a probe, not assumed - and it
applies `Normal` on re-enable with no knowledge of selection. There is no public hook that runs after
it, and `VisualStateGroup` is sealed with no change notification. **This is exactly why the property
Wave C replaced was `internal`:** `View.IsItemSelected` was read *inside* `ChangeVisualState`, so
selection took part in the same recompute instead of racing it.

An earlier revision stopped there and recorded the re-enable path as an unfixable gap. **That was
wrong too.** Dispatching the refresh puts it after the whole set-value operation, including
`ChangeVisualState`, which is the ordering guarantee needed - so the behaviour is correct on device
with no upstream change. `ItemSelectionState.PostRecompute` is the seam; it defaults to the element's
dispatcher and is substituted in host tests, which have none, so the tests exercise the real
sequencing rather than a stand-in for it. When no dispatcher exists the refresh is skipped rather
than run inline, because running it inline would land *before* `ChangeVisualState` and be overwritten
anyway.

**MAUI-TIZEN-API-0006** has now been corrected twice - first from "(none required)", then from
"unfixable" - and stands only as an **ergonomic** request: every backend that renders selection has
to discover this ordering and re-dispatch around it.

## Icon-press routing

The shell view and the toolbar handler both subscribe to the same `IconPressed` event, so exactly one
of them owns any given press. Gating the drawer side on `FlyoutBehavior == Flyout` alone read
*availability* rather than *ownership*: the drawer stays available in flyout mode while a pushed page
shows a back button, so a back press toggled the drawer open **and** popped the stack.

Both call sites now route through one predicate, `ToolbarDrawerToggle.ShouldToggleDrawer`, which asks
the same question the rendering does - is the slot owner the drawer toggle? The `FlyoutPage` handler
had the mirror-image bug: its guard honoured back precedence (`!toolbar.BackButtonVisible`) but not
the drawer capability, so a Split (Locked) flyout - which offers no toggle at all - still opened its
drawer.

`FlyoutLayoutBehavior` compounded that. It is projected into `IFlyoutView.FlyoutBehavior` (Popover ->
Flyout, Split -> Locked), so it changes whether a drawer toggle exists at all. Re-dispatching
`FlyoutBehavior` updates the drawer but not the toolbar, so the hamburger survived a switch to Split
and a switch back to Popover left the slot empty. `MapFlyoutLayoutBehavior` now also refreshes the
toolbar's leading slot.

Neither call site can be instantiated in a host test - one is an NUI view, the other needs a platform
handler - so the predicate is executed directly and the call sites are pinned by source invariants in
`WaveCToolbarIconPressRoutingSourceTests`. Negative controls fire for both the predicate and each call
site.

> One trap worth recording: `Toolbar.IsVisible` defaults to **false**. A first draft of the back-press
> test passed for that reason rather than the intended one. The routing tests now set `IsVisible`
> explicitly.

## Item selection visuals

The in-tree backend set the internal `View.IsItemSelected`. An earlier revision of this adapter
replaced it with a single `VisualStateManager.GoToState` call per event, which is **not** equivalent:
the internal property was durable state that took part in every later recomputation, while a one-shot
transition stores nothing.

Three defects followed. Selection was lost whenever anything else recomputed the state - focusing a
selected row dropped it out of `Selected`, and unfocusing stranded it wherever the focus transition
landed. Selection could paint over `Disabled`, because nothing consulted `IsEnabled`. And a recycled
row could not be reasoned about, since the adapter could not answer whether a row was selected.

Selection and pointer-over are now stored in attached `BindableProperty` values - the same shape
upstream's own Tizen `ShellFlyoutItemView` uses, and public API, so no reflection is involved - and
every change recomputes the whole state by precedence: **Disabled > Selected > PointerOver > Normal**,
then focus applied independently, mirroring `VisualElement.ChangeVisualState`.

That ordering is what makes selection durable. `Focused`/`Unfocused` share a group with `Selected`, so
an authored `Focused` state wins while focused; when focus is lost the base state is re-applied first,
so a template authoring no `Unfocused` state - the common case - lands back on `Selected` instead of
being stranded. `Reset` clears everything the adapter owns and is called on the recycle path, so a
reused row cannot come back carrying the previous item's state.

`IsPointerOver` on `VisualElement` is internal and deliberately not read; the adapter owns its own
flag. `Unfocused` is spelled as a literal because `VisualStateManager.CommonStates.Unfocused` is
internal - the state *name* is part of the public XAML contract templates are authored against.

`WaveCItemSelectionStateTests` executes all of it (14 tests). Negative controls: reverting to a
one-shot transition fails 3, and reproducing the original focus path - where focus does not re-apply
the base state - fails `SelectionSurvivesFocusAndUnfocus`.

## The toolbar navigation slot

Back button, drawer toggle and title icon share **one** slot, so choosing between them is a
precedence decision: back > drawer > title icon > none. `TizenToolbarNavigationSlot` owns that
decision, and two defects found by review are pinned by tests.

### A late title-icon load must not repaint a slot it no longer owns

Title icons load asynchronously. The generation guard and the image-source comparison are both
necessary but **not sufficient**: setting `TitleIcon` while a back button is already showing starts a
load at the newest generation for the current source, so both of those checks pass and the icon
overwrites the back button when it arrives.

`IsCurrentTitleIconUpdate` therefore takes the drawer-toggle capability as well and applies a third,
owner check - the result is discarded unless the title icon still owns the slot. This matches the
`NavigationIconKind == TitleIcon` requirement upstream added in dotnet/maui#37863 (approved head
`53b9073`).

### The flyout owner comes from the toolbar's page, not the handler's virtual view

`TizenToolbarHandler.VirtualView` **is** the toolbar, so the earlier `VirtualView as IFlyoutView`
cast could never match and the drawer-toggle capability was permanently `false`. The visible symptom
was narrow and easy to misread: on a Shell, pushing a page and then popping it set
`BackButtonVisible = false` and restored an *empty* navigation slot instead of the hamburger, with
`FlyoutBehavior` unchanged the whole time.

`ToolbarDrawerToggle.FindFlyoutOwner` resolves the owner from `Toolbar.Parent` - the page the toolbar
presents - and walks that page's **public** `IElement.Parent` chain to the nearest `Shell` or
`FlyoutPage`. Controls' own `FindParentOfType` helper is `internal` and is deliberately not used;
Wave C's public replacement lives in `ShellElementTree`. The walk returns the *nearest* flyout
ancestor of either type rather than preferring `Shell`, so a `FlyoutPage` hosted inside a `Shell`
resolves to the `FlyoutPage` that actually owns its drawer.

Both fixes have negative controls: reverting the owner check fails two navigation-slot tests, and
restoring the null-owner short-circuit fails two drawer-toggle tests, including
`PoppingBackToTheRootRestoresTheDrawerToggle`.

On adoption of `IToolbarDrawerToggleVisible` the whole adapter - owner parameter and resolution
included - collapses to a pattern match on the toolbar alone.

## Testing

Wave C adds `WaveCSource`, `WaveCSourceIntegrityTests` and `WaveCMapperParityTests` to the existing
`tests/Maui.Tizen.SourceTests` project rather than standing up a competing suite, and reuses Wave
B's Roslyn parser (`WaveBSource.Parse`, widened from `static` to `public static`) so there is only
one implementation of the mapper-extraction rules to keep correct.

The suite runs in the existing workload-free CI lane. Note that it depends on
`Maui.Tizen.Core.RefPackCompile` having been built first - `EmittedTypeTests` reflects over that
lane's output. An earlier revision of this document described those tests as having "pre-existing
environmental failures"; they were simply being run without that dependency built. Built properly,
the whole suite passes.

- no `Microsoft.Maui.Controls.Internals` usings and no internal API use
- no reflection (`System.Reflection`, `BindingFlags`, `GetMethod`/`GetProperty`/`GetField`)
- no `partial class` extending a MAUI Controls type
- no type reusing any public MAUI type name
- no file left carrying the upstream `.Tizen.cs` multi-targeting suffix
- every handler declares both a property mapper and a command mapper
- every neutral MAUI mapper key is either implemented or recorded as an explicit gap, so the port
  cannot silently fall behind as MAUI adds properties
- every empty no-op mapper carries an XML doc comment explaining why
- every adapter has a matching `UpstreamApiRequests` entry, and the request IDs stay unique and
  sequential
- every no-op justification is *feature-specific* - it must name the property or the concrete Tizen
  limitation, so thirty mappers cannot all say "not supported"
- the provisional adapters carry **expiry tests** (`WaveCUpstreamExpiryTests`) that reflect over the
  referenced MAUI assemblies and fail the moment the upstream API lands, forcing the adapter to be
  deleted rather than kept as a diverging parallel implementation
- superseded raw sources never reach a compiled item list
  (`WaveCSupersededSourceTests`)
- every mapping delegate accepts exactly the handler type its mapper is declared over, and no
  mapper is declared over a MAUI handler *interface* (`WaveCMapperDispatchTests`)
- the current Controls product and API15 RefPack lanes import the same deterministic Wave C
  source manifest; the legacy net9 Wave C lane is absent

## Handler registration

`TizenNavigationHandlers` is an internal registration hook called by
`TizenControlsHostingExtensions.AddTizenControlsBackend`. It registers all 15 concrete Wave C
handlers inside the same `UseMauiAppTizenControls<TApp>` production path used by Core, Wave A and
Wave B. There is no public `UseMauiTizenNavigation` or `AddMauiTizenNavigationHandlers` API for an
application to forget or call in the wrong order.

`WaveCHandlerRegistrationTests` derives the expected handler set from source, rejects missing and
duplicate registrations, and pins the call from the Controls composition root. The host-side
`WaveCProductionRegistrationTests` builds that real composition path and resolves every concrete
Controls type through `IMauiHandlersFactory`.

## Adaptor registration is shared, not duplicated

The four Shell item adaptors each kept a private `Dictionary<NView, View>`. That looks equivalent to
the base adaptor's own table and is not: rebinding a recycled row, resolving the MAUI view in
`UpdateViewState`, activating the current item and tearing a row down are all keyed off the base
registration, so a parallel table silently opts every row out of all of it.

`TizenItemTemplateAdaptor` now exposes `RegisterNativeView` / `GetRegisteredView` /
`UnregisterNativeView`, and all four Shell adaptors go through them. Enabled-state tracking is
attached inside registration so no caller has to remember it, and removal now *unregisters* rather
than merely looking up — leaving the entry behind kept the view alive and let a recycled native view
resolve to a MAUI view whose handler was already disposed.

## Final predecessor integration

Finalized main supplies the Core-owned toolbar, stack navigation, flyout, wrapper, image, mapper,
disconnect and disposal infrastructure. Wave C references those types directly and contributes only
navigation, Shell, items/carousel, toolbar handler, menus and their platform views/adaptors.

Raw imported Tizen sources remain solely as provenance and are excluded from every compiled item
list by `eng/manifests/wave-c-superseded.json` and `WaveCSupersededSourceTests`.

## External blockers and device gaps

The pinned MAUI packages still do not expose modal navigation (#37853), generic handler contracts
(#37855), Blazor APIs (#37858), or the external selection-state seam. Long-press, Shell resolver,
toolbar capability and image-paint APIs (#37861–#37864) may be merged upstream but are retained
behind typed adapters and expiry tests until they ship in the pinned package family. No reflection or
internal access is used.

A Tizen device or emulator is still required to validate NUI recycling, focus, native selection,
Shell navigation animations, drawer interaction, toolbar rendering and disposal timing. The host and
API15 lanes validate composition, compilation, metadata, deterministic parity and NUI-free
lifecycles; they do not claim device behavior.
