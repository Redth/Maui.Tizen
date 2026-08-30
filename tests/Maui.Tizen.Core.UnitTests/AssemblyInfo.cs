// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

// Building a MAUI Controls app is NOT thread-safe with respect to other app builds, so the tests in
// this assembly must not run in parallel.
//
// MauiApp.CreateBuilder().UseMauiApp<T>() reaches SetupDefaults -> RemapForControls, which mutates
// MAUI's *process-global* static handler mappers in place. PropertyMapper stores its mappings in a
// plain Dictionary, so two threads doing that at once corrupt it:
//
//   System.InvalidOperationException: Operations that change non-concurrent collections must have
//   exclusive access. A concurrent update was performed on this collection and corrupted its state.
//       at Microsoft.Maui.PropertyMapper.GetProperty(String key)
//       at Microsoft.Maui.PropertyMapperExtensions.ReplaceMapping[...]
//       at Microsoft.Maui.Controls.TabbedPage.RemapForControls()
//       at Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions.UseMauiApp[TApp](MauiAppBuilder)
//
// Once that corruption happens, a handler's static initializer can also fail while chaining the
// damaged mapper, and .NET caches a failed type initialization permanently - so a single collision
// resurfaces as TypeInitializationException on every later access and takes out dozens of unrelated
// tests. Measured at roughly a one-in-five failure rate across full runs, which is exactly the shape
// that reaches CI intermittently and gets dismissed as flakiness.
//
// Seven test classes in this assembly build apps, so the collision window is wide. Serialising the
// assembly is the fix that actually addresses the cause: the shared state is MAUI's global statics,
// not anything this repository owns, and no per-class collection grouping helps unless every
// app-building class - present and future - remembers to join it.
//
// The cost is negligible: the suite executes in about three seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
