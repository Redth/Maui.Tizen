# Tizen Essentials service coverage matrix

`Maui.Tizen.Essentials` provides Tizen implementations of the .NET MAUI Essentials service
contracts as a standalone platform backend for **.NET 11 and newer**.

Registration is the whole integration surface: `AddTizenEssentials(MauiAppBuilder)` adds every
service below as a `TryAdd` singleton, and .NET 11 MAUI bridges DI-registered Essentials
services onto their static facades during `MauiApp` initialization (dotnet/maui#36657, first
available publicly in `11.0.0-preview.7.26418.3`). This package therefore performs no
`SetDefault` reflection, and ships no `MainThread` platform hook: main-thread marshalling is
bridged from the registered `IDispatcher`.

Support levels:

| Level | Meaning |
| --- | --- |
| `Implemented` | The whole contract is backed by native Tizen APIs. |
| `Partial` | Part of the contract is backed by native Tizen APIs. The remaining members throw `FeatureNotSupportedException` with an explicit reason. |
| `Unsupported` | Tizen has no API that can satisfy the contract. Every member throws `FeatureNotSupportedException`. Nothing is faked. |

Nothing in this backend returns a success-shaped fallback (an empty collection, `null`, or a
completed `Task`) to stand in for a capability the platform does not have.

Profile column values are the Tizen device profiles on which the service is usable:
`Mobile`, `Wearable`, `TV`, `Common` (IoT headed), or `All`.

<!-- coverage-matrix:begin -->

