using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Asserts the shipped shape of the two packable projects.
/// </summary>
/// <remarks>
/// Package layout is easy to break silently: a task assembly that lands in lib/ instead of
/// buildTransitive/, or a missing native SkiaSharp binary, produces a package that restores
/// cleanly and then fails at build time on someone else's machine. These tests pack for real
/// and read the resulting archives.
/// </remarks>
[Trait("Category", "Packaging")]
public class PackageContentTests : TestBase
{
	private static readonly Lazy<string> PackageDirectory = new(PackOnce);

	private static string PackOnce()
	{
		var output = Path.Combine(RepositoryRoot, "artifacts", "packages", "test");

		foreach (var project in new[]
		{
			Path.Combine("src", "Maui.Tizen.Build.Tasks", "Maui.Tizen.Build.Tasks.csproj"),
			Path.Combine("src", "Maui.Tizen.Templates", "Maui.Tizen.Templates.csproj"),
		})
		{
			var startInfo = new ProcessStartInfo("dotnet")
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};

			startInfo.ArgumentList.Add("pack");
			startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, project));
			startInfo.ArgumentList.Add("-p:PackageOutputPath=" + output);
			startInfo.ArgumentList.Add("--nologo");
			startInfo.ArgumentList.Add("-v:q");
			startInfo.ArgumentList.Add("-nr:false");

			using var process = Process.Start(startInfo)!;
			var log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode != 0)
				throw new InvalidOperationException($"dotnet pack failed for '{project}':{Environment.NewLine}{log}");
		}

		return output;
	}

	private static IReadOnlyList<string> EntriesOf(string packageId)
	{
		var path = Directory.GetFiles(PackageDirectory.Value, packageId + ".*.nupkg")
			.OrderBy(p => p, StringComparer.Ordinal)
			.LastOrDefault();

		Assert.True(path is not null, $"No package was produced for '{packageId}'.");

		using var archive = ZipFile.OpenRead(path!);
		return archive.Entries
			.Select(e => e.FullName.Replace('\\', '/'))
			.Where(n => !n.StartsWith("_rels/", StringComparison.Ordinal)
				&& !n.StartsWith("package/", StringComparison.Ordinal)
				&& n != "[Content_Types].xml")
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
	}

	[Fact]
	public void BuildTasksPackageShipsItsMSBuildEntryPoints()
	{
		var entries = EntriesOf("Maui.Tizen.Build.Tasks");

		Assert.Contains("buildTransitive/Maui.Tizen.Build.Tasks.props", entries);
		Assert.Contains("buildTransitive/Maui.Tizen.Build.Tasks.targets", entries);
		Assert.Contains("buildTransitive/Maui.Tizen.Build.Tasks.dll", entries);
	}

	/// <summary>
	/// A tasks package must not ship a lib/ folder: consumers would pick the task assembly up as
	/// a compile time reference.
	/// </summary>
	[Fact]
	public void BuildTasksPackageHasNoLibFolder()
	{
		Assert.DoesNotContain(EntriesOf("Maui.Tizen.Build.Tasks"), e => e.StartsWith("lib/", StringComparison.Ordinal));
	}

	/// <summary>
	/// SkiaSharp resolves its native library by probing beside the managed assembly and in
	/// architecture sub-folders. The layout mirrors Microsoft.Maui.Resizetizer's own package.
	/// </summary>
	[Theory]
	[InlineData("buildTransitive/SkiaSharp.dll")]
	[InlineData("buildTransitive/libSkiaSharp.dylib")]
	[InlineData("buildTransitive/x64/libSkiaSharp.dll")]
	[InlineData("buildTransitive/x86/libSkiaSharp.dll")]
	[InlineData("buildTransitive/arm64/libSkiaSharp.dll")]
	[InlineData("buildTransitive/x64/libSkiaSharp.so")]
	[InlineData("buildTransitive/arm64/libSkiaSharp.so")]
	[InlineData("buildTransitive/musl-x64/libSkiaSharp.so")]
	public void BuildTasksPackageShipsNativeSkiaSharpFor(string entry)
	{
		Assert.Contains(entry, EntriesOf("Maui.Tizen.Build.Tasks"));
	}

	/// <summary>
	/// Every operating system the tasks can run on must have a loadable binary in the package, in
	/// a location <see cref="SkiaSharpHost"/> actually probes. A gap here is invisible until a
	/// build runs on that OS and fails inside SkiaSharp's static initializer.
	/// </summary>
	[Theory]
	[InlineData("libSkiaSharp.dylib", "macOS")]
	[InlineData("libSkiaSharp.so", "Linux")]
	[InlineData("libSkiaSharp.dll", "Windows")]
	public void BuildTasksPackageCanResolveNativeSkiaSharpOn(string fileName, string operatingSystem)
	{
		var entries = EntriesOf("Maui.Tizen.Build.Tasks");

		var probed = entries.Where(e =>
		{
			var name = e.Substring(e.LastIndexOf('/') + 1);
			if (name != fileName)
				return false;

			var relative = e.Substring("buildTransitive/".Length);

			// Flat beside the managed assembly, or exactly one architecture folder deep - the two
			// layouts SkiaSharpHost probes inside a package.
			return relative.Count(c => c == '/') <= 1;
		}).ToList();

		Assert.True(
			probed.Count > 0,
			$"The package ships no native SkiaSharp for {operatingSystem} in a probed location. Entries: {string.Join(", ", entries)}");
	}

	/// <summary>
	/// Architecture coverage, asserted separately from presence so a package that silently drops
	/// (say) arm64 Linux is caught rather than passing on the x64 entry alone.
	/// </summary>
	[Theory]
	[InlineData("x64/libSkiaSharp.so")]
	[InlineData("arm64/libSkiaSharp.so")]
	[InlineData("musl-x64/libSkiaSharp.so")]
	[InlineData("arm/libSkiaSharp.so")]
	[InlineData("x64/libSkiaSharp.dll")]
	[InlineData("x86/libSkiaSharp.dll")]
	[InlineData("arm64/libSkiaSharp.dll")]
	public void BuildTasksPackageShipsArchitectureSpecificNative(string relativePath)
	{
		Assert.Contains("buildTransitive/" + relativePath, EntriesOf("Maui.Tizen.Build.Tasks"));
	}

	/// <summary>
	/// The macOS binary is a universal build, so it ships once, flat.
	/// </summary>
	[Fact]
	public void BuildTasksPackageShipsTheMacOSNativeFlat()
	{
		Assert.Contains("buildTransitive/libSkiaSharp.dylib", EntriesOf("Maui.Tizen.Build.Tasks"));
	}

	/// <summary>
	/// The managed task assembly and the natives must sit in the same folder, because that folder
	/// is what <c>UsingTask AssemblyFile</c> resolves against and what the resolver probes from.
	/// </summary>
	[Fact]
	public void BuildTasksPackagePlacesManagedTaskAlongsideItsNativeAssets()
	{
		var entries = EntriesOf("Maui.Tizen.Build.Tasks");

		Assert.Contains("buildTransitive/Maui.Tizen.Build.Tasks.dll", entries);
		Assert.Contains("buildTransitive/SkiaSharp.dll", entries);
		Assert.Contains("buildTransitive/libSkiaSharp.dylib", entries);
	}

	/// <summary>
	/// These are host build-time binaries. They must never be advertised as package content that a
	/// Tizen application would carry, which is what suppressed dependencies plus the
	/// developmentDependency flag guarantee.
	/// </summary>
	[Fact]
	public void BuildTasksPackageDoesNotShipNativeAssetsAsApplicationContent()
	{
		var entries = EntriesOf("Maui.Tizen.Build.Tasks");

		Assert.DoesNotContain(entries, e => e.StartsWith("runtimes/", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, e => e.StartsWith("contentFiles/", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, e => e.StartsWith("content/", StringComparison.Ordinal));

		// Every shipped file lives under buildTransitive, i.e. is MSBuild-only.
		Assert.All(
			entries.Where(e => !e.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && e != "README.md"),
			e => Assert.StartsWith("buildTransitive/", e));
	}

	/// <summary>
	/// The shipped Linux binaries must not require libfontconfig.
	/// </summary>
	/// <remarks>
	/// This is the regression that broke CI. The default SkiaSharp.NativeAssets.Linux build links
	/// against libfontconfig.so.1, which is absent from the dotnet/sdk container images and is not
	/// guaranteed on a build agent. When it is missing the failure surfaces as a bare
	/// "The type initializer for 'SkiaSharp.SKData' threw an exception" from whichever MSBuild
	/// target happened to rasterize first - no mention of fontconfig, no mention of Skia's native
	/// library. Reading the ELF dependencies directly means a future package bump that quietly
	/// reintroduces the dependency fails here instead, on any developer machine.
	/// </remarks>
	[Fact]
	public void ShippedLinuxNativesDoNotRequireFontconfig()
	{
		var natives = ExtractedPackageFiles("Maui.Tizen.Build.Tasks")
			.Where(f => f.Name.EndsWith(".so", StringComparison.Ordinal))
			.ToList();

		Assert.NotEmpty(natives);

		foreach (var native in natives)
		{
			Assert.True(ElfReader.IsElf(native.Path), $"'{native.Name}' is not a valid ELF binary.");

			var needed = ElfReader.GetNeededLibraries(native.Path);

			Assert.DoesNotContain(
				needed,
				n => n.StartsWith("libfontconfig", StringComparison.OrdinalIgnoreCase));

			// Sanity check that the dependency list was really parsed rather than coming back
			// empty, which would make the assertion above vacuous.
			Assert.Contains(needed, n => n.StartsWith("libc", StringComparison.OrdinalIgnoreCase)
				|| n.StartsWith("ld-", StringComparison.OrdinalIgnoreCase));
		}
	}

	[Fact]
	public void BuildTasksPackageIsMarkedAsADevelopmentDependency()
	{
		var nuspec = ReadNuspec("Maui.Tizen.Build.Tasks");
		Assert.Contains("<developmentDependency>true</developmentDependency>", nuspec);
	}

	[Fact]
	public void TemplatePackageShipsTheTizenTemplate()
	{
		var entries = EntriesOf("Maui.Tizen.Templates");

		Assert.Contains("content/templates/maui-tizen/.template.config/template.json", entries);
		Assert.Contains("content/templates/maui-tizen/MauiTizenApp.csproj", entries);
		Assert.Contains("content/templates/maui-tizen/MauiProgram.cs", entries);
		Assert.Contains("content/templates/maui-tizen/Platforms/Tizen/Main.cs", entries);
		Assert.Contains("content/templates/maui-tizen/Platforms/Tizen/tizen-manifest.xml", entries);
	}

	[Fact]
	public void TemplatePackageIsMarkedAsATemplatePackage()
	{
		Assert.Contains("<packageTypes>", ReadNuspec("Maui.Tizen.Templates"));
	}

	[Fact]
	public void PackagingIsDeterministic()
	{
		var first = EntriesOf("Maui.Tizen.Build.Tasks");
		var second = EntriesOf("Maui.Tizen.Build.Tasks");

		Assert.Equal(first, second);
	}

	/// <summary>
	/// Extracts the package once and returns its files on disk, for assertions that need to read
	/// binary content rather than just entry names.
	/// </summary>
	private static IReadOnlyList<(string Name, string Path)> ExtractedPackageFiles(string packageId)
	{
		var source = Directory.GetFiles(PackageDirectory.Value, packageId + ".*.nupkg")
			.OrderBy(p => p, StringComparer.Ordinal)
			.Last();

		var destination = Path.Combine(PackageDirectory.Value, "extracted", packageId);

		if (!Directory.Exists(destination))
		{
			Directory.CreateDirectory(destination);
			ZipFile.ExtractToDirectory(source, destination, overwriteFiles: true);
		}

		return Directory
			.GetFiles(destination, "*", SearchOption.AllDirectories)
			.Select(p => (Name: Path.GetFileName(p), Path: p))
			.ToList();
	}

	private static string ReadNuspec(string packageId)
	{
		var path = Directory.GetFiles(PackageDirectory.Value, packageId + ".*.nupkg")
			.OrderBy(p => p, StringComparer.Ordinal)
			.Last();

		using var archive = ZipFile.OpenRead(path);
		var entry = archive.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		using var reader = new StreamReader(entry.Open());
		return reader.ReadToEnd();
	}
}

/// <summary>
/// Keeps the emitted template aligned with the repository's frozen target framework contract.
/// </summary>
public class TemplateContentTests : TestBase
{
	private static string TemplateDirectory =>
		Path.Combine(RepositoryRoot, "src", "Maui.Tizen.Templates", "templates", "maui-tizen");

	private static string ReadRepositoryProperty(string name)
	{
		var props = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));
		var match = Regex.Match(props, $"<{name}>([^<]+)</{name}>");
		Assert.True(match.Success, $"Directory.Build.props does not declare <{name}>.");
		return match.Groups[1].Value;
	}

	[Fact]
	public void TemplateTargetsTheRepositoryTargetFramework()
	{
		var dotnetVersion = ReadRepositoryProperty("DotNetVersion");
		var platformVersion = ReadRepositoryProperty("TizenPlatformVersion");

		var csproj = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiTizenApp.csproj"));
		var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json")));

		var placeholder = template.RootElement
			.GetProperty("symbols").GetProperty("TizenPlatformVersion");

		Assert.Equal(platformVersion, placeholder.GetProperty("defaultValue").GetString());

		// The TFM is versioned, never a bare net11.0-tizen.
		Assert.Contains($"<TargetFramework>net{dotnetVersion}-tizenTIZEN_PLATFORM_VERSION</TargetFramework>", csproj);
	}

	[Fact]
	public void TemplateManifestApiVersionMatchesTheRepositoryContract()
	{
		var expected = ReadRepositoryProperty("TizenManifestApiVersion");

		var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json")));
		var symbol = template.RootElement.GetProperty("symbols").GetProperty("TizenApiVersion");

		Assert.Equal(expected, symbol.GetProperty("defaultValue").GetString());

		var manifest = File.ReadAllText(Path.Combine(TemplateDirectory, "Platforms", "Tizen", "tizen-manifest.xml"));
		Assert.Contains("api-version=\"TIZEN_API_VERSION\"", manifest);
	}

	[Fact]
	public void TemplateReferencesThePinnedTizenReferencePack()
	{
		// eng/Maui.props is the single declaration of the TizenFX reference pack.
		var mauiProps = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "Maui.props"));
		var expected = Regex.Match(mauiProps, "<TizenReferencePackVersion[^>]*>([^<]+)</TizenReferencePackVersion>").Groups[1].Value;
		Assert.False(string.IsNullOrEmpty(expected), "eng/Maui.props does not declare <TizenReferencePackVersion>.");

		var expectedId = Regex.Match(mauiProps, "<TizenReferencePackId[^>]*>([^<]+)</TizenReferencePackId>").Groups[1].Value;

		var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json")));
		var symbol = template.RootElement.GetProperty("symbols").GetProperty("TizenRefPackVersion");

		Assert.Equal(expected, symbol.GetProperty("defaultValue").GetString());

		var csproj = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiTizenApp.csproj"));
		Assert.Contains(expectedId, csproj);
	}

	/// <summary>
	/// The Tizen references must be conditioned so the generated project can grow other target
	/// frameworks without dragging the Tizen backend into them.
	/// </summary>
	[Fact]
	public void TizenPackageReferencesAreConditioned()
	{
		var csproj = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiTizenApp.csproj"));

		// The identifier must be computed, not read from $(TargetPlatformIdentifier): the SDK does
		// not set that until after the project body is evaluated, so the references would be
		// dropped from every generated project.
		Assert.DoesNotContain("Condition=\"'$(TargetPlatformIdentifier)' == 'tizen'\"", csproj);
		Assert.Contains("GetTargetPlatformIdentifier('$(TargetFramework)')", csproj);
		Assert.Contains("<ItemGroup Condition=\"'$(_MauiTizenPlatform)' == 'tizen'\">", csproj);
		Assert.Contains("Maui.Tizen.Build.Tasks", csproj);
	}

	[Fact]
	public void TemplateRegistersTheTizenHostBuilderExtension()
	{
		var mauiProgram = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiProgram.cs"));

		Assert.Contains("UseMauiAppTizen<App>()", mauiProgram);
		Assert.Contains("#if TIZEN", mauiProgram);
	}

	[Fact]
	public void TemplateManifestUsesTheGeneratorPlaceholders()
	{
		var manifest = File.ReadAllText(Path.Combine(TemplateDirectory, "Platforms", "Tizen", "tizen-manifest.xml"));

		// These are the tokens GenerateTizenManifest replaces; if they drift the generated
		// manifest silently keeps the placeholder text.
		Assert.Contains("maui-application-id-placeholder", manifest);
		Assert.Contains("maui-application-title-placeholder", manifest);
		Assert.Contains("maui-appicon-placeholder", manifest);
	}
}
