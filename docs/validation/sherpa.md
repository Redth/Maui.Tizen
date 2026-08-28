# MAUI.Sherpa consumer head

Sherpa validates these packages the way a real application does. **Nothing in Sherpa changes in this
work**, and Sherpa is not built here.

Contract: `eng/validation/consumers/sherpa-smoke-contract.json`

## Why a separate consumer head

A source build proves the code compiles. It does not prove that the resulting package restores, that
its dependency graph is sane, or that its MSBuild logic works from the outside. Those problems only
appear for consumers, which is the worst possible place to find them.

Keeping Sherpa as a genuinely external consumer is what makes the signal meaningful. Wiring it into
this repository's build would let it resolve project references instead of packages, and it would
stop testing the thing it exists to test.

## The handoff

This repository owns the *contract*; Sherpa owns the *implementation*.

| Owned here | Owned by Sherpa |
|---|---|
| Which packages are consumed | How the head is written |
| How the feed is supplied | Its pipeline and runners |
| What each smoke step must prove | Its own application code |
| What a failure means | Reporting the status back |

`SherpaSmokeContractTests` validates the contract on every hosted run: it is well-formed, every step
explains what a failure means, every consumed package has a package-content contract, and no
credentials or infrastructure URLs are committed.

## Feed handoff

The feed is a **parameter**, never a committed value:

| Parameter | Purpose |
|---|---|
| `MAUI_TIZEN_PACKAGE_FEED` | Feed to restore from |
| `MAUI_TIZEN_PACKAGE_VERSION` | Package version to pin |

When unset, the smoke job reports "not run" rather than failing — no feed exists until the Samsung
workload unblocks packing.

A URL committed here would be both a leak and a hard dependency on one organisation's
infrastructure, so `ContractCarriesNoCredentialsOrPrivateInfrastructure` fails on any `http://` or
`https://` in the file.

## Smoke steps

| Step | Must prove | A failure means |
|---|---|---|
| `restore` | The head restores against the feed | Package metadata or dependency graph is wrong — **our** defect |
| `dependency-policy` | No banned resolutions in the restored graph | A banned transitive dependency reached a real consumer, most likely MAUI Graphics 6.x via `Tizen.UIExtensions` |
| `build` | The head builds for `net11.0-tizen11.0` | `buildTransitive` targets or assembly references are broken for consumers |
| `tpk` | A TPK is produced | Packaging or `tizen-manifest.xml` generation is broken |
| `launch` | Installs, launches, first page renders | Builds but does not run — typically a runtime dependency that only appears in a packaged app |

Every step states what a failure means, and the contract test enforces that it does. "The smoke test
failed" tells the Sherpa side nothing about whether the defect is theirs or ours, and that ambiguity
is what makes cross-repository signals get ignored.

`launch` is marked `requiresDeviceInfrastructure: true` so the Sherpa pipeline can report it as "not
run" rather than failing when no device lab is attached — the same tolerance policy as our own
[device lane](device-lane.md).

## Reporting back

A commit status on the Maui.Tizen commit that produced the packages:

- Context: `maui-tizen/sherpa-smoke`
- Required: **for release only**

Requiring it on every pull request would block this repository on another repository's
infrastructure, which is the wrong coupling for a signal that is inherently downstream.

## Sequencing

1. *(blocked)* Samsung workload ships → packages can be produced.
2. Publish to a feed and hand the feed and version to Sherpa as parameters.
3. Sherpa implements the head against this contract.
4. Sherpa reports `maui-tizen/sherpa-smoke` back.
5. Make the status required for release.
