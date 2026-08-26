# CI and release gating

## Jobs

`.github/workflows/ci.yml` — every push and pull request:

| Job | Purpose | Required |
|---|---|---|
| `workload-free` | Foundation build and repository invariants | yes |
| `hosted-validation` | The validation suites (this lane) | yes |
| `provenance` | Imported history has not been flattened | yes |
| `device-lane-status` | Reports device lane availability in the run summary | no (informational) |
| `tizen-workload-gate` | Attempts the Samsung workload install and reports | no (`continue-on-error`) |

`.github/workflows/tizen-device-validation.yml` — nightly, on demand, and on `v*` tags:

| Job | Purpose |
|---|---|
| `availability` | Decides whether the lane can and must run. Hosted runner. |
| `device-matrix` | The real work, on `[self-hosted, tizen]`, in the `tizen-device-lab` environment |
| `release-gate` | Converts "did not run" into pass-for-PR / fail-for-release |

## Tolerating missing infrastructure without hiding it

The requirement is to tolerate an unavailable device lane for pull requests while requiring it for a
release. Three mechanisms, each chosen over an easier alternative that would have hidden something:

**`availability` runs on a hosted runner.** If the whole workflow required the self-hosted agent, an
offline agent would leave runs queued forever and the status would read "in progress" rather than
"unavailable".

**Steps are conditioned on preflight output, not `continue-on-error`.** A `continue-on-error` step
that fails still reports green at the job level, so an unavailable lane would be indistinguishable
from a passing one. Conditioning on `steps.preflight.outputs.lane_available` makes the steps
genuinely "not run".

**`release-gate` always runs (`if: always()`)** and delegates the decision to
`eng/validation/scripts/evaluate-release-gate.sh`.

The decision is a script rather than inline YAML because the previous inline version contained a
hole that no test could catch: it only inspected the matrix job's *result*, and a matrix whose
device steps were all conditioned out still reports `success`. A device lane that validated nothing
passed the release gate.

The fix is that each profile writes a result file recording whether the lane was available and
whether it passed, and the gate requires one per required profile. Artifacts rather than job
outputs, because matrix job outputs collapse to a single last-writer-wins value and a passing
profile would mask a skipped one.

```
required = tag push v* OR workflow_dispatch(release_validation: true)

required == false                          -> pass, lane is informational
required == true, lab disabled             -> fail: no device lab attached
required == true, matrix != success        -> fail: names the actual result
required == true, any profile result absent-> fail: job did not complete
required == true, any lane_available!=true -> fail: nothing ran on hardware
required == true, any status != pass       -> fail
otherwise                                  -> pass
```

`Maui.Tizen.Validation.Tests.ReleaseGateTests` exercises this as a truth table, including the
skipped-but-successful case that the review found.

`device-lane-status` in `ci.yml` reports the same information on ordinary pull requests, so the
lane's absence is visible on every run rather than only when someone goes looking.

## Environments

| Environment | Purpose |
|---|---|
| `tizen-device-lab` | Scopes access to lab resources. No secrets are referenced by this repository. |
| `tizen-release` | Human approval before a release proceeds. |

## Repository variables

| Variable | Meaning |
|---|---|
| `TIZEN_DEVICE_LAB_ENABLED` | `true` when a `tizen`-labelled runner is registered |
| `TIZEN_CATALOG_APP_ID` | Application id used by the lifecycle harness |

Variables, not secrets — none of these are sensitive, and using secrets would make them invisible in
logs where they are useful for diagnosis.

## Release-only checks

Some assertions are noise on a pull request and essential at release. They are gated on
`MAUI_TIZEN_RELEASE_VALIDATION=1`:

- `UnshippedApiIsEmptyBeforeARelease` — the imported baseline starts with hundreds of pending API
  entries, so failing every PR on it would train people to ignore the suite.

The device workflow exports the flag automatically whenever the run is a release, and
`ReleaseReadinessTests.ReleaseValidationFlagIsWiredIntoTheReleaseWorkflow` fails if that wiring is
removed - otherwise every release-only gate would silently go back to skipping.

Under the flag these stop skipping and start failing:

| Gate | Requires |
|---|---|
| `EveryDeclaredPackageWasProduced` | a `.nupkg` for every package-content contract |
| `EveryProducedPackageSatisfiesItsContentContract` | each package matches its contract |
| `EveryRequiredVisualBaselineExists` | a baseline per catalog case, profile, theme and density |
| `EveryRequiredProfileReportedADeviceResult` | every gating profile ran on hardware and passed |
| `UnshippedApiIsEmptyBeforeARelease` | no pending public API |

Run them locally with:

```bash
MAUI_TIZEN_RELEASE_VALIDATION=1 ./eng/validation/run-hosted-validation.sh
```

## Recommended branch protection

Required: `workload-free`, `hosted-validation`, `provenance`.

Not required: `tizen-workload-gate`, `device-lane-status` — both are informational and both are
expected to be unable to do their real job today.

Promote `release-gate` to required on release branches only. Making it required on `main` would
block every pull request on infrastructure that deliberately is not attached to them.

## When the Samsung workload ships

1. `tizen-workload-gate` starts succeeding and says so in its summary.
2. Flip `eng/baselines.json > target.workloadManifest.status` to `available`.
3. Register a `tizen` runner and set `TIZEN_DEVICE_LAB_ENABLED=true`.
4. Several currently-skipped tests activate on their own — package content, consumer restore,
   `buildTransitive` validation, live parity and coverage.
5. Capture the first visual baselines and commit them with their sidecars.
