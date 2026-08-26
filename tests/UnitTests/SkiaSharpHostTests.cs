using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Covers the native SkiaSharp resolution that lets the build tasks rasterize images from inside
/// an MSBuild task assembly.
/// </summary>
/// <remarks>
/// The regression these guard against is platform specific and was invisible on macOS: SkiaSharp
/// declares plain <c>[DllImport("libSkiaSharp")]</c> entry points and ships no loader, so a task
/// assembly loaded via <c>UsingTask AssemblyFile=...</c> had no native probe path and the Linux CI
/// agent failed with "The type initializer for 'SkiaSharp.SKData' threw an exception".
/// </remarks>
public class SkiaSharpHostTests : TestBase
{
	[Fact]
	public void RegistersAResolverOnThisHost()
	{
		SkiaSharpHost.EnsureNativeLibraryResolved();

		Assert.True(
			SkiaSharpHost.IsRegistered,
			$"The native SkiaSharp resolver failed to register: {SkiaSharpHost.RegistrationError}");
		Assert.Null(SkiaSharpHost.RegistrationError);
	}

	[Fact]
	public void EnsureIsIdempotent()
	{
		SkiaSharpHost.EnsureNativeLibraryResolved();
		SkiaSharpHost.EnsureNativeLibraryResolved();

		Assert.True(SkiaSharpHost.IsRegistered);
	}

	/// <summary>
	/// The end-to-end assertion: a real call into native Skia succeeds after registration. This is
	/// exactly the operation (encode/decode) that failed on Linux.
	/// </summary>
	[Fact]
	public void CanPerformARealNativeSkiaOperation()
	{
		SkiaSharpHost.EnsureNativeLibraryResolved();

		var path = WritePng(Path.Combine(CreateTempDirectory(), "probe.png"), 8, 8);

		using var decoded = SkiaSharp.SKBitmap.Decode(path);

		Assert.NotNull(decoded);
		Assert.Equal(8, decoded!.Width);
	}

	[Fact]
	public void ProbesTheNativeFileNameForThisOperatingSystem()
	{
		var expected =
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libSkiaSharp.dll" :
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libSkiaSharp.dylib" :
			"libSkiaSharp.so";

		Assert.Equal(expected, SkiaSharpHost.NativeLibraryFileName);
	}

	/// <summary>
	/// All three layouts this repository can produce must be probed: the flat and architecture
	/// sub-folder layouts shipped in the NuGet package, and the runtimes/{rid}/native layout that a
	/// plain project build leaves on disk.
	/// </summary>
	[Fact]
	public void ProbesFlatArchitectureAndRuntimesLayouts()
	{
		var candidates = SkiaSharpHost.GetCandidatePaths().ToList();
		var fileName = SkiaSharpHost.NativeLibraryFileName;
		var root = Path.GetDirectoryName(typeof(SkiaSharpHost).Assembly.Location)!;

		Assert.Equal(Path.Combine(root, fileName), candidates[0]);

		Assert.Contains(candidates, c => SkiaSharpHost.ArchitectureFolders
			.Any(a => c == Path.Combine(root, a, fileName)));

		Assert.Contains(candidates, c => c.Replace('\\', '/').Contains("/runtimes/")
			&& c.Replace('\\', '/').EndsWith("/native/" + fileName, StringComparison.Ordinal));
	}

	[Fact]
	public void ProbesAMuslFallbackOnLinux()
	{
		var isLinux = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			&& !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		if (!isLinux)
			return;

		// Both libc flavours are probed rather than sniffed: loading the wrong one simply fails
		// and probing continues.
		Assert.Contains(SkiaSharpHost.ArchitectureFolders, a => a.StartsWith("musl-", StringComparison.Ordinal));
		Assert.Contains(SkiaSharpHost.RuntimeIdentifiers, r => r.StartsWith("linux-musl-", StringComparison.Ordinal));
	}

	/// <summary>
	/// The Visual Studio / MSBuild.exe path: those hosts run on .NET Framework, where there is no
	/// NativeLibrary resolver, so the library has to be preloaded through the OS loader instead.
	/// </summary>
	/// <remarks>
	/// Full-framework MSBuild cannot be executed from this test suite - it does not exist on
	/// macOS or Linux, and the tests themselves run on .NET. What is verified here is the part
	/// that is host independent: that the same candidate list resolves to a real binary and that
	/// the OS loader accepts it. The remaining gap is running MSBuild.exe itself, which needs a
	/// Windows agent.
	/// </remarks>
	[Fact]
	public void OperatingSystemLoaderCanLoadTheNativeBinary()
	{
		var loaded = SkiaSharpHost.TryLoadThroughOperatingSystemLoader();

		Assert.True(
			loaded is not null,
			"No native SkiaSharp binary could be loaded through the OS loader. Candidates: "
				+ string.Join(", ", SkiaSharpHost.GetCandidatePaths()));

		Assert.True(File.Exists(loaded!));
	}

	/// <summary>
	/// On Windows the package ships the natives only in architecture sub-folders, which are on no
	/// default search path. The probe order must therefore include them, or a Visual Studio build
	/// finds nothing.
	/// </summary>
	[Fact]
	public void ArchitectureSubFolderIsProbedForTheCurrentProcessArchitecture()
	{
		var expected = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			Architecture.Arm => "arm",
			_ => null,
		};

		if (expected is null)
			return;

		Assert.Contains(expected, SkiaSharpHost.ArchitectureFolders);
	}

	/// <summary>
	/// The build output is what the sample and the MSBuild integration tests load the tasks from,
	/// so the host binary has to be sitting next to the managed assembly there.
	/// </summary>
	[Fact]
	public void BuildOutputCarriesTheHostNativeBinary()
	{
		var taskDirectory = Path.GetDirectoryName(BuildTasksAssemblyPath)!;

		var flat = Path.Combine(taskDirectory, SkiaSharpHost.NativeLibraryFileName);
		var underRuntimes = Directory.Exists(Path.Combine(taskDirectory, "runtimes"))
			&& Directory.GetFiles(Path.Combine(taskDirectory, "runtimes"), SkiaSharpHost.NativeLibraryFileName, SearchOption.AllDirectories).Length > 0;

		Assert.True(
			File.Exists(flat) || underRuntimes,
			$"No native SkiaSharp binary was found for the build tasks in '{taskDirectory}'.");
	}
}
