# Tizen gesture support matrix

What the Tizen backend can actually deliver for each .NET MAUI gesture recognizer, and why.

Two independent things have to be true for a gesture to work end to end:

1. **Detection** — Tizen/NUI must be able to observe the gesture on a view.
2. **Dispatch** — .NET MAUI must expose a *public* way to raise the gesture on its recognizer.

They fail independently, so they are tracked separately below. This repository is an
out-of-tree backend: it uses only public .NET MAUI API and does not use `DispatchProxy`,
`InternalsVisibleTo`, or private reflection.

---

## Summary

| Recognizer | Detection (NUI) | Dispatch (public MAUI API) | End to end |
|---|---|---|---|
| `PanGestureRecognizer` | `PanGestureDetector` | `IPanGestureController` | ✅ Works |
| `PinchGestureRecognizer` | `PinchGestureDetector` | `IPinchGestureController` | ✅ Works |
| `SwipeGestureRecognizer` | `PanGestureDetector` | `ISwipeGestureController` | ✅ Works |
| `TapGestureRecognizer` | `TapGestureDetector` | ❌ `SendTapped` is internal | ⚠️ Blocked on MAUI |
| `LongPressGestureRecognizer` | `LongPressGestureDetector` | ❌ `SendLongPressing` / `SendLongPressed` are internal | ⚠️ Blocked on MAUI |
| `PointerGestureRecognizer` | `View.TouchEvent` + `View.HoverEvent` | ❌ all send members internal | ⚠️ Blocked on MAUI |
| `DragGestureRecognizer` | ❌ no view-level NUI equivalent | ❌ `SendDragStarting` is internal | ❌ Not supported |
| `DropGestureRecognizer` | ❌ no view-level NUI equivalent | ⚠️ only `SendDragOver` is public | ❌ Not supported |

Legend: ✅ works today · ⚠️ implemented and tested up to the blocking seam · ❌ not supported.

---

## The dispatch gap

.NET MAUI 11 exposes exactly three public gesture controller interfaces:

```text
Microsoft.Maui.Controls.IPanGestureController
Microsoft.Maui.Controls.IPinchGestureController
Microsoft.Maui.Controls.ISwipeGestureController
```

There is no `ITapGestureController`, no `ILongPressGestureController`, and no pointer
equivalent. `TapGestureRecognizer`, `LongPressGestureRecognizer` and
`PointerGestureRecognizer` expose no public `Send*` members at all — verified by reflecting
over the shipped `Microsoft.Maui.Controls` assembly, not by reading source.

This is a **true public API gap**, not a limitation of Tizen. The same gap blocks any
out-of-tree backend from supporting these gestures.

### How it is handled here

Detection is implemented in full. Dispatch goes through one seam,
`ITizenGestureDispatcher`:

- `TizenGestureDispatcher` raises pan, pinch and swipe through the public controllers.
- For tap, long press and pointer it logs once per gesture kind and returns. It never
  throws, so a view carrying a `TapGestureRecognizer` behaves exactly as if it had no
  gesture rather than crashing.
- `ITizenGestureDispatcher.IsSupported(TizenGestureKind)` reports the matrix above.

`TizenGestureDispatcherTests` pins this reality: `TapCannotBeRaisedBecauseMauiKeepsTheApiInternal`
and its siblings assert that the recognizer's event does **not** fire. When the upstream
API lands, those tests fail loudly and the only change needed is to complete
`TizenGestureDispatcher` — no handler, detector or lifecycle code has to move.

### What upstream needs to change

Any one of these would unblock the ⚠️ rows:

1. Make the existing `SendTapped` / `SendLongPressing` / `SendLongPressed` and pointer send
   members public, mirroring what was already done for pan, pinch and swipe; or
2. add `ITapGestureController`, `ILongPressGestureController` and `IPointerGestureController`
   public interfaces alongside the existing three.

