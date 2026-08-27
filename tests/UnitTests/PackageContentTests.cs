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
	private static IReadOnlyList<string> EntriesOf(string packageId)
		=> ProducedPackages.EntryNames(ProducedPackages.PathOf(packageId));

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

	[Fact]
	public void MuslHostsDoNotFallBackToAGlibcNative()
	{
		var project = File.ReadAllText(Path.Combine(BuildTasksProjectDirectory, "Maui.Tizen.Build.Tasks.csproj"));

		Assert.Contains(
			"'$(_MauiTizenHostIsMusl)' != 'true'",
			project,
			StringComparison.Ordinal);
		Assert.Contains("Code=\"MAUITIZEN1012\"", project, StringComparison.Ordinal);
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

	/// <summary>
	/// Every packable project declares <c>PackageReadmeFile</c>, so the readme must actually be in
	/// the package or pack fails with NU5039.
	/// </summary>
	/// <remarks>
	/// The readme is contributed by a shared <c>ItemGroup</c> in Directory.Build.props whose
	/// condition reads <c>$(IsPackable)</c>, a property each project sets in its own body - which
	/// looks like it should not work, because Directory.Build.props is imported first.
	///
	/// It does work: MSBuild evaluates ALL properties, across the project body and every import,
	/// before it evaluates ANY items. A property assigned inside Directory.Build.props sees the
	/// pre-body value, but an item condition in that same file sees the final one.
	///
	/// This test deliberately asserts the OUTCOME rather than where the item is declared, so it
	/// keeps protecting against NU5039 if that ItemGroup is ever moved to Directory.Build.targets
	/// or restructured.
	/// </remarks>
	[Theory]
	[InlineData("Maui.Tizen.Build.Tasks")]
	[InlineData("Maui.Tizen.Templates")]
	public void PackageShipsTheReadmeItDeclares(string packageId)
	{
		var nuspec = ReadNuspec(packageId);

		var declared = System.Text.RegularExpressions.Regex.Match(nuspec, "<readme>([^<]+)</readme>");
		Assert.True(declared.Success, $"'{packageId}' does not declare a <readme> element.");

		var readmePath = declared.Groups[1].Value.Replace('\\', '/');

		Assert.Contains(
			readmePath,
			EntriesOf(packageId));
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

	/// <summary>
	/// Every template file must land under the directory that holds <c>.template.config</c>, with
	/// its tree intact.
	/// </summary>
	/// <remarks>
	/// A template package is only a template package if the tree survives. Flattened into
	/// <c>content/</c>, the package still installs and <c>dotnet new</c> still "succeeds" - it
	/// just writes package internals into the user's folder instead of a project, which is a
	/// failure nobody attributes to packing. Asserting the shape here, and instantiating for real
	/// below, means neither half can regress unnoticed.
	/// </remarks>
	[Fact]
	public void TemplatePackageKeepsTheTemplateDirectoryTree()
	{
		var entries = EntriesOf("Maui.Tizen.Templates");

		var templateRoot = "content/templates/maui-tizen/";
		var templateEntries = entries.Where(e => e.StartsWith("content/", StringComparison.Ordinal)).ToList();

		Assert.NotEmpty(templateEntries);
		Assert.All(templateEntries, e => Assert.StartsWith(templateRoot, e, StringComparison.Ordinal));

		// Nothing flattened: the deepest authored file must still be nested.
		Assert.Contains(templateEntries, e => e.EndsWith("/Resources/AppIcon/appicon.svg", StringComparison.Ordinal));

		// And the source tree and the package tree agree file for file.
		var sourceRoot = Path.Combine(RepositoryRoot, "src", "Maui.Tizen.Templates", "templates", "maui-tizen");
		var expected = Directory
			.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
			.Select(p => templateRoot + Path.GetRelativePath(sourceRoot, p).Replace('\\', '/'))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToList();

		Assert.Equal(expected, templateEntries.OrderBy(p => p, StringComparer.Ordinal).ToList());
	}

	[Fact]
	public void TemplatePackageIsMarkedAsATemplatePackage()
	{
		Assert.Contains("<packageTypes>", ReadNuspec("Maui.Tizen.Templates"));
	}

	/// <summary>
	/// Two packs of identical sources must ship identical content.
	/// </summary>
	/// <remarks>
	/// This compares the packages by NORMALIZED ENTRIES - the shipped entry names plus a hash of
	/// each entry's bytes - rather than by comparing archives byte for byte or by comparing zip
	/// entry timestamps. Those differ between two packs by design: NuGet stamps the .nuspec and
	/// the OPC bookkeeping per pack, and zip entries carry the local file's modification time.
	/// A byte or timestamp comparison would therefore either fail always or, if narrowed to a
	/// single archive read twice, assert nothing at all - which is what the previous version of
	/// this test did: it called the same accessor twice on the same file.
	/// </remarks>
	[Fact]
	public void PackingTheSameSourcesTwiceProducesTheSameContent()
	{
		var second = ProducedPackages.PackAgain();

		foreach (var packageId in new[] { "Maui.Tizen.Build.Tasks", "Maui.Tizen.Templates" })
		{
			var first = NormalizedContent(ProducedPackages.PathOf(packageId));
			var repeat = NormalizedContent(ProducedPackages.PathOf(packageId, second));

			Assert.Equal(first, repeat);
		}
	}

	/// <summary>
	/// Entry name to content hash, for every shipped entry. The .nuspec is compared by name only:
	/// it legitimately carries per-pack metadata.
	/// </summary>
	private static IReadOnlyList<string> NormalizedContent(string packagePath)
	{
		using var archive = ZipFile.OpenRead(packagePath);

		return archive.Entries
			.Select(e => e.FullName.Replace('\\', '/'))
			.Where(ProducedPackages.IsMeaningfulEntry)
			.OrderBy(n => n, StringComparer.Ordinal)
			.Select(name =>
			{
				if (name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
					return name + " (metadata)";

				using var stream = archive.GetEntry(name)!.Open();
				using var sha = System.Security.Cryptography.SHA256.Create();
				return name + " " + Convert.ToHexString(sha.ComputeHash(stream));
			})
			.ToList();
	}

	/// <summary>
	/// Extracts the package once and returns its files on disk, for assertions that need to read
	/// binary content rather than just entry names.
	/// </summary>
	private static IReadOnlyList<(string Name, string Path)> ExtractedPackageFiles(string packageId)
	{
		var source = ProducedPackages.PathOf(packageId);

		var destination = Path.Combine(ProducedPackages.Directory, "extracted", packageId);

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
		using var archive = ZipFile.OpenRead(ProducedPackages.PathOf(packageId));
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

	private static string ReadTemplateProjectWithoutComments()
		=> Regex.Replace(
			File.ReadAllText(Path.Combine(TemplateDirectory, "MauiTizenApp.csproj")),
			"<!--.*?-->",
			string.Empty,
			RegexOptions.Singleline);

	/// <summary>
	/// The TizenFX reference pack must be declared once, in eng/Maui.props, and must NEVER be a
	/// PackageReference in the emitted project.
	/// </summary>
	/// <remarks>
	/// Samsung.Tizen.Ref.API15 has the <c>DotnetPlatform</c> package type, which NuGet refuses to
	/// install through PackageReference (NU1213). The Samsung workload supplies it as a reference
	/// pack, resolved from the tizen platform version in the target framework. The template used
	/// to reference it explicitly, which meant the one configuration the template exists to serve
	/// - a machine with the workload installed - was the configuration it broke.
	///
	/// Comments are stripped before matching: the template file explains this rule in prose, and
	/// the prose names the very reference it forbids.
	/// </remarks>
	[Fact]
	public void TemplateNeverPackageReferencesTheTizenReferencePack()
	{
		var mauiProps = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "Maui.props"));
		var expectedId = Regex.Match(mauiProps, "<TizenReferencePackId[^>]*>([^<]+)</TizenReferencePackId>").Groups[1].Value;
		Assert.False(string.IsNullOrEmpty(expectedId), "eng/Maui.props does not declare <TizenReferencePackId>.");

		var csproj = ReadTemplateProjectWithoutComments();

		Assert.DoesNotMatch(new Regex($@"<PackageReference\s+Include=""{Regex.Escape(expectedId)}"""), csproj);

		var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json")));

		// The symbol that fed the reference is gone too, so nothing can quietly re-add it.
		Assert.False(
			template.RootElement.GetProperty("symbols").TryGetProperty("TizenRefPackVersion", out _),
			"The template still declares a TizenFX reference pack version symbol.");
	}

	/// <summary>
	/// The MAUI packages are on their own version line and must be pinned to the repository's
	/// declared development baseline, not to the Maui.Tizen version.
	/// </summary>
	[Fact]
	public void TemplatePinsTheMauiPackagesToTheDevelopmentBaseline()
	{
		var packages = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
		var expected = Regex
			.Match(packages, @"<PackageVersion\s+Include=""Microsoft\.Maui\.Controls""\s+Version=""([^""]+)""")
			.Groups[1].Value;

		Assert.False(string.IsNullOrEmpty(expected), "Directory.Packages.props does not pin Microsoft.Maui.Controls.");

		var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json")));
		var symbol = template.RootElement.GetProperty("symbols").GetProperty("MauiVersion");

		Assert.Equal(expected, symbol.GetProperty("defaultValue").GetString());

		var csproj = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiTizenApp.csproj"));
		Assert.Contains(@"<PackageReference Include=""Microsoft.Maui.Controls"" Version=""MAUI_VERSION"" />", csproj);
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

	/// <summary>
	/// Every Maui.Tizen package the template references must be one this repository declares.
	/// </summary>
	/// <remarks>
	/// The template referenced an umbrella <c>Maui.Tizen</c> package that no project produces and
	/// that has never existed. Package IDs in this repository follow the project name, so the
	/// check is simply that the project is there.
	/// </remarks>
	[Fact]
	public void TemplateReferencesOnlyPackageIdsThisRepositoryDeclares()
	{
		var csproj = ReadTemplateProjectWithoutComments();

		var owned = Regex
			.Matches(csproj, @"<PackageReference\s+Include=""(Maui\.Tizen[^""]*)""")
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		Assert.NotEmpty(owned);

		foreach (var id in owned)
		{
			Assert.True(
				File.Exists(Path.Combine(RepositoryRoot, "src", id, id + ".csproj")),
				$"The template references '{id}', but src/{id}/{id}.csproj does not exist, so no project "
					+ "declares that package ID.");
		}

		// The hosting entry point and the Controls handlers both have to be there for the emitted
		// MauiProgram.cs and App.cs to compile once the gate lifts.
		Assert.Contains("Maui.Tizen.Core", owned);
		Assert.Contains("Maui.Tizen.Controls", owned);
	}

	[Fact]
	public void TemplateRegistersTheTizenHostBuilderExtension()
	{
		var mauiProgram = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiProgram.cs"));

		Assert.Contains("UseMauiAppTizen<App>()", mauiProgram);
		Assert.Contains("using Microsoft.Maui.Platforms.Tizen.Hosting;", mauiProgram);

		// The umbrella namespace the old conditional imported does not exist.
		Assert.DoesNotContain("using Maui.Tizen;", mauiProgram);
	}

	/// <summary>
	/// No file under templates/ may contain a C# preprocessor directive, commented out or not.
	/// </summary>
	/// <remarks>
	/// The Template Engine parses <c>#if</c> / <c>#else</c> / <c>#endif</c> in template content as
	/// TEMPLATE conditionals, including the commented-out form. That is what silently rewrote the
	/// generated host builder, and a commented example of the same directives made
	/// <c>dotnet new</c> fail outright after writing a partial project. This is asserted on the
	/// SOURCE as well as on the instantiated output (see
	/// <c>TemplateInstantiationTests.GeneratedSourcesContainNoPreprocessorDirectives</c>), because
	/// the source check names the rule while the instantiation check proves the consequence.
	/// </remarks>
	[Fact]
	public void TemplateContentContainsNoPreprocessorDirectives()
	{
		foreach (var file in Directory.EnumerateFiles(TemplateDirectory, "*.cs", SearchOption.AllDirectories))
		{
			foreach (var line in File.ReadAllLines(file))
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal))
					trimmed = trimmed.Substring(2).TrimStart();

				Assert.False(
					trimmed.StartsWith("#if", StringComparison.Ordinal)
						|| trimmed.StartsWith("#else", StringComparison.Ordinal)
						|| trimmed.StartsWith("#endif", StringComparison.Ordinal),
					$"'{Path.GetRelativePath(RepositoryRoot, file)}' contains a preprocessor directive. "
						+ "The Template Engine consumes these as template conditionals; explain the rule in "
						+ "Maui.Tizen.Templates.csproj instead, which is not template content.");
			}
		}
	}

	/// <summary>
	/// The Tizen entry point must derive from this backend's application class, not from MAUI's.
	/// </summary>
	[Fact]
	public void TemplateEntryPointDerivesFromTizenMauiApplication()
	{
		var main = File.ReadAllText(Path.Combine(TemplateDirectory, "Platforms", "Tizen", "Main.cs"));

		Assert.Contains("using Microsoft.Maui.Platforms.Tizen;", main);
		Assert.Contains(": TizenMauiApplication", main);
	}

	/// <summary>
	/// The template must not register a font it does not ship, and must not hand a non-font to
	/// the font pipeline.
	/// </summary>
	/// <remarks>
	/// MauiProgram.cs registered <c>OpenSans-Regular.ttf</c> while Resources/Fonts contained only
	/// an instructions file. A missing font is not a build error - the font loader fails to
	/// resolve the name at runtime - so nothing in the pipeline noticed, in either direction: the
	/// bare <c>Resources\Fonts\*</c> glob simultaneously fed that instructions .txt to the font
	/// pipeline, which copies it into the TPK's res/fonts.
	/// </remarks>
	[Fact]
	public void TemplateRegistersNoFontItDoesNotShip()
	{
		var mauiProgram = File.ReadAllText(Path.Combine(TemplateDirectory, "MauiProgram.cs"));
		var fonts = Path.Combine(TemplateDirectory, "Resources", "Fonts");

		foreach (Match match in Regex.Matches(mauiProgram, @"^\s*fonts\.AddFont\(""([^""]+)""", RegexOptions.Multiline))
		{
			Assert.True(
				File.Exists(Path.Combine(fonts, match.Groups[1].Value)),
				$"MauiProgram.cs registers '{match.Groups[1].Value}' but the template does not ship it.");
		}

		var csproj = ReadTemplateProjectWithoutComments();

		Assert.DoesNotContain(@"<MauiFont Include=""Resources\Fonts\*"" />", csproj);
		Assert.Contains(@"<MauiFont Include=""Resources\Fonts\*.ttf"" />", csproj);
	}

	/// <summary>
	/// Packing must not depend on NuGet inferring the template's directory structure.
	/// </summary>
	/// <remarks>
	/// $(ContentTargetFolders) makes the packed layout an inference from each item's identity.
	/// Any Content item NuGet cannot relate back to the project directory is written flat into
	/// the target folder, which turns a template package into a package that installs and then
	/// emits its own internals instead of a project. The destination is therefore written out.
	/// </remarks>
	[Fact]
	public void TemplateContentDeclaresItsPackagePathExplicitly()
	{
		var project = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Maui.Tizen.Templates", "Maui.Tizen.Templates.csproj"));

		Assert.DoesNotContain("<ContentTargetFolders>", project);
		Assert.Contains(@"PackagePath=""content/templates/%(RecursiveDir)%(Filename)%(Extension)""", project);
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
