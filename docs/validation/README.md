# Validation

How this repository proves the Tizen backend works, and — just as importantly — what it currently
cannot prove and why.

## The shape of the problem

The primary target is `net11.0-tizen11.0`. **Nobody can build it today.** The Samsung workload
manifest `samsung.net.sdk.tizen.manifest-11.0.100` has not been published, so any Tizen target
framework fails restore with `NETSDK1139`. See [blockers](blockers.md).

That single fact drives the whole design. If validation were built around "run the tests on a
device", there would be no validation at all until an external dependency ships, and the harness
itself would be entirely unexercised on the day it finally mattered.

So the work is split by *what blocks it*, not by what it tests:

| Lane | Runs | Blocked by | Required for |
|---|---|---|---|
| [Hosted](hosted-lane.md) | Every push and pull request, on `ubuntu-latest` | Nothing | Every PR |
| [Device](device-lane.md) | Nightly, on demand, and on release tags, on self-hosted Samsung infrastructure | Samsung workload **and** device availability | Release only |

Everything that can be pulled into the hosted lane has been. That includes some things which look
device-bound at first glance — the DevFlow API contract, the capability and privilege policy, the
native-element registry, package content, and consumer restore.

## Running it

```bash
# Everything that needs no Tizen workload. This is what CI runs.
./eng/validation/run-hosted-validation.sh

# Device lane, on a machine with Tizen Studio and a target attached.
./eng/validation/scripts/tizen-device-lane.sh preflight
```

`preflight` always exits 0 and reports availability as structured output. Callers decide whether an
unavailable lane is acceptable; for pull requests it is, for releases it is not.

## What is validated where

| Concern | Where | Notes |
|---|---|---|
| Baseline ↔ `Directory.Build.props` consistency | Hosted | The check the root props file promises |
| Package content contracts | Hosted | `eng/validation/package-contents/*.contract.txt` |
| Dependency policy (MAUI Graphics 6.x via UIExtensions) | Hosted | Tripwire against the real published package |
| MSBuild / `buildTransitive` behaviour | Hosted | Real `dotnet build` against fixtures |
| Handler mapper/command parity | Both | Engine + fakes hosted; live mappers on device |
| DI registration, Essentials coverage | Both | Same split |
| Public API | Hosted (files) + device (analyzer) | See [hosted lane](hosted-lane.md) |
| API15 removed/deprecated APIs | Hosted | [Source guards](api15-guards.md), verified against the pinned reference pack |
| Consumer restore | Hosted | Synthetic today, real packages when they exist |
| DevFlow agent API contract | Hosted | Guards code that cannot be compiled |
| Screenshots, lifecycle, TV focus | Device | [Baselines](visual-baselines.md), [input](lifecycle-input-tv.md) |
| MAUI.Sherpa smoke | External | [Contract](sherpa.md) |

## Skips are load-bearing

A large amount of this suite legitimately cannot run yet. Every skip carries a reason naming what
is missing and which lane covers it, and the CI summary prints them. A suite that quietly skips
everything is indistinguishable from one that passes, which is the specific failure this lane is
designed to avoid.

Current skips on a hosted run: 7 of 133 tests.

## Contents

- [Hosted lane](hosted-lane.md)
- [API15 source guards](api15-guards.md)
- [Device lane](device-lane.md)
- [CI and release gating](ci.md)
- [DevFlow agent](devflow.md)
- [Visual baselines](visual-baselines.md)
- [Lifecycle, input and TV focus](lifecycle-input-tv.md)
- [MAUI.Sherpa consumer head](sherpa.md)
- [Blockers](blockers.md)
