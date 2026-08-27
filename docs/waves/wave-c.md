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

All code lives in `src/Maui.Tizen.Controls.Navigation/`.

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

### The acceptance gate is the API15 ref-pack lane

`tests/Maui.Tizen.Core.RefPackCompile` compiles the backend against the **real net11 public MAUI
packages** and the **Samsung.Tizen.Ref.API15** reference assemblies. That lane - and only that lane -
is the acceptance gate for Wave C. Every Wave C source and catalog page is listed for it in
[`eng/Maui.Tizen.WaveC.Sources.props`](../../eng/Maui.Tizen.WaveC.Sources.props), and
`WaveCAcceptanceGateTests` fails if a file is missing from that list.

**A previous revision of this document claimed a `net9.0-tizen7.0` compile as acceptance. That was
wrong and is worth recording rather than quietly deleting.** MAUI 9.0.120 still ships a Tizen build,
so that lower-band compile resolved `Microsoft.Maui.Platform.MauiToolbar`, `StackNavigationManager`
and `IPlatformViewHandler` from the MAUI package itself and went green. None of those exist on the
net11 surface. Pointing the real gate at Wave C immediately surfaced **60 diagnostics** the
behaviour-baseline compile could not see:

| Diagnostic | Cause |
| --- | --- |
| 6 × CS9333 | net11 `IToolbarHandler` / `IMenuBarHandler` / `IMenuBarItemHandler` declare `PlatformView` as `object`; the 9.0.120 Tizen build typed it concretely |
| 4 × CS0246 `IPlatformViewHandler` | Exists only inside MAUI's own `net*-tizen` build; the out-of-tree counterpart is core's `ITizenPlatformViewHandler` |
| 24 × CS0109 | `new` on per-handler mapper fields that hide nothing out-of-tree |
| 6 × CS0108 / 6 × CS0114 | NUI `BaseHandle.Dispose()` / `View.Dispose(DisposeTypes)` participation |
| 14 × CS8766 | net11 `I*Handler.PlatformView` is non-nullable `object`; the base handler's is `object?` |

All 60 are fixed. `9.0.120` is retained **only** as a behaviour/API comparison baseline, never as
acceptance.

### What is still blocked, and why the gate is currently off

Wave C consumes two Tizen platform primitives that the net11 MAUI surface does not publish:

| Missing | References | Owner |
| --- | --- | --- |
| `Microsoft.Maui.Platform.MauiToolbar` | 38 | Core — `TizenToolbarView` |
| `Microsoft.Maui.Platform.StackNavigationManager` | 10 | Core — `TizenStackNavigationManager` |
| `MauiFlyoutView` / `MauiTVFlyoutView` | 2 | Core — `TizenFlyoutView` / `TizenTVFlyoutView` |
| `DrawerView` update extensions (6) | 6 | Core |
| `FlyoutBehavior` → drawer behaviour mapping | 1 | Core |

Ownership was confirmed on 2026-08-26. The integration check behind it ran against Core head
`4e256f1271`; live Core is now `2f19d872f74e` (19 commits on), so that check is **stale** and will
be re-run against stable reviewed Core -> Wave A -> Wave B heads before the acceptance gate opens.
The three-extension collision it found is confirmed and assigned: Core owns those members, Wave B
deletes its duplicates during the final rebase. Wave C owns the Flyout/Shell/navigation **handlers** and two
pieces of its own cleanup: re-pointing at existing Core/Wave B names (`ToTizenNativeColor`,
`ToTizenCommonColor`, `TizenWrapperView`, `TizenPlatformExtensions.UpdateBackground`) and inlining
the handler-specific toolbar attach, since `ViewHandler.MapToolbar` never existed outside MAUI's
per-platform builds.

That cleanup is deliberately **not** started yet: the names it would re-point at are the same ones
still under review, so doing it early would mean doing it twice.

These are Core-owned primitives that belong in `Maui.Tizen.Core` beside `TizenViewHandler<,>` and
`TizenContentViewGroup`. **Wave C must not declare its own** - that would give the repository two
authoritative toolbars, which is exactly the ambiguity
[`eng/manifests/wave-c-superseded.json`](../../eng/manifests/wave-c-superseded.json) exists to
prevent. `WaveCDoesNotDeclareItsOwnCopyOfABlockedPrimitive` enforces it.

Their transitive blast radius is **25 of 50 files**, so excluding just the six direct consumers
would leave the lane verifying almost nothing - the same false confidence in a different costume.
The whole wave is therefore gated on one flag:

```bash
dotnet build tests/Maui.Tizen.Core.RefPackCompile/... -p:MauiTizenWaveCAcceptance=true
```

With the gate **off** the lane is clean. With it **on**, the only diagnostics are the 48 `CS0246`
references to those two types - zero warnings, nothing else outstanding.
`AcceptanceGateMustBeReopenedOnceCoreLandsThePrimitives` fails as soon as core provides either, so
the flag cannot be left off once the blocker clears.

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

### Mapper dispatch — a known, deliberate gap

Wave A hit a crash pattern worth stating plainly here: MAUI's
`PropertyMapper<TVirtualView, TViewHandler>` **casts** the handler when it dispatches a mapping, so
an interface-declared mapper instantiated over a concrete built-in handler makes any chained Tizen
mapping throw at runtime. Wave C is structurally exposed — all 86 of its mapper delegates take a
concrete `Tizen*Handler`.

`WaveCMapperDispatchTests` pins the two source-level invariants that keep that safe: delegates must
match their mapper's declared handler type (checked **per class**, since several handlers share a
file and reuse names like `MapText`), and no mapper may be declared over a handler interface. Both
pass today.

Those are **necessary but not sufficient**. Proving dispatch is safe needs a real Controls host that
registers the Tizen handlers, enumerates every key including inherited and chained ones, and
actually invokes each mapping — only that catches an inherited concrete-handler cast or a no-op body
that never runs. That cannot be written until the predecessor stack lands, and host tests cannot
instantiate NUI views. A placeholder test therefore **fails the moment the acceptance gate opens**,
so the gap is closed deliberately rather than forgotten.

### What is still unverified

Runtime behaviour. There is no Tizen emulator or device here, so item recycling, virtualization
performance, navigation animation and Shell lazy content are **compile-verified only**.

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
- the validation lane still targets a real Tizen TFM and still compiles the same sources as the
  shipping project

## Predecessor dependencies

Wave C is branched from the foundation import commit so paths and history line up. It consumes,
but does not define, the following Core-level Tizen primitives, which belong to the foundation and
Waves A/B:

- `Microsoft.Maui.Platform.MauiToolbar`, `ToolbarExtensions.UpdateTitle`
- `Microsoft.Maui.Platform.StackNavigationManager`, `NaviPage`
- `Microsoft.Maui.Platform.MauiFlyoutView`, `MauiTVFlyoutView`, `FlyoutViewExtensions`
- `Microsoft.Maui.Platform.WrapperView`, `ViewGroup`, image-source and font services

In the validation lane these resolve from the MAUI 9.0.120 package, which still ships a Tizen
implementation. Once Waves A/B land their own `Maui.Tizen.Core`, the Wave C project needs a
`ProjectReference` to it instead — that is the one merge action Wave C leaves behind.

Wave C also leaves the raw imported sources it supersedes in place under
`src/Maui.Tizen.Controls/Core/` rather than deleting them, so that a later rebase onto finalized
predecessor branches is a content merge rather than a delete/add conflict. Removing them is a
follow-up once Waves A/B have settled the final assembly layout.
