# DevFlow agent

The Tizen backend for [DevFlow](https://github.com/dotnet/maui-labs), the in-app agent that lets an
external driver inspect and drive a running MAUI application.

## What is consumed, not written

DevFlow is taken from published maui-labs packages. Nothing is vendored or reimplemented:

| Package | Version | Role |
|---|---|---|
| `Microsoft.Maui.DevFlow.Agent.Core` | `0.1.0-preview.12.26421.1` | MAUI visual tree walker, HTTP surface, element registry |
| `Microsoft.Maui.DevFlow.Agent.Abstractions` | (transitive) | Framework-neutral server, routing, DTOs, CSS querying |

There is no `Microsoft.Maui.DevFlow.Agent.Tizen` upstream, and `Microsoft.Maui.DevFlow.Agent` ships
only Android, iOS, Mac Catalyst, macOS and Windows heads. The Tizen backend is ours to write.

## Layout

```
src/Diagnostics/
  Maui.Tizen.DevFlow.Agent.Shared/     net11.0        - builds and is tested today
    TizenPlatformIdentity.cs
    TizenAgentCapabilityPolicy.cs      capability + privilege decisions
    TizenAgentConnection.cs            sdb tunnel descriptor
    NativeElementDiagnosticsBridge.cs  registry for platform-owned chrome

  Maui.Tizen.DevFlow.Agent/            net11.0-tizen11.0 - CANNOT BE BUILT (see blockers)
    TizenAgentService.cs               : MauiDevFlowAgentService
    TizenVisualTreeWalker.cs           : VisualTreeWalker
    TizenAgentServiceExtensions.cs     AddMauiDevFlowAgent()
    TizenScreenshotCapture.cs          Tizen.NUI.Capture
    TizenNativeInput.cs                privilege-gated synthesised input
    TizenDeviceEnvironment.cs          device probing
```

The split is the whole point. Everything that does not need Tizen types was moved into `Shared` so
it can actually be executed and asserted on a hosted runner. What is left in the Tizen project is
only the code that genuinely touches NUI.

## The uncompilable-code problem

`Maui.Tizen.DevFlow.Agent` targets a framework nobody can build. Code like that rots silently
against an upstream preview package, so two things constrain it:

**`DevFlowContractTests`** pins every DevFlow member the Tizen agent overrides. DevFlow's packages
are plain `net10.0` assemblies, so the hosted lane loads them and asserts each signature still
exists and is still virtual. If maui-labs renames or reshapes one, an ordinary pull request fails.

Every signature used was verified by compiling a probe subclass against the real package, not
inferred from documentation.

**`Shared` carries the logic.** Capability policy, privilege gating, platform identity, the native
element registry and the connection descriptors are all unit-tested — 42 tests.

What remains genuinely unverified: NUI property access, `Tizen.NUI.Capture` usage, and privilege
queries. That is stated at the top of the project file rather than left to be discovered.

## Registration

Mirrors every shipped backend, so `MauiProgram.cs` is identical across platforms:

```csharp
#if DEBUG
    builder.AddMauiDevFlowAgent();
#endif
```

Internally this uses `DevFlowAgentHost.Configure(...)` followed by
`DevFlowAgentHostContext.AttachTo(...)`, which is the current path used by the Android, GTK and WPF
backends. It deliberately does **not** re-implement broker registration: `DevFlowAgentService`
already owns that, and duplicating it produces two registrations racing to bind the port.

## Element mapping

The base walker already covers the MAUI visual tree. The Tizen walker supplies only the *native*
layer, mapped as:

| DevFlow | NUI |
|---|---|
| `IsVisible` | `View.Visibility` |
| `IsEnabled` | `View.Sensitive` |
| `IsFocused` | `View.KeyInputFocus` |
| `Bounds` | `View.ScreenPosition` + `View.CurrentSize` |
| `AutomationId` | `View.Name` |

`View.Name` is used for identity because it is the only thing NUI carries that survives a layout
pass; object hash codes do not, which is why stable ids come from the bridge instead.

Bounds are read live rather than from registration time, because chrome moves — a toolbar slides, a
dialog centres — after it is registered.

## `NativeElementDiagnosticsBridge`

Shell chrome, toolbar items and native dialogs are NUI views owned by the platform. They never
appear in the MAUI visual tree, so without an explicit registry a driver simply cannot see or tap
them.

DevFlow exposes the *consumption* side of this (`WalkNativeTree`, `QueryNative`,
`GetNativeElementById`, `HitTestNativeElements`, `TryNativeElement*`) but no public registration API
— that bookkeeping is internal to the framework backends. The bridge is the Tizen-side
implementation of it.

`Generation` maps onto DevFlow's `registryGeneration`: requests carry the generation they were
computed against, so a driver acting on a stale snapshot is rejected instead of tapping whatever now
occupies that id.

Hit-testing orders by ascending area to approximate "topmost", because NUI does not expose a uniform
z-order for platform-owned chrome. Bounds are half-open so adjacent elements cannot both claim a
shared edge.

## Two kinds of interaction

Not interchangeable, and the difference is advertised so a driver knows which guarantees it has:

**Synthesised input** posts real touch events through the window. It exercises the full input stack,
so it is the only way to validate gesture recognisers and hit-testing. It requires
`http://tizen.org/privilege/inputgenerator`.

**Direct invocation** calls the widget's own API. Always available, but bypasses hit-testing — a
control hidden behind an overlay still "taps" successfully. It is the fallback, never the default.

Native input is advertised as supported **only** when the privilege is actually granted. Declaring a
privilege in `tizen-manifest.xml` is not the same as holding it, since privacy privileges can be
denied at runtime. Advertising a capability that silently no-ops is worse than reporting it
unsupported, because the failure appears as an inexplicable test result rather than a clear 501.

Losing the privilege does **not** disable framework-level tap and fill; a device without it is less
capable, not broken.

## Fill

Framework fill goes through the MAUI element, keeping bindings and validation running. Native fill
handles only NUI `TextField` and `TextEditor`. Writing straight to the platform widget for a MAUI
control would change the visible text without ever notifying the view model — the app would look
correct and behave wrongly.

## Screenshots

`Tizen.NUI.Capture` is file-based and asynchronous: it writes a PNG and raises `Finished`. There is
no in-memory overload, so the flow is capture to a temp file, read, delete.

The file goes in the application's own data directory, not `/tmp`: a sandboxed Tizen application is
not guaranteed write access elsewhere, and a capture that silently fails to write is
indistinguishable from one that produced nothing. A 10-second timeout guards a wedged GL pipeline,
which would otherwise never raise `Finished` and would hang the HTTP request forever.

## The `platform` field

DevFlow's `agent-status.json` schema constrains `platform` to
`ios | android | maccatalyst | windows | linux | macos`. `tizen` is not a member, so an accurate
value is out of spec until upstream adds it.

Both behaviours are implemented and explicit rather than silently hard-coded:

| Mode | Reports | Notes |
|---|---|---|
| `Accurate` (default) | `tizen` | Correct; strict schema validators will reject it |
| `SchemaCompatible` | `linux` + `x-platform: tizen` | Works with stock validating clients |

`DevFlowContractTests.DevFlowSpecPlatformEnum_StillLacksTizen` fails the moment maui-labs adds
`tizen`, at which point `SchemaCompatible` can be deleted. **Follow-up:** file an issue at
`dotnet/maui-labs`.

## Connecting

Fixed port `9223` plus `sdb forward`. See [device lane](device-lane.md#reaching-the-agent).