| Contract | Implementation | Level | Profiles | Notes |
| --- | --- | --- | --- | --- |
| `IAccelerometer` | `TizenAccelerometer` | Implemented | All | `Tizen.Sensor.Accelerometer`. Shake detection reimplemented (MAUI's `AccelerometerQueue` is internal). |
| `IAppActions` | `TizenAppActions` | Unsupported | – | Tizen has no home-screen shortcut / quick action API. `IsSupported` is `false`. |
| `IAppInfo` | `TizenAppInfo` | Partial | All | `RequestedTheme` is `Unspecified` and `RequestedLayoutDirection` is `LeftToRight`: Tizen exposes neither to applications. |
| `IAppleSignInAuthenticator` | `TizenAppleSignInAuthenticator` | Unsupported | – | Native Sign in with Apple is Apple-platform only. |
| `IBarometer` | `TizenBarometer` | Implemented | All | `Tizen.Sensor.PressureSensor`. |
| `IBattery` | `TizenBattery` | Partial | All | Power source read from Tizen's own reading (`None`/`Ac`/`Usb`/`Wireless`); state distinguishes `Full` and `NotCharging`. `EnergySaverStatus` / `EnergySaverStatusChanged` throw: Tizen exposes no application-visible power saving state. |
| `IBrowser` | `TizenBrowser` | Partial | All | Always opens the system browser: Tizen has no in-app browser, so `BrowserLaunchMode` cannot be honoured. |
| `IClipboard` | `TizenClipboard` | Unsupported | – | Tizen scopes copy/paste to the focused NUI/EFL window selection buffer; there is no headless clipboard service. |
| `ICompass` | `TizenCompass` | Implemented | All | Azimuth from `Tizen.Sensor.OrientationSensor`. `applyLowPassFilter` has no extra effect (Tizen already fuses and filters). |
| `IConnectivity` | `TizenConnectivity` | Implemented | All | `Tizen.Network.Connection`. `ConnectionProfiles` is queried synchronously per read, so it works without subscribing to `ConnectivityChanged`. Requires `network.get` + `internet`. |
| `IContacts` | `TizenContacts` | Implemented | Mobile | `Tizen.Pims.Contacts`. `GetAllAsync` requests runtime `contact.read` consent and materialises the result before disposing the native cursor. |
| `IDeviceDisplay` | `TizenDeviceDisplay` | Implemented | All | Screen metrics from feature keys; `KeepScreenOn` uses `device_power_request_lock` and requires the `display` privilege. |
| `IDeviceInfo` | `TizenDeviceInfo` | Implemented | All | System / feature information keys. |
| `IEmail` | `TizenEmail` | Partial | Mobile | Attachments are rejected: the Tizen `compose` AppControl accepts no attachment payload. Gated on the `email` feature key. |
| `IFilePicker` | `TizenFilePicker` | Partial | All | Tizen's `pick` operation accepts one MIME filter, so only the first entry of `PickOptions.FileTypes` is applied. |
| `IFileSystem` | `TizenFileSystem` | Implemented | All | Application `DirectoryInfo` data / cache / resource paths. |
| `IFlashlight` | `TizenFlashlight` | Implemented | Mobile | `Tizen.System.Led`. Gated on `camera.back.flash`; requires the `led` privilege. |
| `IGeocoding` | `TizenGeocoding` | Unsupported | – | `Tizen.Maps` (`MapService`) was deprecated in TizenFX API11 and **removed by API15**; there is no replacement. `MapServiceToken` is still accepted so a configured token cannot crash startup, but it is never used. |
| `IGeolocation` | `TizenGeolocation` | Partial | All | One-shot `GetLocationAsync` works. `StartListeningForegroundAsync` / `StopListeningForeground` throw. |
| `IGyroscope` | `TizenGyroscope` | Implemented | All | `Tizen.Sensor.Gyroscope`. |
| `IHapticFeedback` | `TizenHapticFeedback` | Implemented | Mobile, Wearable | `Tizen.System.Feedback`. Unsupported patterns throw instead of silently doing nothing. |
| `ILauncher` | `TizenLauncher` | Implemented | All | AppControl launch requests with scheme-based operation selection. `CanOpenAsync`/`TryOpenAsync` query matched application ids, so `TryOpenAsync` returns `false` instead of throwing when nothing handles the URI. |
| `IMagnetometer` | `TizenMagnetometer` | Implemented | All | `Tizen.Sensor.Magnetometer`. |
| `IMap` | `TizenMap` | Partial | All | `geo:` AppControl. `MapLaunchOptions.NavigationMode` and the launch name cannot be honoured. |
| `IMediaPicker` | `TizenMediaPicker` | Implemented | Mobile | Picking delegates to `TizenFilePicker`; capture uses the `image_capture` / `video_capture` AppControls. |
| `IOrientationSensor` | `TizenOrientationSensor` | Implemented | All | `Tizen.Sensor.RotationVectorSensor`. |
| `IPasskeys` | `TizenPasskeys` | Unsupported | – | Tizen ships FIDO UAF, not a WebAuthn / CTAP2 credential manager. `IsSupported` is `false`. |
| `IPermissions` | `TizenPermissions` | Partial | All | Maps every built-in `Permissions.*` type to verified Tizen privileges, with privacy privileges resolved through `PrivacyPrivilegeManager`. `Permissions.Maps` and `Permissions.Reminders` have no Tizen equivalent and throw rather than reporting `Granted`. |
| `IPhoneDialer` | `TizenPhoneDialer` | Implemented | Mobile | `tel:` AppControl, gated on the `contact` feature key. |
| `IPreferences` | `TizenPreferences` | Implemented | All | `Tizen.Applications.Preference`; shared names are emulated with an **escaped** `{sharedName}~{key}` prefix so distinct stores cannot collide. Default-store keys stay unprefixed for compatibility. |
| `IScreenshot` | `TizenScreenshot` | Implemented | All | `Tizen.NUI.Capture` plus `Tizen.Multimedia.Util` encoders; native handles disposed, RGBX/BGRX treated as opaque, and a bounded wait for `Capture.Finished`. Also implements `IViewScreenshot`. |
| `ISecureStorage` | `TizenSecureStorage` | Implemented | All | Tizen key manager. Versioned Base64url aliases are whitespace-free and private to this API; reads migrate earlier namespaced/raw aliases, while tombstones prevent removed raw values from resurfacing. |
| `ISemanticScreenReader` | `TizenSemanticScreenReader` | Implemented | All | `Tizen.NUI.Accessibility`. |
| `IShare` | `TizenShare` | Implemented | All | `share` / `share_text` AppControls. |
| `ISms` | `TizenSms` | Implemented | Mobile | `sms:` compose AppControl, gated on `network.telephony.sms`. |
| `ITextToSpeech` | `TizenTextToSpeech` | Partial | All | Speech works; every native client operation is serialized and dispatched to Tizen's Ecore/main loop. Cancellation/error retires the generation immediately, then force-posts native teardown so disposal cannot occur inside a callback. `GetLocalesAsync` throws (see API gaps below); use `GetSupportedVoiceLanguagesAsync` with `SpeakWithVoiceAsync`. `SpeechOptions.Pitch` / `Volume` are rejected. |
| `IVibration` | `TizenVibration` | Implemented | Mobile, Wearable | `Tizen.System.Vibrator`; requires the `haptic` privilege. |
| `IWebAuthenticator` | `TizenWebAuthenticator` | Unsupported | – | Tizen has no callback URI registration that returns an external browser response to the app. |

<!-- coverage-matrix:end -->

## What is verified, and what is not

| | Status |
| --- | --- |
| Sources type-check against the **API15 reference pack** the product targets | Verified, by `tests/Maui.Tizen.Essentials.RefPackCompile` |
| The declared public API surface matches `PublicAPI/slice/PublicAPI.Unshipped.txt` | Verified, by the PublicAPI analyzer in that same lane |
| Sources compile against loadable Tizen implementation assemblies | Verified, by `src/Maui.Tizen.Essentials.HostVerification` |
| DI registration, facade/`MainThread` ownership, permission privilege mapping, unsupported classification, ported translation logic | Verified, by `tests/Maui.Tizen.Essentials.Tests` (337 tests) |
| `src/Maui.Tizen.Essentials` builds for `net11.0-tizen11.0` | **Blocked.** Fails with `MAUITIZEN0001`: the Samsung workload manifest `samsung.net.sdk.tizen.manifest-11.0.100` is unpublished. Nobody can build this TFM anywhere yet. |
| Any behaviour that P/Invokes into Tizen (sensors, AppControl, key manager, NUI capture, TTS, geocoding, ...) | **Blocked.** Requires a Tizen device or emulator, which in turn requires the workload. |

The two verification lanes are complementary. `RefPackCompile` type-checks against
`Samsung.Tizen.Ref.API15`, the exact surface `net11.0-tizen11.0` will bind to, but reference
assemblies have no method bodies and can never be executed. `HostVerification` compiles against
the Tizen.NET implementation assemblies, which can be loaded, which is what lets the tests run.
Neither packs or publishes anything, so neither can be mistaken for a shippable neutral build.

That split earns its keep: the API15 lane is what caught the removal of `Tizen.Maps`. The
implementation-assembly lane alone (Tizen.NET, API13) still compiles the old geocoding code
happily, and would have shipped an implementation binding to an assembly the target platform no
longer has.

The blocked rows are gates, not gaps in this work, and they are reported rather than papered
over.

## Deliberately not provided

| Contract | Why |
| --- | --- |
| `IVersionTracking` | MAUI's own `VersionTrackingImplementation` is platform neutral; it is built from `IPreferences` and `IAppInfo`, both of which this package registers. Registering a Tizen-specific version would take facade ownership away from MAUI's lazy `VersionTracking.Default` wrapper for no benefit. |
| `MainThread` | Main-thread marshalling is bridged from the registered `IDispatcher` by MAUI for platform backends that are not in-box. The Tizen dispatcher supplied by the core Tizen backend is therefore the single source of truth, and this package deliberately ships no `MainThread` platform hook and never touches `EcoreMainloop`. |
| `IActivityStateManager` | Android only. |
| `IWindowStateManager` | Windows / Apple only; MAUI does not bridge it elsewhere. |

## Permission mapping

Every built-in `Permissions.*` type maps to exactly one of three explicit kinds. The distinction
matters: collapsing "Tizen does not gate this" and "Tizen cannot do this" into a single empty
privilege list makes both report `Granted`, telling an application to proceed with a capability the
platform will never provide.

| Kind | Meaning | Members |
| --- | --- | --- |
| `Requires` | Tizen gates the capability. Privacy privileges additionally need runtime consent via `PrivacyPrivilegeManager`. | Bluetooth, CalendarRead/Write, Camera, ContactsRead/Write, Flashlight, LaunchApp, LocationAlways/WhenInUse, Media, Microphone, NearbyWifiDevices, NetworkState, Phone, Photos, PhotosAddOnly, PostNotifications, Sensors, Sms, Speech, StorageRead/Write, Vibrate |
| `Ungated` | Tizen genuinely requires no privilege. An affirmative claim, not a missing mapping. | Battery |
| `Unsupported` | No Tizen equivalent; checking or requesting throws. | Maps, Reminders |

Privileges are taken from the privilege annotations in the TizenFX API13 XML documentation rather
than from memory, and the `isRuntime` flag is asserted against the documented privacy-privilege set
by `MarksEveryPrivacyPrivilegeAsRuntime`. `Camera` and `Microphone` were previously marked
non-runtime, which skipped the consent check entirely and reported `Granted` on a denied capability.

`Permissions.Maps` no longer declares `http://tizen.org/privilege/mapservice`: with `Tizen.Maps`
removed at API15 that privilege gates nothing, so requiring applications to declare it was asking
for a permission for a dead capability.

## Tizen API surface changes found while porting

| Tizen API | Status on API15 | Impact |
| --- | --- | --- |
| `Tizen.Maps` / `MapService` | **Removed** (deprecated API11, gone by API15) | Geocoding has no implementation. Reclassified `Unsupported`; see the matrix above. |
| `Tizen.Security.PrivacyPrivilegeManager` | Deprecated at API11, still present at API15 | Runtime privilege checks still work. Tizen ships no replacement, so this backend keeps using it behind a scoped `#pragma`, as dotnet/maui did. |
| `Tizen.NUI.Window.Instance` | Deprecated at API12 in favour of `Window.Default` | Screenshot capture uses `Window.Default`. |

## MAUI public API gaps found while porting

These are internal-only MAUI APIs that the in-box Tizen backend relied on and that a standalone
platform backend cannot use. Each one is worked around in this package.

| MAUI API | Accessibility | Impact | Workaround |
| --- | --- | --- | --- |
| `Microsoft.Maui.Media.Locale..ctor(string, string, string, string)` | `internal` (still closed as of `11.0.0-preview.7.26426.4`; pinned by the `MauiLocaleStillExposesNoPublicConstructor` tripwire test, which fails the moment it opens) | **Blocking.** `ITextToSpeech.GetLocalesAsync` must return `IEnumerable<Locale>`, and `SpeechOptions.Locale` must be given one, but no external assembly can construct a `Locale`. | `GetLocalesAsync` throws with an explicit reason; `TizenTextToSpeech.GetSupportedVoiceLanguagesAsync()` and `SpeakWithVoiceAsync(text, language, rate, ct)` expose the same capability. |
| `Microsoft.Maui.Storage.FileMimeTypes` | `internal` | MIME constants used by pickers and launchers. | `TizenFileMimeTypes`. |
| `Microsoft.Maui.Devices.Sensors.PlacemarkExtensions.GetEscapedAddress` | `internal` | Address formatting for `geo:0,0?q=` queries. | `TizenPlacemarkExtensions.GetEscapedAddress`. |
| `Microsoft.Maui.Devices.Sensors.AccelerometerQueue` | `internal` | Shake detection window. | `TizenAccelerometerQueue`. |
| `Microsoft.Maui.Devices.DeviceDisplay.BaseLogicalDpi` | `internal` | Density calculation. | `TizenDeviceDisplay.BaseLogicalDpi`. |
| `Microsoft.Maui.ApplicationModel.Permissions.BasePlatformPermission` and the nested `Permissions.Camera`, `Permissions.LocationWhenInUse`, ... types | `public`, but declared `partial` and only implemented per in-box platform | A standalone backend cannot add a platform half to a partial class in another assembly, so the built-in permission types resolve to the neutral implementation whose members throw. | `TizenPermissions` maps the built-in permission types to Tizen privileges itself, and `TizenBasePlatformPermission` is provided for user-defined permissions. |
| `Microsoft.Maui.ApplicationModel.Platform` (Tizen members: `CurrentPackage`, `MapServiceToken`) | `public`, but the Tizen members are compiled only into the Tizen build | The neutral assembly already declares the type, so the Tizen members cannot be added from outside. | `TizenPlatform.CurrentPackage`; the map token flows through `IPlatformGeocoding.MapServiceToken`. |
| `Microsoft.Maui.Media.IPlatformScreenshot` | `public`, but declares no members outside an in-box platform build | Cannot carry a Tizen-typed capture contract. | `TizenScreenshot` implements the neutral `IViewScreenshot`; Tizen-typed overloads live on `TizenScreenshotExtensions`. |
| `Microsoft.Maui.Utils.ParseVersion` | `internal` | Version parsing for `IAppInfo` / `IDeviceInfo`. | `TizenPlatform.ParseVersion`. |
