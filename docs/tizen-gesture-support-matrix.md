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

Measured against **MAUI 11.0.0-preview.7.26426.4**, which contains
[dotnet/maui#37420](https://github.com/dotnet/maui/pull/37420) and
[#37671](https://github.com/dotnet/maui/pull/37671).

| Recognizer | Detection (NUI) | Dispatch (public MAUI API) | End to end |
|---|---|---|---|
| `PanGestureRecognizer` | `PanGestureDetector` | `IPanGestureController` | ✅ Works |
| `PinchGestureRecognizer` | `PinchGestureDetector` | `IPinchGestureController` | ✅ Works |
| `SwipeGestureRecognizer` | `PanGestureDetector` | `ISwipeGestureController` | ✅ Works |
| `TapGestureRecognizer` | `TapGestureDetector` | `SendTapped` | ✅ Works |
| `PointerGestureRecognizer` | `View.TouchEvent` + `View.HoverEvent` | `SendPointerEntered` / `Exited` / `Moved` / `Pressed` / `Released` | ✅ Works |
| `LongPressGestureRecognizer` | `LongPressGestureDetector` | ❌ `SendLongPressed` / `SendLongPressing` still internal | ⚠️ Blocked on MAUI |
| `DragGestureRecognizer` | ❌ no view-level NUI equivalent | `SendDragStarting` / `SendDropCompleted` | ❌ Not supported (detection) |
| `DropGestureRecognizer` | ❌ no view-level NUI equivalent | `SendDragOver` / `SendDragLeave` / `SendDrop` | ❌ Not supported (detection) |

Legend: ✅ works today · ⚠️ implemented and tested up to the blocking seam · ❌ not supported.

Note the change in *why* drag and drop are unsupported. Their dispatch members are public as of
26426.4; what is missing is detection. See [Drag and drop](#drag-and-drop).

---

## The remaining dispatch gap

Most of this gap has closed. MAUI 11.0.0-preview.7.26426.4 makes the tap, pointer and
drag/drop dispatch members public, on top of the three controller interfaces that were
already public:

```text
Microsoft.Maui.Controls.IPanGestureController            (already public)
Microsoft.Maui.Controls.IPinchGestureController          (already public)
Microsoft.Maui.Controls.ISwipeGestureController          (already public)
TapGestureRecognizer.SendTapped                          (new in #37420 / #37671)
PointerGestureRecognizer.SendPointerEntered/Exited/…     (new in #37420 / #37671)
DragGestureRecognizer.SendDragStarting/SendDropCompleted (new in #37420 / #37671)
DropGestureRecognizer.SendDragOver/SendDragLeave/SendDrop(new in #37420 / #37671)
```

**Exactly two members are still internal**, and they are the only reason any ⚠️ row remains:

```text
LongPressGestureRecognizer.SendLongPressed(View sender, Func<IElement?, Point?> getPosition)
LongPressGestureRecognizer.SendLongPressing(View sender, GestureStatus status, Func<IElement?, Point?> getPosition)
```

Both were verified by reflecting over the shipped 26426.4 assembly, not by reading source:
they are absent from `BindingFlags.Public` and present under `BindingFlags.NonPublic`. There is
no `ILongPressGestureController` either. `TizenGestureDispatcherTests` in this repository
asserts precisely that, so the claim cannot silently rot.

### How it is handled here

Detection is implemented in full for every gesture. Dispatch goes through one seam,
`ITizenGestureDispatcher`:

- `TizenGestureDispatcher` raises pan, pinch and swipe through the public controllers, and tap
  and pointer through their public send members.
- For long press it logs once and returns. It never throws, so a view carrying a
  `LongPressGestureRecognizer` behaves exactly as if it had no gesture rather than crashing.
- `ITizenGestureDispatcher.IsSupported(TizenGestureKind)` reports the matrix above.

`LongPressCannotBeRaisedBecauseMauiKeepsTheApiInternal` asserts the recognizer's events do
**not** fire, and `LongPressSendMembersAreStillInternalUpstream` asserts the two members are
still non-public. Both fail once upstream opens the API, and the only change needed is to
complete `TizenGestureDispatcher` — no handler, detector or lifecycle code has to move.

### Position resolution

The new tap and pointer members take a `Func<IElement?, Point?> getPosition` rather than a plain
point, because MAUI documents the parameter as *"the element to use as the coordinate reference,
or `null` for **screen** coordinates"*. Three distinct cases:

| `relativeTo` | Returned |
|---|---|
| `null` | The **screen** position |
| The view the gesture occurred on | The view-local position |
| Any other element | `null` — cannot be determined |

Answering the `null` case with a view-local coordinate is silently wrong, which is why the native
detectors report both spaces: `TapGesture.ScreenPoint`, `LongPressGesture.ScreenPoint`,
`PanGesture.ScreenPosition`, `PinchGesture.ScreenCenterPoint`, `Touch.GetScreenPosition` and
`Hover.GetScreenPosition`.

When a native event carries no screen position, the screen case returns `null` rather than
substituting the local one — an honest "unknown" instead of a wrong number.

The third row is `null` because translating into another element's space needs that element's
on-screen origin, which requires a native call per element that the Tizen platform layer does not
expose to this assembly.

### Button masks

`TapGestureRecognizer.Buttons` and `PointerGestureRecognizer.Buttons` let an app ask for a
specific button. The dispatcher filters on them, so a recognizer configured for `Primary` never
fires on a right-click and vice versa.

Buttons come from `Touch.GetMouseButton`. `Tizen.NUI.Hover` exposes no equivalent — a hover is
pointer movement with nothing pressed — so hover transitions report no button.

| Native | Reported |
|---|---|
| `MouseButton.Primary` | `ButtonsMask.Primary` |
| `MouseButton.Secondary` | `ButtonsMask.Secondary` |
| `MouseButton.Tertiary` | `ButtonsMask.Primary` |
| `MouseButton.Invalid` (touch) | `ButtonsMask.Primary` |

Touch input has no button, and NUI reports `Invalid` for it. It maps to `Primary`, matching how
MAUI's own touch backends report a finger press. Anything unclassified maps to `Primary` too, so a
stray value can never fabricate a right-click — the failure direction that would actually surprise
a user.

### Pixel scaling

Native coordinates are device pixels; MAUI gesture events are device-independent units. The
conversion factor comes from `DeviceInfo.ScalingFactor`, registered by
`AddTizenNuiControlsPlatform` via `AddTizenPixelScaler`.

This is not cosmetic: Tizen wearables and TVs do not run at 1x, so an identity scaler makes every
pan, swipe, pinch, tap and pointer coordinate wrong by the display factor. The neutral
`AddTizenGestures` still registers an identity fallback with `TryAdd` so host-side tests work
unconfigured, but the platform layer registers the real scaler first and therefore always wins.

### What upstream still needs to change

Making `SendLongPressed` and `SendLongPressing` public — exactly as #37420 did for tap and
pointer — is sufficient. No new interface is required.

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

`TapGesture.NumberOfTaps` is compared against `TapGestureRecognizer.NumberOfTapsRequired` and
non-matching counts are ignored, matching the original backend. Dispatched through the public
`SendTapped`.

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

Events are never consumed, so the view's own handlers still run. Each transition is dispatched
through its matching public send member.

`PlatformPointerEventArgs` is left `null` and `ButtonsMask` at its default. Both parameters are
optional; NUI reports neither a platform-native pointer event object nor a button mask for touch
and hover, so supplying a fabricated value would be misleading.

### Drag and drop

Not supported, for two reasons:

- **Detection.** NUI's drag-and-drop support is window/scene level and is driven by an
  explicit `Tizen.NUI.DragAndDrop` session started by the application. It does not map onto
  .NET MAUI's per-view `DragGestureRecognizer` / `DropGestureRecognizer` semantics, which
  expect the platform to originate a drag from a view based on its recognizer configuration.
- **Dispatch.** No longer the blocker: `SendDragStarting`, `SendDropCompleted`, `SendDragOver`,
  `SendDragLeave` and `SendDrop` are all public as of 26426.4. Drag and drop remain unsupported
  purely because there is nothing on the Tizen side to drive them.

`TizenGestureHandlerFactory` returns `null` for both recognizer types, so they are skipped
rather than throwing. `DragAndDropRecognizersAreNotSupported` covers this.

---

## Tizen profiles

The gesture stack is NUI-based and profile-independent: `TapGestureDetector`,
`PanGestureDetector`, `PinchGestureDetector` and `LongPressGestureDetector` are part of core
TizenFX and are present on every profile.

| Profile | Pan | Pinch | Swipe | Tap | Pointer | Long press (det.) | Drag/drop |
|---|---|---|---|---|---|---|---|
| Mobile | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| Wearable | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ❌ |
| TV | ✅ | ❌ | ✅ | ✅ | ⚠️ | ✅ | ❌ |
| IoT / headed | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ❌ |

Long press is marked "det." because detection works on every profile but dispatch is still gated
on the two internal members above. Every other ✅ is end to end.

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
| Dispatch through real MAUI recognizers, screen/local/unknown position resolution, button masks, and the one blocked gesture | `tests/Controls.UnitTests/TizenGestureDispatcherTests.cs` |
| Pixel scaler registration and lazy display-factor lookup | `tests/Controls.UnitTests/TizenServiceRegistrationTests.cs` |
| DI registration and lifetimes | `tests/Controls.UnitTests/TizenServiceRegistrationTests.cs` |
| NUI adapters under `Core/Platform/Nui` | Type-checked against `Samsung.Tizen.Ref.API15` and `Tizen.UIExtensions.NUI` 0.9.2 by `tests/Maui.Tizen.Controls.RefPackCompile`; behaviour needs a device |

The NUI adapters cannot be executed until the Samsung .NET 11 workload ships
(`eng/baselines.json` → `target.workloadManifest`). They *can* be type-checked without it:
`Samsung.Tizen.Ref.API15` publishes real `ref/net8.0` reference assemblies, so
`tests/Maui.Tizen.Controls.RefPackCompile` compiles the sources against them on a plain
`net11.0` host. That lane is how the non-existent `SetMinimumHoldingTime` call described
above was found. Device tests for the adapters are the remaining gap and are blocked on the
same workload.
