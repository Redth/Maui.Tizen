# Lifecycle, input and TV focus

Behaviour that only exists on a running application. All of it lives in the
[device lane](device-lane.md) and none of it can run today.

Harness: `eng/validation/scripts/tizen-device-lane.sh`

## Lifecycle

```bash
./eng/validation/scripts/tizen-device-lane.sh lifecycle
```

Launches the catalog, backgrounds it, foregrounds it, and confirms the DevFlow agent still responds.

Suspend and resume is where Tizen applications most often lose state or fail to re-attach their
renderer, and the symptom is severe — a blank window — while nothing logs an error. The agent
answering after resume is a cheap, reliable proxy for "the app is still alive and rendering".

Planned additions once the lane runs:

| Case | Asserts |
|---|---|
| Cold start | First page renders within a time budget |
| Background/foreground | Agent responds; visual tree is unchanged |
| Low memory | App survives a memory-pressure event |
| Rotation (mobile) | Layout re-flows and does not lose scroll position |
| Termination | Clean shutdown, no orphaned agent port |

## Input

Two mechanisms with different guarantees — see
[DevFlow: two kinds of interaction](devflow.md#two-kinds-of-interaction). Synthesised input is the
only path that exercises real hit-testing, and it needs
`http://tizen.org/privilege/inputgenerator`. The catalog manifest records which interactions each
case supports, using a closed vocabulary that the hosted lane enforces:

```
tap  fill  scroll  focus  select  toggle  longpress  swipe  remote-navigate
```

An unknown verb fails `CatalogAndBaselineConventionTests.CatalogManifest_IsWellFormed` on the pull
request, rather than producing a mysteriously skipped case on a device weeks later.

## TV remote focus

```bash
TIZEN_PROFILE=tv ./eng/validation/scripts/tizen-device-lane.sh remote-focus
```

Focus order is the most common TV-specific defect and it is invisible to every host-side check: the
application builds, renders and passes unit tests while being completely unusable with a remote.
Driving real key events through DevFlow and reading back which element reports focus is the only way
to observe it.

The harness presses `Down Down Down Right Up Left` and, after each press, queries the tree for the
focused element. Two failures are detected:

**Nothing focused.** A TV screen where no element reports focus is unreachable by remote. The
harness fails immediately and names the key that caused it.

**Focus never moves.** If every press lands on the same element, that is a focus trap. Counting
distinct visited elements catches it; asserting only "something is focused" would not.

Cases participating in focus traversal declare `remote-navigate` in the catalog manifest, and a
hosted test asserts that any case doing so also targets the `tv` profile — a remote-navigate case
that never runs on TV is a silent gap.

The `tv` profile also sets `requiresFocusNavigation: true`, asserted by
`CatalogAndBaselineConventionTests.TvProfile_RequiresFocusNavigation`. A TV lane that does not
exercise remote traversal is not testing the thing that most commonly breaks on TV.

### Planned additions

| Case | Asserts |
|---|---|
| Traversal order | Focus follows visual order, not declaration order |
| Wrap-around | Edge behaviour is intentional, not accidental |
| Focus restore | Returning to a page restores the previously focused element |
| Scroll-into-view | Focusing an off-screen item scrolls it into view |
| Long-press / hold | Repeat behaviour on held keys |

## Why these are scripts rather than tests

The lifecycle and focus harnesses are repository-owned shell scripts, not xunit tests, because they
orchestrate processes outside the test host: `sdb`, application launch, and HTTP calls through a
tunnel. Wrapping that in a test host adds a layer that obscures failures without adding assertions.

The *decision logic* they depend on — capability policy, privilege gating, connection descriptors —
is unit-tested in `Maui.Tizen.DevFlow.Tests`, so the scripts are left to do only orchestration.
