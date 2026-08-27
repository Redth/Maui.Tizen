#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Makes the native SkiaSharp library loadable from inside an MSBuild task assembly.
	/// </summary>
	/// <remarks>
	/// SkiaSharp declares its entry points as a plain <c>[DllImport("libSkiaSharp")]</c> and, as of
	/// 3.x, ships no loader of its own. Resolution therefore falls back to the runtime's default
	/// probing, which is driven by the *host application's* dependency context - here that host is
	/// MSBuild, which knows nothing about SkiaSharp. A task assembly loaded through
	/// <c>UsingTask AssemblyFile=...</c> gets no native probe path of its own, so the first call
	/// into Skia fails with:
	///
	///   error : The type initializer for 'SkiaSharp.SKData' threw an exception.
	///
	/// The failure is platform dependent purely by accident of which library the host process
	/// happens to already have loaded or have on its search path, which is why this reproduced on
	/// the Linux CI agent while passing on macOS.
	///
	/// This class removes the accident: it registers an explicit resolver for the SkiaSharp
	/// assembly that probes the layouts this repository actually produces.
	///
	///   1. Flat, next to the managed assembly            - the NuGet package layout
	///   2. An architecture sub-folder (x64, arm64, ...)  - the NuGet package layout, multi-arch
	///   3. runtimes/{rid}/native/                        - a normal project build output
	///
	/// The first two mirror how Microsoft.Maui.Resizetizer lays out its own host task package; the
	/// third is what a plain <c>dotnet build</c> of this repository leaves on disk, which is what
	/// the sample and the MSBuild integration tests consume.
	///
	/// IMPORTANT: this is strictly host/build-time concern. None of these binaries are inputs to
	/// the Tizen application being built - they are only used by the build task process itself to
	/// rasterize images. See the packaging tests for the assertions that keep them out of app
	/// output.
	///
	/// Targeting netstandard2.0 (required so the tasks load in every MSBuild host) means
	/// <c>NativeLibrary</c> cannot be referenced directly, so it is bound reflectively. On a host
	/// too old to provide it the registration is skipped and behaviour is unchanged.
	/// </remarks>
	internal static class SkiaSharpHost
	{
		static readonly object Gate = new object();
		static bool _initialized;

		/// <summary>The last error encountered while registering, for diagnostics. Null on success.</summary>
		internal static string? RegistrationError { get; private set; }

		/// <summary>True when a resolver was successfully registered.</summary>
		internal static bool IsRegistered { get; private set; }

		/// <summary>
		/// Registers the native resolver exactly once. Safe to call from every task entry point.
		/// </summary>
		public static void EnsureNativeLibraryResolved()
		{
			if (_initialized)
				return;

			lock (Gate)
			{
				if (_initialized)
					return;

				_initialized = true;

				try
				{
					Register();
				}
				catch (Exception ex)
				{
					// Never fail the build from here: if registration does not work, the default
					// runtime probing still gets its chance and produces its own error.
					RegistrationError = ex.Message;
				}
			}
		}

		static void Register()
		{
			var nativeLibraryType = Type.GetType("System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices", throwOnError: false)
				?? Type.GetType("System.Runtime.InteropServices.NativeLibrary, System.Private.CoreLib", throwOnError: false);

			if (nativeLibraryType is null)
			{
				// .NET Framework MSBuild (MSBuild.exe, and therefore Visual Studio). There is no
				// NativeLibrary and no resolver hook, so the library is preloaded instead: once a
				// module is in the process, the loader satisfies the later DllImport("libSkiaSharp")
				// by name without searching again.
				PreloadForDesktopFramework();
				return;
			}

			var setResolver = nativeLibraryType.GetMethod(
				"SetDllImportResolver",
				BindingFlags.Public | BindingFlags.Static);

			var load = nativeLibraryType.GetMethod(
				"TryLoad",
				BindingFlags.Public | BindingFlags.Static,
				binder: null,
				types: new[] { typeof(string), typeof(IntPtr).MakeByRefType() },
				modifiers: null);

			if (setResolver is null || load is null)
			{
				RegistrationError = "NativeLibrary does not expose the expected SetDllImportResolver/TryLoad members.";
				return;
			}

			// The resolver delegate type is NativeLibrary's nested DllImportResolver.
			var resolverDelegateType = setResolver.GetParameters()[1].ParameterType;

			_tryLoad = load;

			var resolver = Delegate.CreateDelegate(
				resolverDelegateType,
				typeof(SkiaSharpHost).GetMethod(nameof(Resolve), BindingFlags.NonPublic | BindingFlags.Static)!);

			var skiaSharpAssembly = typeof(SkiaSharp.SKBitmap).Assembly;

			setResolver.Invoke(null, new object[] { skiaSharpAssembly, resolver });

			IsRegistered = true;
			RegistrationError = null;
		}

		static MethodInfo? _tryLoad;

		/// <summary>
		/// Loads the native library eagerly using the OS loader, for hosts with no
		/// <c>NativeLibrary</c> support.
		/// </summary>
		/// <remarks>
		/// This is the Visual Studio / MSBuild.exe path. Those hosts run on .NET Framework, where
		/// the resolver registered above does not exist, and the default probing looks beside the
		/// HOST executable (devenv.exe, MSBuild.exe) rather than beside this task assembly. The
		/// package deliberately ships the Windows binaries in architecture sub-folders, none of
		/// which is on any default search path, so without this a splash screen build inside
		/// Visual Studio fails while the same project builds from the command line.
		/// </remarks>
		static void PreloadForDesktopFramework()
		{
			var preloaded = TryPreload();

			if (preloaded is null)
			{
				RegistrationError = "No native SkiaSharp binary could be preloaded for this host.";
				return;
			}

			IsRegistered = true;
			RegistrationError = null;
			PreloadedPath = preloaded;
		}

		/// <summary>
		/// Walks the candidate list and loads the first binary the OS loader accepts, returning the
		/// path that succeeded, or <c>null</c> if none did.
		/// </summary>
		/// <remarks>
		/// Separated from <see cref="PreloadForDesktopFramework"/>, which only applies the result to
		/// shared state, so that the selection logic can be exercised by tests. It would otherwise
		/// be unreachable anywhere it can be run: the desktop path is taken only when
		/// <c>NativeLibrary</c> is absent, which on a .NET host it never is.
		/// </remarks>
		/// <summary>Attempts to load one specific path, for tests. Returns null when it cannot be loaded.</summary>
		internal static string? TryLoadForTesting(string path)
			=> LoadNativeLibrary(path) != IntPtr.Zero ? path : null;

		internal static string? TryPreload()
		{
			foreach (var candidate in GetCandidatePaths())
			{
				if (!File.Exists(candidate))
					continue;

				if (LoadNativeLibrary(candidate) != IntPtr.Zero)
					return candidate;
			}

			return null;
		}

		/// <summary>The path preloaded on hosts without NativeLibrary, for diagnostics and tests.</summary>
		internal static string? PreloadedPath { get; private set; }

		static IntPtr LoadNativeLibrary(string path)
		{
			try
			{
				return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
					? LoadLibraryW(path)
					: dlopen(path, RTLD_NOW | RTLD_GLOBAL);
			}
			catch (DllNotFoundException)
			{
				return IntPtr.Zero;
			}
			catch (EntryPointNotFoundException)
			{
				return IntPtr.Zero;
			}
		}

		const int RTLD_NOW = 2;
		const int RTLD_GLOBAL = 8;

		[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
		static extern IntPtr LoadLibraryW(string lpFileName);

		// libc rather than libdl: glibc 2.34+ merged dlopen into libc, and on macOS it lives in
		// libSystem, which "libc" also resolves to.
		[DllImport("libc", EntryPoint = "dlopen")]
		static extern IntPtr dlopen(string fileName, int flags);



		/// <summary>
		/// Signature-compatible with <c>NativeLibrary.DllImportResolver</c>. Returns
		/// <see cref="IntPtr.Zero"/> to fall back to the runtime's default behaviour.
		/// </summary>
		static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!string.Equals(libraryName, "libSkiaSharp", StringComparison.OrdinalIgnoreCase))
				return IntPtr.Zero;

			foreach (var candidate in GetCandidatePaths())
			{
				if (!File.Exists(candidate))
					continue;

				var args = new object?[] { candidate, IntPtr.Zero };
				if (_tryLoad is not null && _tryLoad.Invoke(null, args) is true)
					return (IntPtr)args[1]!;
			}

			return IntPtr.Zero;
		}

		/// <summary>
		/// The candidate native library paths, in probe order.
		/// </summary>
		internal static IEnumerable<string> GetCandidatePaths()
		{
			var root = Path.GetDirectoryName(typeof(SkiaSharpHost).Assembly.Location);
			if (string.IsNullOrEmpty(root))
				yield break;

			var fileName = NativeLibraryFileName;

			// 1. Flat, beside the managed assembly (NuGet package layout).
			yield return Path.Combine(root!, fileName);

			// 2. Architecture sub-folder (NuGet package layout for multi-architecture platforms).
			foreach (var architecture in ArchitectureFolders)
				yield return Path.Combine(root!, architecture, fileName);

			// 3. runtimes/{rid}/native (ordinary project build output).
			foreach (var rid in RuntimeIdentifiers)
				yield return Path.Combine(root!, "runtimes", rid, "native", fileName);
		}

		internal static string NativeLibraryFileName
		{
			get
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					return "libSkiaSharp.dll";

				if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					return "libSkiaSharp.dylib";

				return "libSkiaSharp.so";
			}
		}

		/// <summary>
		/// Architecture sub-folder names, most specific first. Both the glibc and musl flavours are
		/// probed on Linux rather than sniffing the C library: loading the wrong one simply fails
		/// and probing continues, which is cheaper and more reliable than detecting the libc.
		/// </summary>
		internal static IEnumerable<string> ArchitectureFolders
		{
			get
			{
				var isLinux = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
					&& !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

				switch (RuntimeInformation.ProcessArchitecture)
				{
					case Architecture.X64:
						yield return "x64";
						if (isLinux)
							yield return "musl-x64";
						break;

					case Architecture.X86:
						yield return "x86";
						break;

					case Architecture.Arm64:
						yield return "arm64";
						if (isLinux)
							yield return "musl-arm64";
						break;

					case Architecture.Arm:
						yield return "arm";
						break;
				}
			}
		}

		/// <summary>
		/// Runtime identifiers to probe under <c>runtimes/</c>, most specific first.
		/// </summary>
		internal static IEnumerable<string> RuntimeIdentifiers
		{
			get
			{
				var architecture = RuntimeInformation.ProcessArchitecture switch
				{
					Architecture.X64 => "x64",
					Architecture.X86 => "x86",
					Architecture.Arm64 => "arm64",
					Architecture.Arm => "arm",
					_ => null,
				};

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					if (architecture is not null)
						yield return "win-" + architecture;
					yield break;
				}

				if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					if (architecture is not null)
						yield return "osx-" + architecture;

					// SkiaSharp ships a single universal binary under the RID-less 'osx' folder.
					yield return "osx";
					yield break;
				}

				if (architecture is not null)
				{
					yield return "linux-" + architecture;
					yield return "linux-musl-" + architecture;
				}
			}
		}
	}
}
