# Wave A final integration plan

Wave A (`redth-tizen-handler-wave-a`, PR #12) is **held**: complete and green on its own, waiting
for a stable reviewed Core head to rebase onto. This is the checklist for that final integration,
written while the facts are fresh rather than reconstructed at merge time.

Every claim below was measured against the tree at the head recorded in "Status", not inferred.

## Status

| Item | State |
|---|---|
| Wave A head | `d7f6188` |
| Base | `redth-tizen-core-vertical-slice` |
| Core head observed | `e467293` — **fetched for inspection only, never merged** |
| CI | green (3/3), 809 tests, `eng/build-workload-free.sh` green |

**Do not merge intermediate Core heads.** Core has moved repeatedly during this wave
(`da6becf` → `163677d` → `dbcf82e` → `e467293`), and each rebase costs a full revalidation cycle
plus a PublicAPI baseline regeneration. The rebase happens once, onto the head Core declares
stable and reviewed.

## 1. Image-service composition with Wave B

**Wave A's half is done and wired.** `ConfigureTizen()` calls
`ConfigureImageSources(sources => sources.AddTizenImageSources())`, which registers the file and
stream services. Before this, `AddTizenImageSources` had no caller at all — the defect Wave B's
review surfaced.

**Wave B extends the same method. It should not add a second entry point.**
`AddTizenImageSources` lives in the *portable* compile group
(`src/Maui.Tizen.Core/ImageSources/TizenImageSourceServiceCollectionExtensions.cs`), deliberately
separated from the NUI-dependent service implementations, so that:

- the `ConfigureTizen` call compiles on both the host and Tizen lanes, and
- Wave B can add the URI and font registrations without touching Tizen-only files.

Adding a parallel `AddTizenImageSourcesWaveB()` and expecting a host to call it would reproduce
exactly the defect being fixed.

**The handoff is enforced, not remembered.**
`CompositionRootTests.FontAndUriSourcesAreNotYetTizenOwned` asserts that `IFontImageSource`
and `IUriImageSource` still resolve to MAUI's neutral defaults. When Wave B registers Tizen
services for them, that test **fails with instructions**: extend
`TizenSourcesAreRegisteredOnTheTizenLane` to assert the new services and delete the stale case.

**Why this class of bug is invisible to ordinary tests.** MAUI's neutral package already registers
`FileImageSourceService`, `StreamImageSourceService`, `FontImageSourceService` and
`UriImageSourceService`. Every image source type therefore *resolves* whether or not any Tizen
registration ever runs — nothing throws, nothing is reported missing, and images are simply blank.
A test asserting "an image source service is registered" passes on an app that can never display an
image. Wave B's tests must assert **which implementation wins**, never mere resolvability.

That a later `AddService` replaces MAUI's default rather than being shadowed by it is the
load-bearing assumption behind the whole design, and is pinned by
`ATizenRegistrationReplacesMauisNeutralDefault` rather than assumed.

### Integration check

- [ ] `AddTizenImageSources` registers file, stream, URI and font services.
- [ ] `FontAndUriSourcesAreNotYetTizenOwned` deleted, its cases folded into
      `TizenSourcesAreRegisteredOnTheTizenLane`.
- [ ] `EveryTizenRegistrationExtensionHasACaller` still passes — it fails on any public
      `AddTizen*` extension that no compiled source calls, which is how the missing
      `AddTizenControlHandlers` and `AddTizenControlServices` calls were found.

## 2. Removing the Core-owned test delta

Wave A carries **one** delta to a Core-owned test:
`ControlsRegistrationTests.NoParallelTizenHandlerInterfacesRemain`, +17 lines.

It is not a redesign — an earlier revision was, and that was reverted. What remains is forced: the
test asserts an *exact list* of exported `ITizen*` type names, and Wave A genuinely exports two
more (`ITizenFontManager`, `ITizenModalHost`). Neither is a parallel handler hierarchy; the test
matches on a name prefix, so it sees them.

**This delta is a rebase hazard, and the resolution direction matters.** If Core independently
adjusts the same test, the two edits conflict — and the correct resolution is to **take Core's
version and drop Wave A's**, not to re-apply Wave A's on top. Re-applying would silently reinstate
a delta on a file Wave A does not own.

Three ways it disappears, in order of preference:

**A. Core adds the two names to its own list.** Simplest; Core owns the file and the assertion.
Wave A's delta vanishes at rebase with no further work.

**B. Core narrows the test to handler interfaces.** The test's stated intent is "no parallel
*handler* hierarchy remains"; its implementation is a name-prefix match. Narrowing it to
`IElementHandler`-assignable types (or names ending in `Handler`) makes intent and implementation
agree, and no service contract ever trips it again — including Waves B/C's.

**C. Wave A makes both interfaces `internal`.** Technically viable: every consumer is inside
`Maui.Tizen.Core` (verified — `TizenTextExtensions` and `TizenControlHandlerCollectionExtensions`).
But it is a **public API change** requiring a PublicAPI baseline regeneration, and it removes a
host's ability to substitute a font manager or modal host. Not recommended unless Core declines
both A and B.

### Integration check

- [ ] After rebase, `git diff <core-head> -- tests/Maui.Tizen.Core.UnitTests/` is **empty**.
- [ ] If it is not, the remaining delta is deliberate and agreed with Core, or it is dropped.

## 3. Upstream adoption guards — only after packaged APIs

Two MAUI gaps shape this backend. Both are worked around honestly (degrade, never reflect over
internals), and both now have an expiry test that **fails when the fix ships**, so a workaround
cannot outlive its justification.

| Gap | Guard | Verified state in the pinned package |
|---|---|---|
| `ImageSourcePaint` is `internal` | `ImageSourcePaintIsStillInternal` | `IImageSourcePaint` absent; `ImageSourcePaint.IsVisible == false` |
| `ContainerView` setter is `private protected` | `ContainerViewIsStillUnsettableByAnExternalBackend` | `ValidateContainerView` absent; setter reports `IsFamilyAndAssembly` |

**Neither may be adopted until the API is in a restored package** — not when the PR merges, and not
when a branch shows the shape. Both guards read the *pinned assembly* by reflection, so they cannot
be satisfied by a PR being open, and both report the **shipped member shape** when it lands,
including an explicit warning when that shape is not what the adoption plan assumed.

Supporting guards that must survive the rebase:

- `BackendDoesNotReachImageSourcePaintByName` — no reflection over MAUI internals.
- `BackendDoesNotImplementImageSourcePaint` — upstream supports `IImageSourcePaint` for
  **consumption only** and may add members, so an external implementation would break on a
  servicing update. This guard must **outlive** the expiry test: the temptation to implement only
  becomes real once the type resolves.
- `BackgroundMappingsKeepTheViewInScope` — background mappings must pass the *view*, not
  `view.Background`. Resolving an image source needs an `IImageSourceServiceProvider` reached via
  `view.Handler`, so a mapping that passes only the paint can never render an image background
  however the extension is later fixed. The distinction is invisible today, which is why it is a
  test and not a comment.

**Note for Core:** both `IView` overloads of `UpdateBackground` currently forward to the `Paint`
overload and drop the view, so `TizenLayoutHandler`, `TizenPageHandler`,
`TizenContentViewHandler` and `TizenViewMappers` inherit the same limitation. Wave A's own call
sites are fixed and guarded; the Core-owned ones are not.

### Adoption order when `IImageSourcePaint` ships

Match the image case **first**, ahead of gradient *and* solid, in the `IView` overload:

1. `paint is IImageSourcePaint image` → if `image.ImageSource is null`, clear any previously
   applied image and return.
2. Otherwise resolve through `TizenImageLoader`, which already provides cancellation,
   supersession, failure-clearing and disposal — including the "resolved successfully but yielded
   no image" case, which must also clear (`ALoadResolvingToNoImageClearsThePrevious`).

Matching after the solid branch leaves image paints flattening to a colour exactly as they do
today.

### The signature rule, which has now bitten three times

The neutral package types platform-facing things as `object`. Being *more specific* than the base
declaration fails — sometimes loudly, sometimes silently:

| Where | Symptom |
|---|---|
| `IXHandler.PlatformView` explicit implementation | `CS9333` — produced the retired `ITizen*Handler` hierarchy |
| Mapper entry typed against the concrete handler | **silent** rebinding to MAUI's inherited no-op |
| `ValidateContainerView(TizenWrapperView)` | `CS0115` |

**Match the base or interface signature exactly; narrow inside the body.** The silent variant is
the dangerous one — it compiles and behaves differently per target framework.

### Integration check

- [ ] Both expiry tests still pass, i.e. both gaps are still open. A *failure* here is good news
      and means the adoption work is now in scope.
- [ ] No adoption code merged while the tests pass.

## 4. Real mapper behaviour tests

The standing acceptance bar for this wave: **resolution is not implementation.** Key presence and
absence of `InvalidCastException` are necessary but nowhere near sufficient. Core's own review
found Label's `FormattedText`, `LineBreakMode`, `MaxLines` and accessibility keys *reachable* yet
behaviourally inert, with every test of the day passing.

What Wave A asserts by observable effect:

- `ControlsRemapBehaviorTests` — `Picker.ItemsSource` really raises `IPicker.Items`;
  `Stepper.Increment` really raises `IStepper.Interval`; `Description`/`Hint`/`HeadingLevel` really
  reach the backend's `Semantics` mapping. Each was verified non-vacuous by removing the override
  and watching the test fail.
- `ControlMapperBehaviorTests` — visibility, enabled, opacity, sizing, transforms, input
  transparency and focus reach the platform view, rather than resolving to a no-op.
- `TizenHandlerMapperTests.EveryChainedMappingInvokesWithoutCastFailure` — every chained key
  dispatches. This is the drift alarm for the concrete-handler cast: MAUI's static mappers are
  constructed as `PropertyMapper<IView, XHandler>`, closed over the *concrete* handler, so a key
  reachable only through the chain throws when dispatched to a Tizen handler. **Every chained key
  must be overridden**, which is why `Picker.ItemsSource` and `Stepper.Increment` have Tizen bodies.
- `EveryControlHandlerResolvesFromTheCompositionRoot` — each control resolves through an app built
  with `ConfigureTizen()` alone. A test that arranges the registration it is verifying proves the
  method works, not that it is *wired*; that gap hid all fourteen handlers being unregistered.

**Two keys are recorded as reachable but genuinely inert:** `IsInAccessibleTree` and
`ExcludedWithChildren` resolve through the chain and do nothing observable. This was measured, not
assumed. The mapping is Core-owned (`TizenViewMappers`), so
`KnownInertAccessibilityKeysAreStillInert` pins the fact and fails, with instructions, if anyone
implements them. **This contradicts the `TizenViewMappers` doc comment**, which claims all five
accessibility keys route into the platform mappers — three do, two do not. Core's to correct.

**What a host lane cannot prove, and should not claim.** A control-specific body such as
`TizenEntryHandler.MapBackground` sits entirely inside `#if TIZEN`; off-device it genuinely does
nothing, and no host test can say otherwise. What *is* verifiable here is dispatch logic — whether
a Controls key forwards to the backend key that implements it — which is where the remaps live.
Everything past that needs the device lane the unpublished Samsung workload still blocks.

### Integration check

- [ ] Behaviour tests still pass after rebase, and any Core mapper change that makes a key inert
      is caught by them rather than by a later review.
- [ ] Parity matrix regenerated (`MAUI_TIZEN_UPDATE_PARITY_MATRIX=1 dotnet test …`) — it is
      generated from the shipped mappers, so a Core mapper change silently staleness it.

## Rebase procedure

1. Confirm Core's head is **declared stable and reviewed**, not merely green.
2. `git rebase <core-head>`; expect conflicts in `eng/Maui.Tizen.Core.Sources.props` (both waves
   add compile items) and possibly `ControlsRegistrationTests.cs` (see §2 — take Core's).
3. Regenerate the PublicAPI baseline:
   `dotnet format analyzers tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj --diagnostics RS0016 --severity warn`.
   Review the diff; an unexpected entry is the signal that baseline exists to give.
4. Regenerate the parity matrix.
5. Run `eng/build-workload-free.sh` — host, API15 ref-pack, PublicAPI and parity lanes.
6. Push, dispatch CI explicitly (`gh workflow run CI --ref …`; a force-push does not trigger
   `pull_request`), and confirm all three jobs.

Local timing is not a signal on a loaded machine: this worktree has shown a ~2m15s `dotnet test`
startup floor for a 45ms test class while CI ran the whole suite in 40 seconds. Measure in CI
before treating slowness as a regression.
