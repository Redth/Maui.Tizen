# Device lane

Validation that genuinely requires Samsung hardware or an emulator: versioned `net11.0-tizen*`
builds, TPK creation, deploy and run, live handler parity, visual baselines, lifecycle, and TV
remote focus traversal.

Workflow: `.github/workflows/tizen-device-validation.yml`
Driver: `eng/validation/scripts/tizen-device-lane.sh`

## Status

**Unavailable.** Two independent gates, either of which is sufficient to stop the lane:

1. The Samsung workload manifest is unpublished — see [blockers](blockers.md#1-the-samsung-net-11-workload-is-unpublished).
2. No device or emulator infrastructure is attached to this repository.

The lane is written, wired and syntactically verified. It has never executed against hardware, and
nothing in this repository claims otherwise.

## No infrastructure in the repository

Nothing here names a machine, serial, account or URL. That is a hard rule, checked by
`CatalogAndBaselineConventionTests.ProfileMatrix_EmulatorNotesCarryNoPersonalInfrastructure` and by
`SherpaSmokeContractTests.ContractCarriesNoCredentialsOrPrivateInfrastructure`.

Environment-specific values arrive at runtime:

| Variable | Purpose |
|---|---|
| `TIZEN_PROFILE` | `mobile` or `tv` |
| `TIZEN_MOBILE_SERIAL` / `TIZEN_TV_SERIAL` | `sdb` serial per profile. The matrix binds each profile to an explicit serial: a runner label says which *machine* to use, not which of several attached targets to drive, so a `tv` job could otherwise silently run against a handset. |
| `TIZEN_DEVICE_SERIAL` | Serial used by a manual invocation of the script |
| `TIZEN_DEVICE_IMAGE` | Recorded in capture sidecars; must name an image, never a machine |
| `TIZEN_TFM` | Target framework; defaults to `eng/baselines.json > target.targetFramework` |
| `DEVFLOW_HOST_PORT` / `DEVFLOW_DEVICE_PORT` | Tunnel ports, default `9223` |
| `APP_ID` | Application id for the lifecycle harness |

Runner selection is by label (`[self-hosted, tizen]`); enablement is the `TIZEN_DEVICE_LAB_ENABLED`
repository variable.

## Setting up a runner

1. Install Tizen Studio with the mobile and TV extensions; ensure `sdb` and `tizen` are on `PATH`.
2. Install the Samsung workload: `dotnet workload install tizen`. *(Blocked until the `11.0.100`
   band is published.)*
3. Create or attach an emulator or device.
4. Register a self-hosted runner with the `tizen` label.
5. Set the `TIZEN_DEVICE_LAB_ENABLED` repository variable to `true`.
6. Create a `tizen-device-lab` environment, and a `tizen-release` environment with required
   reviewers.

| Variable | Purpose |
|---|---|
| `TIZEN_CATALOG_PROJECT` | Path to the application under test. **Unset today**, so the device job reports no application and a release is blocked rather than passing vacuously. There is deliberately no hard-coded path: a workflow step that builds a non-existent project looks plausible for as long as the job never runs. |
| `TIZEN_CATALOG_APP_ID` | Tizen application id, for launch and lifecycle. |
| `TIZEN_HOME_APP_ID` | Home application id, used to background the app under test. Profile-specific, so it has no default. |

Verify with:

```bash
./eng/validation/scripts/tizen-device-lane.sh preflight
```

It always exits 0 and emits `workload_available`, `tooling_available`, `device_available` and
`lane_available` as structured output. The caller decides whether an unavailable lane is acceptable.

## Preflight checks the right workload

Preflight greps for the `tizen` workload, not `maui-tizen`. The latter contains no Tizen platform
packs and reporting it as installed is the single most common false positive in this area — see
[blockers](blockers.md#the-trap-maui-tizen-is-not-the-tizen-workload).

## The matrix

Profiles come from `eng/validation/profiles/tizen-profiles.json`, the same file that drives the
baseline layout and the catalog test plan. Adding a profile is a one-file change rather than an edit
spread across YAML, folder names and assertions.

| Profile | Input | Themes | Densities | Focus traversal |
|---|---|---|---|---|
| `mobile` | touch, key | light, dark | mdpi, hdpi, xhdpi | no |
| `tv` | remote, key | dark | fhd, uhd | **yes** |

Only `net11.0-tizen11.0` gates a release. `alsoValidTargets` (`tizen10.1`, `10.0`, `9.0`, `8.0`) are
marked `confirmed: false` and are exercised opportunistically, because they have not been verified
against real Samsung tooling. A test asserts none of them claims to be confirmed — a confirmed
target belongs in `eng/baselines.json`, not in an opportunistic list.

## The app must be built with the agent compiled in

`AddMauiDevFlowAgent()` is conventionally guarded by `#if DEBUG`, so a Release build excludes the
agent entirely and the driver has nothing to talk to. The device lane therefore builds with
`-p:MauiTizenValidation=true`, and the application is expected to guard registration as:

```csharp
#if DEBUG || MAUITIZEN_DEVFLOW
    builder.AddMauiDevFlowAgent();
#endif
```

The property is expected to define `MAUITIZEN_DEVFLOW`. This keeps the agent out of shipping builds
while allowing a Release-configuration build to be driven.

## Install, launch, tunnel, wait - in that order

Installing does not start anything. An earlier version installed, forwarded and then queried the
agent, which could only have worked if something else had already launched the app.

The wait is equally load-bearing: the agent binds its port during application startup, so a query
issued immediately after launch fails in a way that looks like a broken tunnel.
`wait-for-agent` polls with a bounded timeout (`AGENT_TIMEOUT_SECONDS`, default 60) and, on
timeout, says to check whether the agent was compiled in at all.

`WorkflowOrderingTests` asserts this order.

## Baselines are captured *and* compared

`baselines` captures into the same folder shape as the baselines themselves —
`{profile}/{apiLevel}/{theme}/{density}/{caseId}.png` — so each capture maps to exactly one
baseline rather than being matched by guesswork. It then runs the comparison
(`VisualBaselineComparisonTests`), which uses the deterministic comparer and writes
`expected.png` / `actual.png` / `diff.png` per failure into `artifacts/visual-diffs/`. The workflow
uploads screenshots and diffs whatever the outcome: on failure they are the evidence, on success
they are what a reviewer needs to approve an intentional visual change.

Theme and density describe how the *device* is configured, so they come from `TIZEN_THEME` and
`TIZEN_DENSITY`, defaulting to the first entry in the profile matrix.

All DevFlow calls use `curl --fail`. Without it curl exits 0 on a 4xx/5xx and writes the error body
to the output file, so a `501` would be saved as a `.png` and the capture step would report success.

## One profile at a time, on distinct ports

The matrix sets `max-parallel: 1` and gives each profile its own host port (mobile 9223, tv 9224).
Either alone would be insufficient: distinct ports stop a leaked `sdb forward` from capturing the
other profile's traffic, and serialising stops two jobs contending for `sdb` and for the attached
targets.

## Only device work runs on device hardware

The device lane runs the comparison suite (`MAUI_TIZEN_SUITES`), not the whole hosted lane. The
hosted suites already ran on `ubuntu-latest`; repeating them on scarce lab hardware would spend
device time re-proving things that never touch a device.

## Lifecycle is a real suspend/resume

`lifecycle` brings the **home application** to the foreground to background the app under test,
rather than terminating it. Using `app_launcher -k` and relaunching, as an earlier version did,
tests process startup - and suspend/resume is precisely where Tizen apps lose state or fail to
re-attach their renderer, because the process survives and the surface does not.

Three things are asserted after resume, because each fails independently:

1. the process is still running after backgrounding (a terminated app is a lifecycle failure);
2. the agent responds and the visual tree is non-empty (handlers re-attached - an app can answer
   `/agent/status` with a detached renderer);
3. a marker written before backgrounding survives (proving this was a resume, not a cold start).

## On-device assertions run on the device

Mapper parity and Essentials coverage need the Tizen backend executing in-process, so the device
lane invokes them through a DevFlow extension endpoint inside the deployed app
(`device-assertions`). Running `run-hosted-validation.sh` on the self-hosted controller instead
would load no Tizen backend, and those suites would skip there exactly as on any hosted runner - a
device lane that validated nothing. A run that reports zero assertions is treated as a failure for
the same reason.

## Why the availability job runs on a hosted runner

If the whole workflow required the self-hosted agent, an offline agent would leave every run queued
indefinitely and the result would read "in progress" rather than "unavailable". The `availability`
job runs on `ubuntu-latest` and decides whether the matrix should run at all.

Similarly, the matrix steps are conditioned on the preflight result rather than wrapped in
`continue-on-error`, so an unavailable lane reports "not run" instead of a green step that did
nothing.

## Reaching the agent

A fixed device port plus an `sdb forward` tunnel, rather than dynamic ports or discovery. A device
is reached through `sdb` anyway, the tunnel makes emulator and physical device identical from the
driver's point of view, and a fixed port keeps teardown deterministic.

The trade-off is one agent per host port at a time. That is fine while the lane runs one target at a
time, and `TizenAgentConnection.HostPort` is configurable so a future parallel lane can allocate a
distinct host port per target while the device-side port stays fixed.

Teardown is unconditional (`if: always()`): a leaked forward silently captures the next job's
traffic.