Option 2 is more consistent with how pan, pinch and swipe are already exposed, and was the
shape used by [dotnet/maui#36655](https://github.com/dotnet/maui/pull/36655) when it made
`IGesturePlatformManager` and `IGesturePlatformManagerFactory` public.

---

## Per-recognizer notes

### Pan

Ported from the NUI `PanGestureHandler`. NUI reports **per-frame displacement**, while
.NET MAUI expects the **running total** since the pan began, so the handler accumulates it
and converts device pixels to device-independent units. Each pan gets a fresh gesture id.

### Swipe

Tizen has no swipe detector. A `PanGestureDetector` backs swipe recognition and the
accumulated movement is handed to `ISwipeGestureController`, which applies
`SwipeGestureRecognizer.Threshold` and decides whether the movement qualifies. This
matches the original NUI backend.

Because pan and swipe share a detector type, a view carrying both recognizers allocates two
independent detectors. Native gestures are left unconsumed (`Handled = false`) so they
coexist.

### Pinch

Ported from the NUI `PinchGestureHandler`. The native scale is relative to the start of the
gesture, so it is composed with the view's scale captured when the pinch began:
`1 + (nativeScale - 1) * scaleAtStart`. The pinch centre is expressed as a fraction of the
view. A view that has not been measured yet reports a zero size; the handler degrades to
the origin rather than producing `NaN`.

### Tap

`TapGestureGesture.NumberOfTaps` is compared against
`TapGestureRecognizer.NumberOfTapsRequired` and non-matching counts are ignored, matching
the original backend. Dispatch is blocked (see above).

### Long press

`LongPressGestureDetector` supports the touch count only.

> **`MinimumPressDuration` is not honoured on Tizen.** `Tizen.NUI.LongPressGestureDetector`
> exposes no minimum-holding-time API — only `SetTouchesRequired`. The system-wide long-press
> duration applies instead.
>
> Note that `LongPressGestureHandler.cs` on dotnet/maui `net11.0` calls
> `NativeDetector.SetMinimumHoldingTime(...)`. **That method does not exist in TizenFX**
> (checked against `Samsung.Tizen.Ref` API13 and API15). That source has not been compiled
> since Tizen was dropped from the MAUI build, so the call was never validated. Do not
> restore it during a future sync — it will not compile.

### Pointer

No upstream equivalent: the original NUI backend had no `PointerGestureRecognizer` support
at all. Implemented here by subscribing to the view's `TouchEvent` and `HoverEvent` and
mapping `PointStateType` onto pointer transitions:

| Source | `PointStateType` | Pointer action |
|---|---|---|
| Touch | `Down` | `Pressed` |
| Touch | `Up` | `Released` |
| Touch | `Motion` | `Moved` |
| Touch | `Leave` | `Exited` |
| Hover | `Started` | `Entered` |
| Hover | `Motion` | `Moved` |
| Hover | `Finished`, `Leave` | `Exited` |

Events are never consumed, so the view's own handlers still run. Dispatch is blocked.

### Drag and drop

Not supported, for two reasons:

- **Detection.** NUI's drag-and-drop support is window/scene level and is driven by an
  explicit `Tizen.NUI.DragAndDrop` session started by the application. It does not map onto
  .NET MAUI's per-view `DragGestureRecognizer` / `DropGestureRecognizer` semantics, which
  expect the platform to originate a drag from a view based on its recognizer configuration.
- **Dispatch.** `DropGestureRecognizer` only exposes `SendDragOver` publicly. The members
  needed to complete a drop, and everything needed to start a drag, are internal.

`TizenGestureHandlerFactory` returns `null` for both recognizer types, so they are skipped
rather than throwing. `DragAndDropRecognizersAreNotSupported` covers this.

---

## Tizen profiles

The gesture stack is NUI-based and profile-independent: `TapGestureDetector`,
`PanGestureDetector`, `PinchGestureDetector` and `LongPressGestureDetector` are part of core
TizenFX and are present on every profile.

| Profile | Pan | Pinch | Swipe | Tap (det.) | Long press (det.) | Pointer (det.) | Drag/drop |
|---|---|---|---|---|---|---|---|
| Mobile | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| Wearable | ✅ | ⚠️ | ✅ | ✅ | ✅ | ⚠️ | ❌ |
| TV | ✅ | ❌ | ✅ | ✅ | ✅ | ⚠️ | ❌ |
| IoT / headed | ✅ | ⚠️ | ✅ | ✅ | ✅ | ⚠️ | ❌ |

"det." means detection only; dispatch is still gated on the MAUI API gap above.

Profile caveats:

- **Wearable.** Pinch is detectable but rarely usable: small round displays make two-finger
  gestures impractical, and many wearable devices report only a single touch point. Hover is
  not reported, so pointer degrades to touch-derived transitions only.
- **TV.** Input is remote-based. There is no touch digitiser, so pinch cannot occur. Pan and
  swipe arrive from directional input, and pointer covers only the on-screen cursor where
  the device provides one.
- **IoT / headed.** Depends entirely on the attached input device. Treat it as mobile when a
  touchscreen is present, otherwise as TV.

Nothing in the backend hard-codes these rows. An unsupported gesture surfaces as
`ITizenNativeGestureDetectorFactory.CreateDetector` returning `null`, which
`TizenGestureDetector` treats as "skip this recognizer", so the rest of a view's gestures
keep working. `UnsupportedGestureKindsProduceNoHandler` covers that path. A profile-specific
factory can therefore refine this table without changing any other code.

---

## Verification

| Layer | How it is verified today |
|---|---|
| Gesture translation (totals, scaling, gesture identity, tap counts, pointer mapping) | `tests/Controls.UnitTests/TizenGestureTranslationTests.cs` |
| Manager and detector lifecycle (attach, detach, enable, dispose, collection changes) | `tests/Controls.UnitTests/TizenGesturePlatformManagerTests.cs` |
| Dispatch through real MAUI recognizers, and the blocked gestures | `tests/Controls.UnitTests/TizenGestureDispatcherTests.cs` |
| DI registration and lifetimes | `tests/Controls.UnitTests/TizenServiceRegistrationTests.cs` |
| NUI adapters under `Core/Platform/Nui` | Compile-checked against `Samsung.Tizen.Ref` API13 and `Tizen.UIExtensions.NUI` 0.9.2; behaviour needs a device |

The NUI adapters cannot be executed until the Samsung .NET 11 workload ships
(`eng/baselines.json` → `target.workloadManifest`). They *can* be compile-checked without
it, by compiling the sources against the TizenFX reference assemblies directly; that is how
the non-existent `SetMinimumHoldingTime` call described above was found. Device tests for
the adapters are the remaining gap and are blocked on the same workload.
