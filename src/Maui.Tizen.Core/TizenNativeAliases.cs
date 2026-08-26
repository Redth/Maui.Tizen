// Single definition point for the native Tizen types this backend builds on.
//
// Compiled only into the real net11.0-tizen11.0 product assembly. The host-side verification lanes
// under tests/ supply their own alias file pointing at stand-ins, which is why every NUI call site
// in the shared sources is wrapped in `#if TIZEN`.

global using TizenNativeView = Tizen.NUI.BaseComponents.View;
global using TizenNativeWindow = Tizen.NUI.Window;
global using TizenNativeApplication = Tizen.Applications.CoreApplication;
