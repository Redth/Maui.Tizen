# Maui.Tizen control catalog

The catalog is the application the device lane drives. It exists to put every supported control on
screen in a known state so that screenshots, input and focus traversal can be exercised against
something stable.

## Current state

Only the **manifest** exists in this PR. `catalog-manifest.json` is machine-readable and already
drives real assertions on the hosted lane:

- `Maui.Tizen.Validation.Tests` validates it (unique slugs, known profiles, closed interaction
  vocabulary, remote-navigate cases targeting the TV profile).
- Every checked-in visual baseline must map back to a case in it, so orphaned baselines cannot
  accumulate.

The MAUI application itself is not here. It would target `net11.0-tizen11.0`, which
[cannot be built by anyone yet](../../docs/validation/blockers.md), and an application nobody can
compile would rot silently. The manifest is the part that can be kept honest today.

## Adding a control

1. Add a case to `catalog-manifest.json`.
2. Give it a kebab-case `id` — this is also the baseline image file name.
3. List the `profiles` it applies to. A case using `remote-navigate` must include `tv`.
4. Set `capturesBaseline` if it should have a visual baseline.
5. Use only interactions from `interactions.allowed`.

The hosted validation suite fails on anything malformed, so a bad entry is caught on the pull
request rather than at 3am in the nightly device run.

## Contract the app must satisfy

The device lane drives the app through DevFlow, so the application must:

1. **Register the agent under the validation constant.** A plain Release build excludes
   `AddMauiDevFlowAgent()` if it is guarded by `#if DEBUG` alone, and the lane builds Release with
   `-p:MauiTizenValidation=true`:

   ```csharp
   #if DEBUG || MAUITIZEN_DEVFLOW
       builder.AddMauiDevFlowAgent();
   #endif
   ```

2. **Expose the on-device conventions endpoint** at
   `extensions/maui-tizen/conventions/run`, returning `{ "total": n, "failed": [...], "skipped": [...] }`.
   Mapper parity and Essentials coverage need the Tizen backend executing in-process, so they can
   only run inside the app. A response reporting `total: 0` is treated as a failure.

3. **Support route navigation by case id**, so `ui/actions/navigate` with a catalog case id shows
   that case for capture.

4. **Render deterministically** — no animations still running at capture time, no clocks, no random
   data, no network.

## Intended layout

```
samples/Maui.Tizen.Catalog/
  catalog-manifest.json          <- the contract (exists today)
  Maui.Tizen.Catalog.csproj      <- net11.0-tizen11.0 app  (blocked on the Samsung workload)
  Pages/<case-id>.xaml           <- one page per case, deterministic content
  MauiProgram.cs                 <- calls AddMauiDevFlowAgent() under #if DEBUG
```

Pages must render deterministically: no animations left running at capture time, no clocks, no
random data, no network. Non-determinism in a catalog page shows up as a flaky baseline diff and
costs far more to diagnose than it saves to write.

## Related

- [Visual baselines](../../docs/validation/visual-baselines.md)
- [Lifecycle, input and TV focus](../../docs/validation/lifecycle-input-tv.md)
- [DevFlow agent](../../docs/validation/devflow.md)
