// Microsoft.Maui.Controls declares its own LayoutAlignment alongside
// Microsoft.Maui.Primitives.LayoutAlignment, and this lane now compiles the Controls bridge
// sources - so every stub implementing IView saw an ambiguous reference (CS0104).
//
// Aliased globally rather than per-file: the ambiguity is a property of this assembly's reference
// set, not of any one test, and IView is a Core interface so the Primitives type is always the
// correct one here.
global using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;

// Maui.Tizen.Controls introduces a sibling namespace that would otherwise shadow the Controls
// shorthand throughout this test assembly.
global using MauiControls = Microsoft.Maui.Controls;
