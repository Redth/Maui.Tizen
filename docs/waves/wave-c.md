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

## Build status — read this before filing a red build

**The shipping target framework cannot be built anywhere today.**

`eng/baselines.json` > `target.workloadManifest` records that
`samsung.net.sdk.tizen.manifest-11.0.100` has not been published; only the `-9.0.100` and
`-10.0.100` bands exist. `net11.0-tizen11.0` therefore cannot be restored, on any machine, by
anyone. `Maui.Tizen.Controls.Navigation.csproj` fails with an explicit, actionable error
(`MAUITIZEN1001`) rather than degrading.

Per `Directory.Build.props`, do **not** "fix" that by adding a neutral `net11.0` TFM. A neutral
build would go green while producing assemblies that cannot run on Tizen.

### The validation lane

To keep "it compiles" from being an unverified claim, Wave C adds an opt-in compile lane:

```bash
./eng/validation/run-validation-lane.sh
```

It compiles **the exact same source files** (both projects import
`src/Maui.Tizen.Controls.Navigation/Sources.props`) against `net9.0-tizen7.0` — the repository's own
declared `behaviorBaseline` — using the Samsung workload band that *is* published and MAUI 9.0.120.

This is not a neutral fallback: `net9.0-tizen7.0` is a real Tizen target framework compiled against
real TizenFX reference assemblies, which is the opposite of the failure mode the neutral-TFM rule
exists to prevent. The lane is compile-only; nothing is packed or published from it.

The script provisions an **isolated** SDK under `artifacts/validation-sdk/` rather than mutating the
developer's machine-wide dotnet installation, because installing workloads is shared-state mutation
that can change how unrelated repositories build. It also installs the Samsung workload manifest
from nuget.org by hand, since Samsung does not ship it through the in-box workload manifests.

### What the validation lane does and does not prove

- It **does** prove the migrated code compiles against real TizenFX and real public MAUI API, and
  that no internal API is reachable — the internals coupling above was found *by the compiler*.
- It **does not** prove runtime behaviour. There is no Tizen emulator or device in this
  environment, so nothing here has been executed. Item recycling, virtualization performance,
  navigation animation and Shell lazy content creation are **unverified at runtime**.
- API differences between MAUI 9.0.120 and the net11 package set are not covered by this lane.

## MAUI package floor

Wave C is written against the `11.0.0-preview.7.26426.4` (`bedd1b18`) package set, which the
foundation bumps in `Directory.Packages.props` and `eng/baselines.json`. Wave C deliberately does
not bump those files itself - they are foundation-owned, and editing them here would only create a
merge conflict.

Two things in that set were checked against Wave C:

- **TabbedPage badges (dotnet/maui#37755).** `BadgeText`, `BadgeColor` and `BadgeTextColor` are now
  declared on `TizenTabbedPageHandler` and classified `NoOp`, matching upstream's own statement that
  "Tizen exposes the shared API without a platform renderer". See
  [`docs/wave-c-mapper-parity.md`](../wave-c-mapper-parity.md#tabbedpage-badges) for why their keys
  are string literals rather than `nameof`.
- **Hardened gesture APIs.** No Wave C impact. The only gesture-adjacent surface Wave C touches is
  `IFlyoutView.IsGestureEnabled`, a stable core property. Gesture recognizers and
  `GesturePlatformManager` belong to the alerts/gestures workstream.

## Coverage at a glance

20 migrated handlers, 54 supported mappings and 33 documented no-ops. Full detail in
[`docs/wave-c-mapper-parity.md`](../wave-c-mapper-parity.md).

While generating it, Wave C also fixed a latent bug in the shared Roslyn parser Wave B introduced:
it only recognised mapper fields named exactly `Mapper` / `CommandMapper`. Handlers that shadow a
generic base mapper must give the field a distinct name (`CarouselViewMapper`,
`ItemsViewCommandMapper`, ...), and those were being reported as having **no** mapper coverage at
all - which surfaced as dozens of fictitious parity gaps. The parser now matches on suffix.
`docs/wave-b-mapper-parity.json` is regenerated as part of that fix and legitimately reports more
coverage than before; it is a correction, not drift.

## Testing

Wave C adds `WaveCSource`, `WaveCSourceIntegrityTests` and `WaveCMapperParityTests` to the existing
`tests/Maui.Tizen.SourceTests` project rather than standing up a competing suite, and reuses Wave
B's Roslyn parser (`WaveBSource.Parse`, widened from `static` to `public static`) so there is only
one implementation of the mapper-extraction rules to keep correct.

These are source tests on purpose: until the Samsung .NET 11 workload ships, the Tizen assemblies
cannot be compiled or executed by anyone, so a reflection-based test over the built handlers is not
an option. They run on a plain TFM in the existing workload-free CI lane:

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
