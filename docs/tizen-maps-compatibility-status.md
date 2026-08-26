# Tizen Maps &amp; Compatibility — capability decision

This document records the capability decision for `Maui.Tizen.Maps` and
`Maui.Tizen.Compatibility` on this branch. **Neither project ships any code.** Both remain
exactly in the "Skeleton"/"Provisional" state the `redth-tizen-core-vertical-slice` foundation
already established (identity, packing metadata and dependencies declared; nothing compiled or
packed) — this document is the evidence and reasoning for *why* that is the correct outcome
right now, not a proposal to change it.

This supersedes an earlier version of this branch that vendored dotnet/maui's shared,
`#if TIZEN`-conditional source files (`MapHandler.cs`, `VisualElementRenderer.cs`,
`ViewHandlerDelegator.cs`, and the plain cross-platform `Map`/`Pin`/`IMap`/etc. types) directly
into these projects. That was wrong: those fully-qualified type names already belong to the
neutral `Microsoft.Maui.Maps` / `Microsoft.Maui.Controls.Core` packages this repository
references (see `docs/architecture.md`'s collision rules), so redeclaring them here would have
produced type-identity collisions (`CS0433`) for any app that references both — not a working
Tizen backend. All of that vendored source has been removed.

## Maps: no Tizen-specific code needed, none should be added

**Capability finding:** dotnet/maui's own upstream `MapHandler` for Tizen was never anything
but a stub — `CreatePlatformView()` and almost every property mapper throw
`NotImplementedException`. Critically, **the real, published, platform-neutral
`Microsoft.Maui.Maps`/`Microsoft.Maui.Controls.Maps` packages already provide the exact same
stub**, because dotnet/maui also ships a `*.Standard.cs` fallback partial for exactly this
"no specific platform" case (`MapHandler.Standard.cs`, `MapElementHandler.Standard.cs`,
`MapPinHandler.Standard.cs`), and it is byte-for-byte behaviorally identical to the Tizen
partial: same `NotImplementedException` throws, same no-op `MapPinHandler` mappers.

Verified directly, not assumed — `docs/capability-probes/maps-neutral-stub-probe.sh` builds and
**runs** a throwaway console app (plain `net11.0`, using this repository's own
`Directory.Packages.props` pin and `nuget.config`) that calls the real `UseMauiMaps()` and
confirms:

```
Map is registered to handler: Microsoft.Maui.Maps.Handlers.MapHandler
  from assembly: Microsoft.Maui.Maps, Version=11.0.0.0, Culture=neutral, PublicKeyToken=null
CONFIRMED: MapHandler.CreatePlatformView() throws NotImplementedException,
           matching dotnet/maui's own historical Tizen (non-)implementation.
```

So a Tizen MAUI app that references `Microsoft.Maui.Controls.Maps` directly and calls
`UseMauiMaps()` gets **precisely** the behavior the old, now-deleted `Maui.Tizen.Maps` source
in this branch was trying to reproduce by hand — for free, with zero Tizen-specific code, and
with zero collision risk, since it's the same assembly the app already depends on. Shipping a
`Maui.Tizen.Maps` package that duplicates `MapHandler` under the same name would only risk a
`CS0433` ambiguous-reference error for no behavioral gain.

**Does Tizen have a real native map view this could be built against instead?** Researched
directly against the current [Samsung/TizenFX](https://github.com/Samsung/TizenFX) source tree
(the actual API surface, not marketing docs, which redirect elsewhere): there is **no
`Tizen.Maps` namespace and no map-view module of any kind**. TizenFX has `Tizen.Location`
(geocoding/positioning) but nothing that renders a map. This was checked exhaustively —
`src/` was enumerated in full and searched for anything map-related; nothing exists. This
means the pre-net11.0 Xamarin.Forms-era `Compatibility.Maps` Tizen renderer
(`FormsMaps.cs`/`MapRenderer.cs`, tag `9.0.120` only) that a naive read of the old history might
suggest reviving was, at best, built against a native surface that predates the current TizenFX
and cannot be assumed to still exist; at worst it never had a real backing implementation
either. Either way, **there is currently no supported path to a functional native Tizen map
view**, so implementing one is out of scope for this PR and would need its own
research/design effort if ever pursued.

**Disposition: `Maui.Tizen.Maps` ships no code and is not packed.** This is not a regression
from a working state — there never was one. App authors who want `Map` on Tizen today should
reference `Microsoft.Maui.Controls.Maps` directly (as they would for any other unsupported
platform) and get the documented `NotImplementedException` behavior; there is nothing
`Maui.Tizen.Maps` could add. If a real Tizen map backend is ever designed (e.g. wrapping a
third-party JS map widget inside a WebView, since there is no native option), it should be a
deliberate, separate, researched effort — not a revival of the dead stub.

## Compatibility: no public contract exists to build against yet

**Capability finding:** unlike Maps, the compatibility renderer base infrastructure
(`VisualElementRenderer<TElement>`, `ViewHandlerDelegator<TElement>`, and the CollectionView
`ItemTemplateAdaptor` that `ListViewAdaptor`/`TableViewAdaptor` build on) has **no neutral
fallback at all** upstream — these types only ever existed inside a platform-specific
compilation (Android/iOS/Windows/Tizen each got their own complete build). There is no
`*.Standard.cs` equivalent the way there is for Maps.

Verified directly — `docs/capability-probes/compatibility-collision-probe.sh` builds and runs a
throwaway console app against the real, published, plain-`net11.0`
`Microsoft.Maui.Controls.Core` package and confirms:

```
present (as expected, just a sanity check the assembly loaded): ListView (control) -> Microsoft.Maui.Controls.ListView
present (as expected, just a sanity check the assembly loaded): TableView (control) -> Microsoft.Maui.Controls.TableView
present (as expected, just a sanity check the assembly loaded): Frame (control) -> Microsoft.Maui.Controls.Frame
CONFIRMED ABSENT: VisualElementRenderer<TElement>
CONFIRMED ABSENT: ViewHandlerDelegator<TElement> (internal)
CONFIRMED ABSENT: ItemTemplateAdaptor
```

The controls themselves (`ListView`, `TableView`, `Frame`) exist and are usable; the renderer
plumbing that would make them actually draw on Tizen does not exist anywhere public. Building
one today would mean reimplementing `VisualElementRenderer`/`ViewHandlerDelegator` from
scratch (no public contract to extend — genuinely new code, per `docs/architecture.md` Rule 3)
**and** depending on `Maui.Tizen.Controls`'s own Tizen-specific `ItemTemplateAdaptor`
(`src/Maui.Tizen.Controls/Core/Handlers/Items/Tizen/ItemTemplateAdaptor.cs`), which is
imported-but-not-yet-compiled Phase 2 work, not something this PR's scope (Maps/Compatibility)
owns or should get ahead of.

This corroborates `src/Maui.Tizen.Compatibility/README.md`'s own "provisional, likely deleted"
assessment with concrete, reproducible evidence, rather than leaving it an unverified guess.

**Disposition: `Maui.Tizen.Compatibility` ships no code and is not packed** — left exactly as
the foundation's placeholder README describes it. No source was added. If Phase 2
(`Maui.Tizen.Controls`) later lands a working Tizen `VisualElementRenderer`/`ItemTemplateAdaptor`
as part of implementing CollectionView, ListView/TableView/Frame compatibility renderers could
be revisited *at that point* against those real, compiling, Tizen-owned base types — not
before.

## Reproducing the evidence

```bash
docs/capability-probes/maps-neutral-stub-probe.sh
docs/capability-probes/compatibility-collision-probe.sh
```

Both require only the plain `net11.0` SDK (pinned in `/global.json`) and network access to the
`dotnet11` feed already configured in `/nuget.config` — no Tizen workload involved, and both
were run successfully while preparing this decision.

## What this PR actually changes

Given the above, this PR's net effect relative to `redth-tizen-foundation-import` is
documentation and evidence only:

- Added: this document and the two capability-probe scripts under `docs/capability-probes/`.
- Unchanged: `src/Maui.Tizen.Maps/**` and `src/Maui.Tizen.Compatibility/**` are byte-for-byte
  identical to the foundation branch — no source added, no `.csproj` changed.

## Workload status (unchanged from the foundation)

`net11.0-tizen11.0` still cannot be restored or built anywhere — Samsung has not published the
workload manifest package `Samsung.NET.Sdk.Tizen.Manifest-11.0.100-preview.7` (verified against
both the Samsung-cased ID and the `samsung.net.sdk.tizen.manifest-11.0.100-preview.7`
nuget.org flat-container form; neither exists — see `eng/baselines.json` →
`target.workloadManifest` and [`docs/migration.md`](migration.md#the-external-gate) for the
full workload decision). That remains an external gate unrelated to this decision.
`eng/build-workload-free.sh` passes in full in this environment (SDK pin, central package
management, package source mapping, MSBuild conventions, the workload gate itself, and the 20
repository invariant tests all genuinely exercised); the Tizen-specific build/test step is not
attempted, consistent with the foundation's own documented lane design.
