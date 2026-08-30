// Host-side aliases for the native Tizen types.
//
// This file is the test-project counterpart of src/Maui.Tizen.Core/TizenNativeAliases.cs. It points
// the same aliases at the inert stand-ins in this folder so the backend's mappers, DI registration
// and dispatching can be compiled and EXECUTED on a machine without the Samsung Tizen workload.
//
// These stubs live in the test project on purpose. The product project
// (src/Maui.Tizen.Core) is single-TFM net11.0-tizen11.0 and must never gain a neutral fallback -
// see the repository contract in Directory.Build.props.

global using TizenNativeView = Microsoft.Maui.Platforms.Tizen.TizenPlatformView;
global using TizenNativeWindow = Microsoft.Maui.Platforms.Tizen.TizenPlatformWindow;
global using TizenNativeApplication = Microsoft.Maui.Platforms.Tizen.TizenPlatformApplication;
