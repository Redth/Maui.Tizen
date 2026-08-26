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
| `TIZEN_DEVICE_SERIAL` | `sdb` serial; empty uses the sole attached target |
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
