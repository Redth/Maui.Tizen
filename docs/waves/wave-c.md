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
| 0009 | `Toolbar.DrawerToggleVisible` | **No public equivalent.** Tizen-owned state in `ToolbarDrawerToggle` |

`Adapters/UpstreamApiRequests.cs` carries the same list in machine-readable form, with the concrete
API being requested upstream in each case. The source tests fail if an adapter exists without a
matching entry, so the list cannot rot.

### Remaining coupling and known behavioural gaps

Two items are not fully resolved and should not be described as "done":

1. **`MenuShellItem` flyout templates (request 0001).** Upstream's helper has a second redirect:
   for a `MenuShellItem` it forwards to the wrapped `MenuItem` when that item sets
   `Shell.MenuItemTemplateProperty`. `MenuShellItem` *and* its `MenuItem` property are both
   `internal`, so an out-of-tree backend cannot express this branch at all — not awkwardly, not at
   all, short of reflection. A bare `MenuItem` in a flyout therefore falls back to the shell-level
   item template. This is the strongest single argument for publishing the helper.

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

| Missing type | References | Expected core owner |
| --- | --- | --- |
| `Microsoft.Maui.Platform.MauiToolbar` | 38 | `TizenToolbarView` |
| `Microsoft.Maui.Platform.StackNavigationManager` | 10 | `TizenStackNavigationManager` |

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
