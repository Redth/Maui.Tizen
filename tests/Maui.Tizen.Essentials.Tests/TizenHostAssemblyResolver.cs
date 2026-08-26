using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Resolves the Tizen.NET assemblies that the build copies next to the test binaries.
/// </summary>
/// <remarks>
/// Tizen.NET publishes its assemblies as NuGet <c>ref/</c> assets, so they never appear in the
/// generated <c>.deps.json</c> and the default host probing logic will not find them even when the
/// files are present. They are real implementation assemblies, so loading them by path is enough to
/// let the host-side tests read Tizen metadata and run managed-only code paths. Calls that actually
/// P/Invoke into Tizen still require a device or emulator.
/// </remarks>
internal static class TizenHostAssemblyResolver
{
	[ModuleInitializer]
	internal static void Initialize() =>
		AssemblyLoadContext.Default.Resolving += static (context, name) =>
		{
			if (name.Name is not { } simpleName)
				return null;

			if (!simpleName.StartsWith("Tizen", StringComparison.Ordinal) &&
				!simpleName.StartsWith("ElmSharp", StringComparison.Ordinal))
			{
				return null;
			}

			var path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");

			return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
		};
}
