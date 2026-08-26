# Visual baselines

Reference screenshots for the control catalog. Nothing is checked in yet: baselines are produced by
the device lane, which is blocked on the Samsung workload
([blockers](blockers.md)).

## Layout

```
tests/VisualBaselines/{profile}/{apiLevel}/{theme}/{density}/{caseId}.png
tests/VisualBaselines/{profile}/{apiLevel}/{theme}/{density}/{caseId}.json
```

Example:

```
tests/VisualBaselines/mobile/API15/dark/hdpi/button-default.png
tests/VisualBaselines/mobile/API15/dark/hdpi/button-default.json
```

Every segment is there because it changes pixels — profile changes default metrics, API level
changes platform styling, theme changes palette, density changes rasterisation. Collapsing any of
them forces one image to represent several legitimately different renderings, which is how baseline
suites end up permanently disabled.

Valid combinations are generated from `eng/validation/profiles/tizen-profiles.json`, and `caseId`
must match a case in `samples/Maui.Tizen.Catalog/catalog-manifest.json`. Both are enforced by
`Maui.Tizen.Validation.Tests`.

## The `.json` sidecar

Required next to every image. Without provenance, a stale baseline is indistinguishable from a
correct one, and the only way to judge a diff is to re-capture and eyeball it.

```json
{
  "caseId": "button-default",
  "profile": "mobile",
  "apiLevel": "API15",
  "theme": "dark",
  "density": "hdpi",
  "targetFramework": "net11.0-tizen11.0",
  "deviceImage": "tizen-11.0-mobile-x86",
  "width": 720,
  "height": 1280,
  "commit": "79aa0f3",
  "capturedUtc": "2026-08-26T09:12:44Z"
}
```

`deviceImage` must identify an image, never a machine or an account.

## Tolerances

The default lives in `tizen-profiles.json > visualBaselines.defaultTolerance`:

- `maxChannelDelta: 2` — absorbs GPU rounding, not real colour changes.
- `maxDifferingPixelRatio: 0.001` — absorbs sub-pixel text rendering, not layout shifts.

A per-baseline override is allowed but **must** carry `toleranceJustification`. An unexplained
tolerance bump is how a genuine regression gets absorbed, so the suite rejects one without a reason.

## Comparison

`Maui.Tizen.TestUtils` implements PNG decode/encode and comparison directly, with no SkiaSharp or
ImageSharp dependency. Two reasons:

- No native assets, so comparison behaves identically on a hosted Linux runner, a developer Mac and
  the self-hosted Tizen lane.
- Determinism. Imaging libraries change resampling and colour handling between versions, which
  silently invalidates every checked-in baseline with no diff to review.

Supported subset: non-interlaced 8-bit PNG, colour types 0/2/4/6. Anything else throws with an
explicit message rather than being silently misread.

On failure the comparer writes `expected.png`, `actual.png`, `diff.png` and `summary.txt` into
`artifacts/visual-diffs/`, which CI uploads. Differences are marked magenta; matching regions are
dimmed so the mask stays readable as an image.

## Updating a baseline

Baselines are updated deliberately, never automatically. An auto-updating baseline suite asserts
nothing. The device lane emits the new image as an artifact; a human inspects the diff and commits
it with the sidecar regenerated.
