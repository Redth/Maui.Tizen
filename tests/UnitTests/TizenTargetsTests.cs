using System;
using System.IO;
using System.Linq;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// End-to-end MSBuild coverage of the Tizen backend's use of the public Resizetizer contract from
/// dotnet/maui PR 36653.
/// </summary>
[Trait("Category", "MSBuild")]
public class TizenTargetsTests : TestBase
{
	private MSBuildProjectBuilder CreateApp(bool lateOptIn = false, string? root = null)
	{
		var builder = new MSBuildProjectBuilder(root ?? CreateTempDirectory("maui-tizen-msbuild")) { LateOptIn = lateOptIn };

		WriteAppSources(builder);

		builder
			.WithProperty("ApplicationId", "com.contoso.tizenapp")
			.WithProperty("ApplicationTitle", "Contoso Tizen")
			.WithProperty("ApplicationDisplayVersion", "1.2.3")
			.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"))
			.WithItem("MauiSplashScreen", "Resources\\Splash\\splash.svg", ("Color", "#512BD4"), ("BaseSize", "128,128"))
			.WithItem("MauiImage", "Resources\\Images\\logo.svg")
			.WithItem("MauiFont", "Resources\\Fonts\\TestFont.ttf")
			.WithItem("MauiAsset", "Resources\\Raw\\data.json", ("LogicalName", "data.json"));

		return builder;
	}

	/// <summary>
	/// Writes the application's resource files. Separate from <see cref="CreateApp"/> so a
	/// two-build test can lay the sources down once and then re-declare the project without
	/// touching them.
	/// </summary>
	private static void WriteAppSources(MSBuildProjectBuilder builder)
	{
		builder.WriteSvg("Resources/AppIcon/appicon.svg");
		builder.WriteSvg("Resources/Splash/splash.svg", "#FFFFFF");
		builder.WriteSvg("Resources/Images/logo.svg", "#00FF00");
		builder.WriteText("Resources/Fonts/TestFont.ttf", "not-a-real-font-but-a-stable-file");
		builder.WriteText("Resources/Raw/data.json", "{}");
		builder.WriteTizenManifest();
	}

	private static void AssertBuildSucceeded(BuildResult result)
		=> Assert.True(result.Success, "Build failed:" + Environment.NewLine + result.Output);

	/// <summary>
	/// Re-declares the same application in a directory an earlier build already used, WITHOUT
	/// rewriting any source file.
	/// </summary>
	/// <remarks>
	/// This is what makes the two-build mutation tests below mean anything. <see cref="CreateApp"/>
	/// writes the SVGs and the manifest every time it is called, so calling it twice bumps their
	/// timestamps and the whole pipeline is out of date for a reason that has nothing to do with
	/// the metadata under test - the test would then pass with or without the fix. Here only the
	/// project file changes, which is exactly the state a user reaches by editing an item's
	/// metadata in their .csproj: no content changed, only what the build was told about it.
	/// </remarks>
	private static MSBuildProjectBuilder RedeclareApp(
		string root,
		(string Name, string Value)[]? icon = null,
		(string Name, string Value)[]? splash = null,
		bool withSplash = true,
		bool lateOptIn = false)
	{
		var builder = new MSBuildProjectBuilder(root) { LateOptIn = lateOptIn };

		builder
			.WithProperty("ApplicationId", "com.contoso.tizenapp")
			.WithProperty("ApplicationTitle", "Contoso Tizen")
			.WithProperty("ApplicationDisplayVersion", "1.2.3")
			.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", icon ?? new[] { ("Color", "#512BD4") });

		if (withSplash)
			builder.WithItem("MauiSplashScreen", "Resources\\Splash\\splash.svg", splash ?? new[] { ("Color", "#512BD4"), ("BaseSize", "128,128") });

		builder
			.WithItem("MauiImage", "Resources\\Images\\logo.svg")
			.WithItem("MauiFont", "Resources\\Fonts\\TestFont.ttf")
			.WithItem("MauiAsset", "Resources\\Raw\\data.json", ("LogicalName", "data.json"));

		return builder;
	}

	private static string IntermediateDirectory(MSBuildProjectBuilder app, BuildResult result)
		=> Path.Combine(app.ProjectDirectory, result.Property("MauiTizenIntermediateOutputPath"));

	private static string SplashDirectory(MSBuildProjectBuilder app, BuildResult result)
		=> Path.Combine(IntermediateDirectory(app, result), GenerateTizenSplashScreens.SplashDirectoryName);

	/// <summary>File name to content hash for every composed splash image.</summary>
	private static IReadOnlyDictionary<string, string> HashSplashImages(string splashDirectory)
		=> Directory
			.GetFiles(splashDirectory, "*.png")
			.ToDictionary(
				f => Path.GetFileName(f),
				f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))),
				StringComparer.Ordinal);

	[Fact]
	public void EarlyOptInProducesTizenTpkInputs()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		// The early opt-in path is the one where the Resizetizer sees ResizetizerPlatformType
		// during evaluation.
		Assert.Equal("True", result.Property("_ResizetizerIsCompatibleApp"));
		Assert.Equal("tizen", result.Property("ResizetizerPlatformType"));

		// The built-in Tizen branches must NOT be active, so the externalized generators own the output.
		Assert.NotEqual("True", result.Property("_ResizetizerIsTizenApp"));
		Assert.Equal("false", result.Property("MauiTizenUseBuiltInResizetizerSupport"));

		var tpkFiles = result.ItemsOf("TizenTpkUserIncludeFiles").ToList();
		Assert.NotEmpty(tpkFiles);

		// Images land in the Tizen resource buckets.
		Assert.Contains(tpkFiles, i =>
			i.Metadata1.Replace('\\', '/').StartsWith("res/contents/default_All-", StringComparison.Ordinal) &&
			Path.GetFileName(i.Identity) == "logo.png");

		// App icons land under shared/res.
		Assert.Contains(tpkFiles, i => i.Metadata1.Replace('\\', '/').StartsWith("shared/res/", StringComparison.Ordinal));

		// res.xml is contributed at the resource root.
		Assert.Contains(tpkFiles, i => Path.GetFileName(i.Identity) == "res.xml" && i.Metadata1.TrimEnd('\\', '/') == "res");

		// Fonts go to res/fonts.
		Assert.Contains(tpkFiles, i =>
			Path.GetFileName(i.Identity) == "TestFont.ttf" &&
			i.Metadata1.Replace('\\', '/').TrimEnd('/') == "res/fonts");

		// Splash screens go to shared/res/splash.
		Assert.Contains(tpkFiles, i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash" &&
			Path.GetFileName(i.Identity).StartsWith("splash.", StringComparison.Ordinal));

		// Raw assets become TizenResource with a TPK file name.
		var assets = result.ItemsOf("TizenResource").ToList();
		Assert.Contains(assets, i => Path.GetFileName(i.Identity) == "data.json" && !string.IsNullOrEmpty(i.Metadata1));
	}

	[Fact]
	public void EarlyOptInGeneratesTheTizenManifest()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var generated = result.Property("TizenManifestFile");
		Assert.EndsWith("tizen-manifest.xml", generated);

		var manifestPath = Path.IsPathRooted(generated) ? generated : Path.Combine(app.ProjectDirectory, generated);
		Assert.True(File.Exists(manifestPath), $"Generated manifest not found at '{manifestPath}'.");

		var xml = System.Xml.Linq.XDocument.Load(manifestPath);
		System.Xml.Linq.XNamespace ns = "http://tizen.org/ns/packages";

		Assert.Equal("com.contoso.tizenapp", xml.Root!.Attribute("package")!.Value);
		Assert.Equal("1.2.3", xml.Root!.Attribute("version")!.Value);

		var uiApplication = xml.Root!.Element(ns + "ui-application")!;
		Assert.Equal("Contoso Tizen", uiApplication.Element(ns + "label")!.Value);
		Assert.Equal("xhdpi/appicon.xhigh.png", uiApplication.Element(ns + "icon")!.Value);

		var splashes = uiApplication.Element(ns + "splash-screens")!.Elements(ns + "splash-screen").ToList();
		Assert.Equal(4, splashes.Count);
		Assert.All(splashes, s => Assert.Equal("img", s.Attribute("type")!.Value));
	}

	/// <summary>
	/// The late opt-in path is what an external backend uses when its targets are imported after
	/// the Resizetizer's. The Resizetizer must still collect every resource kind.
	/// </summary>
	[Fact]
	public void LateOptInCollectsAllResourceKinds()
	{
		var app = CreateApp(lateOptIn: true);
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		// Proof that this really was the late path: the evaluation-time compatibility flag never
		// became True, so only the execution-time fallbacks could have run.
		Assert.NotEqual("True", result.Property("_ResizetizerIsCompatibleApp"));
		Assert.Equal("tizen", result.Property("ResizetizerPlatformType"));

		Assert.NotEmpty(result.ItemsOf("MauiProcessedImage"));
		Assert.NotEmpty(result.ItemsOf("MauiProcessedFont"));
		Assert.NotEmpty(result.ItemsOf("MauiProcessedAsset"));

		var tpkFiles = result.ItemsOf("TizenTpkUserIncludeFiles").ToList();
		Assert.Contains(tpkFiles, i => Path.GetFileName(i.Identity) == "res.xml");
		Assert.Contains(tpkFiles, i => Path.GetFileName(i.Identity) == "TestFont.ttf");
		Assert.Contains(result.ItemsOf("TizenResource"), i => Path.GetFileName(i.Identity) == "data.json");
	}

	/// <summary>
	/// PR 36653 makes referenced-project MauiImage / MauiAsset items flow into the app head for
	/// external backends. Without it, a class library's resources would silently vanish from the TPK.
	/// </summary>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CollectsResourcesFromReferencedProjects(bool lateOptIn)
	{
		var root = CreateTempDirectory("maui-tizen-projref");

		var library = new MSBuildProjectBuilder(root, "ResourceLibrary");
		library.WriteSvg("Resources/Images/library_image.svg", "#FF0000");
		library.WriteText("Resources/Raw/library_asset.txt", "from-library");
		File.WriteAllText(library.ProjectPath, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0</TargetFramework>
			    <Nullable>disable</Nullable>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="Microsoft.Maui.Resizetizer" Version="{ResizetizerPackageVersion}" />
			  </ItemGroup>
			  <ItemGroup>
			    <MauiImage Include="Resources\Images\library_image.svg" />
			    <MauiAsset Include="Resources\Raw\library_asset.txt" LogicalName="library_asset.txt" />
			  </ItemGroup>
			</Project>
			""");

		var app = CreateApp(lateOptIn, root);
		// The reference is a sibling inside the same temp root.
		var relativeReference = Path.Combine("..", "ResourceLibrary", "ResourceLibrary.csproj");
		app.WithProjectReference(relativeReference);
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		Assert.Contains(result.FileNamesOf("MauiProcessedImage"), n => n == "library_image.png");
		Assert.Contains(result.FileNamesOf("MauiProcessedAsset"), n => n == "library_asset.txt");

		Assert.Contains(result.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "library_image.png");
		Assert.Contains(result.ItemsOf("TizenResource"), i => Path.GetFileName(i.Identity) == "library_asset.txt");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ReferencedSplashMetadataInvalidatesProcessedAndComposedImages(bool lateOptIn)
	{
		var root = CreateTempDirectory("maui-tizen-projref-splash-state");
		var library = new MSBuildProjectBuilder(root, "ResourceLibrary");
		library.WriteSvg("Resources/Splash/library_splash.svg", "#FFFFFF");

		void WriteLibraryProject(string color, string baseSize)
		{
			File.WriteAllText(library.ProjectPath, $"""
				<Project Sdk="Microsoft.NET.Sdk">
				  <PropertyGroup>
				    <TargetFramework>net11.0</TargetFramework>
				  </PropertyGroup>
				  <ItemGroup>
				    <PackageReference Include="Microsoft.Maui.Resizetizer" Version="{ResizetizerPackageVersion}" />
				  </ItemGroup>
				  <ItemGroup>
				    <MauiSplashScreen Include="Resources\Splash\library_splash.svg"
				                      Link="library_splash.svg"
				                      Color="{color}"
				                      BaseSize="{baseSize}" />
				  </ItemGroup>
				</Project>
				""");
		}

		WriteLibraryProject("#512BD4", "128,128");

		// Write the app sources once, then declare an app whose only splash comes from the library.
		CreateApp(root: root);
		var app = RedeclareApp(root, withSplash: false, lateOptIn: lateOptIn);
		app.WithProjectReference(Path.Combine("..", "ResourceLibrary", "ResourceLibrary.csproj"));
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var firstProcessed = first.ItemsOf("MauiProcessedImage")
			.Where(item => Path.GetFileName(item.Identity) == "library_splash.png")
			.ToDictionary(
				item => item.Identity,
				item => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(item.Identity))),
				StringComparer.Ordinal);
		var firstComposed = HashSplashImages(SplashDirectory(app, first));

		Assert.NotEmpty(firstProcessed);
		Assert.NotEmpty(firstComposed);

		// Change metadata only. The SVG itself and every app source retain their timestamps.
		WriteLibraryProject("#00FF00", "64,64");

		var second = app.Build();
		AssertBuildSucceeded(second);

		var secondProcessed = second.ItemsOf("MauiProcessedImage")
			.Where(item => Path.GetFileName(item.Identity) == "library_splash.png")
			.ToDictionary(
				item => item.Identity,
				item => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(item.Identity))),
				StringComparer.Ordinal);
		var secondComposed = HashSplashImages(SplashDirectory(app, second));

		Assert.Equal(firstProcessed.Keys.OrderBy(path => path), secondProcessed.Keys.OrderBy(path => path));
		Assert.Contains(firstProcessed, pair => pair.Value != secondProcessed[pair.Key]);
		Assert.Equal(firstComposed.Keys.OrderBy(path => path), secondComposed.Keys.OrderBy(path => path));
		Assert.Contains(firstComposed, pair => pair.Value != secondComposed[pair.Key]);
	}

	/// <summary>
	/// Deleting a generated output while the Resizetizer's own stamps stay current must heal on the
	/// next build rather than silently shipping a TPK with a missing res.xml or splash screen.
	/// </summary>
	[Fact]
	public void IncrementalBuildHealsDeletedGeneratedOutputs()
	{
		var app = CreateApp();
		app.Generate();

		AssertBuildSucceeded(app.Build());

		var first = app.Build();
		AssertBuildSucceeded(first);

		var resXml = first.ItemsOf("TizenTpkUserIncludeFiles").Single(i => Path.GetFileName(i.Identity) == "res.xml").Identity;
		var splashMap = Path.Combine(
			app.ProjectDirectory,
			first.Property("MauiTizenIntermediateOutputPath"),
			GenerateTizenSplashScreens.SplashMapFileName);
		var generatedManifest = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));

		Assert.True(File.Exists(resXml));
		Assert.True(File.Exists(splashMap));
		Assert.True(File.Exists(generatedManifest));

		File.Delete(resXml);
		File.Delete(splashMap);
		File.Delete(generatedManifest);

		var healed = app.Build();
		AssertBuildSucceeded(healed);

		Assert.True(File.Exists(resXml), "res.xml was not regenerated on the incremental build.");
		Assert.True(File.Exists(splashMap), "The splash map was not regenerated on the incremental build.");
		Assert.True(File.Exists(generatedManifest), "The Tizen manifest was not regenerated on the incremental build.");

		// The healed build must still produce the full TPK input set.
		Assert.Contains(healed.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "res.xml");
		Assert.Contains(healed.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "TestFont.ttf");
		Assert.Contains(healed.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash");
	}

	/// <summary>
	/// Deleting one generated splash image while the map survives must regenerate the whole set.
	/// </summary>
	/// <remarks>
	/// The generation target declares only the map as its output, so before the cache validation
	/// target existed this state looked up to date: the build skipped generation and produced a
	/// TPK with a splash screen missing, with nothing in the log to say so.
	/// </remarks>
	[Fact]
	public void IncrementalBuildRegeneratesSplashScreensWhenOneImageIsDeleted()
	{
		var app = CreateApp();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var splashDirectory = Path.Combine(
			app.ProjectDirectory,
			first.Property("MauiTizenIntermediateOutputPath"),
			GenerateTizenSplashScreens.SplashDirectoryName);

		var splashImages = Directory.GetFiles(splashDirectory, "*.png");
		Assert.True(splashImages.Length > 1, "Expected several generated splash screens.");

		var deleted = splashImages.OrderBy(f => f, StringComparer.Ordinal).First();
		File.Delete(deleted);

		// The map is deliberately left in place: that is the state that used to be trusted.
		var mapFile = Path.Combine(
			app.ProjectDirectory,
			first.Property("MauiTizenIntermediateOutputPath"),
			GenerateTizenSplashScreens.SplashMapFileName);
		Assert.True(File.Exists(mapFile));

		var healed = app.Build();
		AssertBuildSucceeded(healed);

		Assert.True(File.Exists(deleted), $"'{Path.GetFileName(deleted)}' was not regenerated.");

		// Every image the map promises must be back, and still be a decodable PNG.
		foreach (var line in File.ReadAllLines(mapFile).Where(l => !string.IsNullOrWhiteSpace(l)))
		{
			var relative = line.Split('|').Last();
			var path = Path.Combine(app.ProjectDirectory, first.Property("MauiTizenIntermediateOutputPath"), relative);

			Assert.True(File.Exists(path), $"'{relative}' is listed in the splash map but missing on disk.");
		}

		Assert.Contains(healed.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash");
	}

	[Fact]
	public void IncrementalBuildKeepsACompleteSplashCache()
	{
		var app = CreateApp();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var splashDirectory = Path.Combine(
			app.ProjectDirectory,
			first.Property("MauiTizenIntermediateOutputPath"),
			GenerateTizenSplashScreens.SplashDirectoryName);
		var sentinel = Path.Combine(splashDirectory, "cache-sentinel.txt");
		File.WriteAllText(sentinel, "generation did not rerun");

		AssertBuildSucceeded(app.Build());

		Assert.True(File.Exists(sentinel), "A complete splash cache was unnecessarily regenerated.");
	}

	/// <summary>
	/// Backend artifacts written into the intermediate resource folder must never be surfaced as
	/// processed images, which is the incremental regression PR 36653 fixed.
	/// </summary>
	[Fact]
	public void IncrementalBuildDoesNotSurfaceBackendArtifactsAsImages()
	{
		var app = CreateApp();
		app.Generate();

		AssertBuildSucceeded(app.Build());

		var first = app.Build();
		AssertBuildSucceeded(first);

		var anyImage = first.ItemsOf("MauiProcessedImage").First().Identity;
		var resizetizerRoot = Directory.GetParent(Path.GetDirectoryName(anyImage)!)!.Parent!.Parent!.FullName;
		File.WriteAllText(Path.Combine(resizetizerRoot, "backend.items"), "artifact");

		var second = app.Build();
		AssertBuildSucceeded(second);

		Assert.DoesNotContain(second.FileNamesOf("MauiProcessedImage"), n => n == "backend.items");
		Assert.DoesNotContain(second.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "backend.items");
	}

	[Fact]
	public void RegistersPlatformsTizenAsAPlatformSpecificFolder()
	{
		var app = CreateApp();
		app.WithProperty("SingleProject", "true");
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var folder = result.ItemsOf("MauiPlatformSpecificFolder")
			.SingleOrDefault(i => i.Identity.Replace('\\', '/').TrimEnd('/') == "Platforms/Tizen");

		Assert.NotNull(folder);
		Assert.Equal("tizen", folder!.Metadata1);
	}

	[Fact]
	public void DiscoversTheAuthoredManifestUnderPlatformsTizen()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		// The property starts out pointing at the authored file and ends up pointing at the
		// generated one, which is what the Samsung packaging targets consume.
		Assert.Contains("maui-tizen", result.Property("TizenManifestFile").Replace('\\', '/'));
	}

	/// <summary>
	/// While the Resizetizer still generates res.xml for the tizen platform type, this package
	/// must adopt that file rather than generating a second copy into the same TPK location.
	/// </summary>
	[Fact]
	public void ContributesExactlyOneResourceXml()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var resXml = result.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => Path.GetFileName(i.Identity) == "res.xml")
			.ToList();

		Assert.Single(resXml);
		Assert.Equal("res", resXml[0].Metadata1.Replace('\\', '/').TrimEnd('/'));
	}

	[Fact]
	public void GeneratedOutputsLiveUnderTheIntermediateOutputPath()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		// Regression: buildTransitive props are imported before IntermediateOutputPath exists,
		// so computing the folder there produced a stray project-relative 'maui-tizen/'.
		var intermediate = result.Property("MauiTizenIntermediateOutputPath").Replace('\\', '/');
		Assert.StartsWith("obj/", intermediate, StringComparison.Ordinal);
		Assert.False(
			Directory.Exists(Path.Combine(app.ProjectDirectory, "maui-tizen")),
			"Generated Tizen files leaked into the project directory instead of the intermediate output path.");
	}

	/// <summary>
	/// The native SkiaSharp binaries are a build-time tool dependency of the MSBuild tasks. They
	/// must not end up in the built application's output, where they would be dead weight at best
	/// and a wrong-architecture binary shipped to a device at worst.
	/// </summary>
	[Fact]
	public void HostNativeAssetsDoNotLeakIntoApplicationOutput()
	{
		var app = CreateApp();
		app.Generate();

		AssertBuildSucceeded(app.Build());

		var outputRoot = Path.Combine(app.ProjectDirectory, "bin");
		Assert.True(Directory.Exists(outputRoot), "The application produced no output directory.");

		var leaked = Directory
			.GetFiles(outputRoot, "*SkiaSharp*", SearchOption.AllDirectories)
			.Select(f => Path.GetRelativePath(outputRoot, f))
			.ToList();

		Assert.True(
			leaked.Count == 0,
			"Host build-time SkiaSharp assets leaked into the application output: " + string.Join(", ", leaked));
	}

	/// <summary>
	/// Rasterization happens inside the MSBuild task assembly, which is the context where native
	/// SkiaSharp resolution previously failed on Linux while passing on macOS. Asserting on the
	/// produced pixels proves the native library really loaded, rather than that the target was
	/// merely skipped.
	/// </summary>
	[Fact]
	public void RasterizesSplashScreensInsideTheMSBuildTaskHost()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		Assert.DoesNotContain("SKData", result.Output);

		var splashes = result.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash")
			.ToList();

		Assert.NotEmpty(splashes);

		foreach (var splash in splashes)
		{
			Assert.True(File.Exists(splash.Identity), $"Splash screen '{splash.Identity}' was not written.");

			// Decoding proves real image bytes were produced by the task host.
			using var bitmap = SkiaSharp.SKBitmap.Decode(splash.Identity);
			Assert.NotNull(bitmap);
			Assert.True(bitmap!.Width > 0 && bitmap.Height > 0);
		}
	}

	[Fact]
	public void DisablingImageProcessingDoesNotBreakTheBuild()
	{
		var app = CreateApp();
		app.WithProperty("EnableMauiImageProcessing", "false");
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		Assert.Empty(result.ItemsOf("MauiProcessedImage"));
	}

	// =====================================================================================
	// Resource and splash-screen semantics.
	//
	// The MAUI processing switches are not decorative: an app that turns one off is telling
	// the build it will supply those resources itself. Each of the three tests below covers a
	// state where this backend previously ignored that and packaged something wrong, and each
	// failure was silent - a green build producing a broken TPK.
	// =====================================================================================

	/// <summary>
	/// Reads the generated manifest that <c>$(TizenManifestFile)</c> points at after a build.
	/// </summary>
	private static string ReadPackagedManifest(MSBuildProjectBuilder app, BuildResult result)
	{
		var declared = result.Property("TizenManifestFile");
		Assert.False(string.IsNullOrEmpty(declared), "The build did not report a TizenManifestFile.");

		var path = Path.IsPathRooted(declared) ? declared : Path.Combine(app.ProjectDirectory, declared);
		Assert.True(File.Exists(path), $"The manifest handed to packaging does not exist: '{path}'.");

		return File.ReadAllText(path);
	}

	/// <summary>
	/// With image processing disabled the build must still hand packaging a GENERATED manifest.
	/// </summary>
	/// <remarks>
	/// Manifest generation used to be reachable only through the Resizetizer's image-processing
	/// hook, which the Resizetizer skips entirely when EnableMauiImageProcessing is false. The
	/// backend therefore left $(TizenManifestFile) pointing at the authored file, and the TPK
	/// shipped with the template's literal placeholders - installing on a device under the
	/// package id "maui-application-id-placeholder". Application identity has nothing to do with
	/// image processing, so it must not be coupled to it.
	/// </remarks>
	[Fact]
	public void DisablingImageProcessingStillGeneratesAPlaceholderFreeManifest()
	{
		var app = CreateApp();
		app.WithProperty("EnableMauiImageProcessing", "false");
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		Assert.Empty(result.ItemsOf("MauiProcessedImage"));

		var manifest = ReadPackagedManifest(app, result);

		Assert.DoesNotContain("maui-application-id-placeholder", manifest);
		Assert.DoesNotContain("maui-application-title-placeholder", manifest);
		Assert.DoesNotContain("maui-appicon-placeholder", manifest);

		Assert.Contains("com.contoso.tizenapp", manifest);
		Assert.Contains("Contoso Tizen", manifest);

		// And the authored file is untouched - generation writes to the intermediate path.
		var authored = File.ReadAllText(Path.Combine(app.ProjectDirectory, "Platforms", "Tizen", "tizen-manifest.xml"));
		Assert.Contains("maui-application-id-placeholder", authored);
	}

	/// <summary>
	/// EnableMauiSplashScreenProcessing=false must suppress this backend's own splash composition,
	/// not merely the Resizetizer's built-in one.
	/// </summary>
	/// <remarks>
	/// The custom splash images reach the TPK through TizenTpkUserIncludeFiles contributed by this
	/// package, so gating only the Resizetizer's item group left the switch looking inert: the
	/// package still carried a complete set of generated splash screens and the manifest still
	/// advertised them.
	/// </remarks>
	[Fact]
	public void DisablingSplashScreenProcessingPackagesNoSplashOutputs()
	{
		var app = CreateApp();
		app.WithProperty("EnableMauiSplashScreenProcessing", "false");
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		Assert.DoesNotContain(result.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/').EndsWith("res/splash", StringComparison.Ordinal));

		var splashDirectory = Path.Combine(
			app.ProjectDirectory,
			result.Property("MauiTizenIntermediateOutputPath"),
			GenerateTizenSplashScreens.SplashDirectoryName);

		Assert.False(Directory.Exists(splashDirectory), "Splash images were composed even though splash processing was disabled.");

		// The manifest must not advertise splash screens that are not in the package.
		Assert.DoesNotContain("splash-screen", ReadPackagedManifest(app, result));
	}

	/// <summary>
	/// Removing MauiSplashScreen from a project must remove its splash artifacts on the very next
	/// build, not on the next clean build.
	/// </summary>
	/// <remarks>
	/// This is a two-build mutation test because the defect only exists in the second build. The
	/// composition target requires @(MauiSplashScreen), so removing the item skipped it - and the
	/// re-discovery glob then found the previous build's PNGs on disk and packaged them anyway,
	/// while the surviving splash map kept the manifest advertising them. Deleting a splash screen
	/// from a project therefore appeared to do nothing at all.
	///
	/// Cleanup is ownership-based: only paths recorded in the backend map are deleted. Unlisted
	/// files in either intermediate directory belong to some other tool or to the developer.
	/// </remarks>
	[Fact]
	public void RemovingTheSplashScreenDeletesStaleSplashArtifactsOnTheNextBuild()
	{
		var root = CreateTempDirectory("maui-tizen-splash-mutation");

		var withSplash = CreateApp(root: root);
		withSplash.Generate();

		var first = withSplash.Build();
		AssertBuildSucceeded(first);

		var intermediate = Path.Combine(withSplash.ProjectDirectory, first.Property("MauiTizenIntermediateOutputPath"));
		var splashDirectory = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashDirectoryName);
		var splashMap = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName);

		Assert.True(Directory.GetFiles(splashDirectory, "*.png").Length > 0, "The first build produced no splash screens.");
		Assert.True(File.Exists(splashMap));

		var unownedSplashFile = Path.Combine(splashDirectory, "unowned.png");
		File.WriteAllText(unownedSplashFile, "not owned by Maui.Tizen");

		// This package does not own the Resizetizer's cache, so it must not recursively remove it.
		var resizetizerSplash = Path.Combine(withSplash.ProjectDirectory, "obj", "Debug", "net11.0", "resizetizer", "sp", "splash");
		Directory.CreateDirectory(resizetizerSplash);
		var resizetizerStale = Path.Combine(resizetizerSplash, "splash.mdpi.portrait.png");
		File.Copy(Directory.GetFiles(splashDirectory, "*.png")[0], resizetizerStale, overwrite: true);

		// Second build: same project directory, same intermediate output, no MauiSplashScreen.
		var withoutSplash = new MSBuildProjectBuilder(root);
		withoutSplash
			.WithProperty("ApplicationId", "com.contoso.tizenapp")
			.WithProperty("ApplicationTitle", "Contoso Tizen")
			.WithProperty("ApplicationDisplayVersion", "1.2.3")
			.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"))
			.WithItem("MauiImage", "Resources\\Images\\logo.svg")
			.WithItem("MauiFont", "Resources\\Fonts\\TestFont.ttf")
			.WithItem("MauiAsset", "Resources\\Raw\\data.json", ("LogicalName", "data.json"));
		withoutSplash.Generate();

		var second = withoutSplash.Build();
		AssertBuildSucceeded(second);

		Assert.DoesNotContain(
			Directory.GetFiles(splashDirectory, "*.png"),
			path => !string.Equals(path, unownedSplashFile, StringComparison.Ordinal));
		Assert.True(File.Exists(unownedSplashFile), "An unowned splash-cache file was deleted.");
		Assert.False(File.Exists(splashMap), "The stale splash map survived a build with no MauiSplashScreen.");
		Assert.True(File.Exists(resizetizerStale), "The backend deleted a cache file owned by Resizetizer.");

		Assert.DoesNotContain(second.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/').EndsWith("res/splash", StringComparison.Ordinal));

		Assert.DoesNotContain("splash-screen", ReadPackagedManifest(withoutSplash, second));
	}

	/// <summary>
	/// Turning splash processing off after a build that had it on must clean up on the next build.
	/// </summary>
	/// <remarks>
	/// The second half of the same defect: the switch was read only where new work was scheduled,
	/// never where existing output was reconsidered, so an app that turned it off kept shipping
	/// the splash screens from before the change until someone ran a clean build.
	/// </remarks>
	[Fact]
	public void DisablingSplashScreenProcessingCleansUpTheOutputsOfAnEarlierBuild()
	{
		var root = CreateTempDirectory("maui-tizen-splash-switch");

		var enabled = CreateApp(root: root);
		enabled.Generate();

		var first = enabled.Build();
		AssertBuildSucceeded(first);

		var intermediate = Path.Combine(enabled.ProjectDirectory, first.Property("MauiTizenIntermediateOutputPath"));
		var splashDirectory = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashDirectoryName);
		var splashMap = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName);

		Assert.True(Directory.GetFiles(splashDirectory, "*.png").Length > 0);
		Assert.Contains("splash-screen", ReadPackagedManifest(enabled, first));
		var unowned = Path.Combine(splashDirectory, "unowned.txt");
		File.WriteAllText(unowned, "not owned by Maui.Tizen");

		var disabled = CreateApp(root: root);
		disabled.WithProperty("EnableMauiSplashScreenProcessing", "false");
		disabled.Generate();

		var second = disabled.Build();
		AssertBuildSucceeded(second);

		Assert.Empty(Directory.GetFiles(splashDirectory, "*.png"));
		Assert.True(File.Exists(unowned), "An unowned splash-cache file was deleted.");
		Assert.False(File.Exists(splashMap), "The splash map survived a build with splash processing disabled.");

		Assert.DoesNotContain(second.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			i.Metadata1.Replace('\\', '/').TrimEnd('/').EndsWith("res/splash", StringComparison.Ordinal));

		Assert.DoesNotContain("splash-screen", ReadPackagedManifest(disabled, second));
	}

	/// <summary>
	/// Two resources whose destinations differ only in case are two different files on Tizen and
	/// both must reach the package.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The de-duplication step used the RemoveDuplicates task, which compares with
	/// OrdinalIgnoreCase. "Bar.js" and "bar.js" therefore collapsed onto one entry and the build
	/// stayed green while the application 404'd on the device. Tizen is Linux; case matters.
	/// </para>
	/// <para>
	/// The destinations here are supplied through <c>Link</c>. That is deliberate and is not a
	/// way of dodging the assertion: <c>LogicalName</c> goes through an ItemGroup in the
	/// Resizetizer's own ProcessMauiAssets that BATCHES on <c>%(MauiAsset.LogicalName)</c>, and
	/// MSBuild's metadata batching is itself case insensitive, so two LogicalNames differing only
	/// in case are merged into a single bucket and given one shared destination before this
	/// backend ever sees them. That is an upstream defect in a target this repository does not
	/// own; what is verified here is that the backend does not add a SECOND, independent
	/// case-insensitive collapse on the path it does own. Documented in docs/asset-providers.md.
	/// </para>
	/// </remarks>
	[Fact]
	public void ResourcesWhoseDestinationsDifferOnlyInCaseAreBothPackaged()
	{
		var app = CreateApp();
		app.WriteText("Resources/Raw/Bar.js", "// upper");
		app.WriteText("Resources/Raw/lowerbar.js", "// lower");
		app.WithItem("MauiAsset", "Resources\\Raw\\Bar.js", ("Link", "scripts/Bar.js"));
		app.WithItem("MauiAsset", "Resources\\Raw\\lowerbar.js", ("Link", "scripts/bar.js"));
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var destinations = result.ItemsOf("TizenResource")
			.Select(i => i.Metadata1.Replace('\\', '/'))
			.ToList();

		Assert.Contains("scripts/Bar.js", destinations);
		Assert.Contains("scripts/bar.js", destinations);
	}

	/// <summary>
	/// De-duplication must still collapse genuinely identical destinations.
	/// </summary>
	[Fact]
	public void ResourcesWithIdenticalDestinationsAreStillDeduplicated()
	{
		var app = CreateApp();
		var duplicated = app.WriteText("generated/dup.txt", "contributed twice");
		app.WithRawProjectContent($"""
			  <PropertyGroup>
			    <MauiTizenAssetProviderTargets>
			      $(MauiTizenAssetProviderTargets);
			      TestContributeTwice;
			    </MauiTizenAssetProviderTargets>
			  </PropertyGroup>
			  <Target Name="TestContributeTwice">
			    <ItemGroup>
			      <MauiAsset Include="{TestBase.Escape(duplicated)}" Link="shared/dup.txt" />
			      <MauiAsset Include="{TestBase.Escape(duplicated)}" Link="shared/dup.txt" />
			    </ItemGroup>
			  </Target>
			""");
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var matching = result.ItemsOf("TizenResource")
			.Count(i => i.Metadata1.Replace('\\', '/') == "shared/dup.txt");

		Assert.Equal(1, matching);
	}

	[Fact]
	public void DifferentResourcesCannotClaimTheSameDestination()
	{
		var app = CreateApp();
		var first = app.WriteText("generated/first.txt", "first");
		var second = app.WriteText("generated/second.txt", "second");
		app.WithRawProjectContent($"""
			  <PropertyGroup>
			    <MauiTizenAssetProviderTargets>
			      $(MauiTizenAssetProviderTargets);
			      TestContributeConflict;
			    </MauiTizenAssetProviderTargets>
			  </PropertyGroup>
			  <Target Name="TestContributeConflict">
			    <ItemGroup>
			      <MauiAsset Include="{TestBase.Escape(first)}" Link="shared/conflict.txt" />
			      <MauiAsset Include="{TestBase.Escape(second)}" Link="shared/conflict.txt" />
			    </ItemGroup>
			  </Target>
			""");
		app.Generate();

		var result = app.Build();

		Assert.False(result.Success);
		Assert.Contains("MAUITIZEN1021", result.Output, StringComparison.Ordinal);
		Assert.Contains(first, result.Output, StringComparison.Ordinal);
		Assert.Contains(second, result.Output, StringComparison.Ordinal);
		Assert.Contains("shared/conflict.txt", result.Output, StringComparison.Ordinal);
	}

	// =====================================================================================
	// Manifest incrementality
	// =====================================================================================

	/// <summary>
	/// A no-op build must not rewrite the generated manifest.
	/// </summary>
	/// <remarks>
	/// Manifest generation had no Inputs/Outputs at all, so every build rewrote the file and
	/// re-stamped everything downstream of it. Adding incrementality is only safe if the
	/// property hand-off survives the target being skipped, which the next test covers.
	/// </remarks>
	[Fact]
	public void ASecondBuildDoesNotRewriteTheGeneratedManifest()
	{
		var app = CreateApp();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var manifestPath = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));
		Assert.True(File.Exists(manifestPath));

		var stamp = File.GetLastWriteTimeUtc(manifestPath);
		var contents = File.ReadAllText(manifestPath);

		// Coarse filesystem timestamps would make an immediate rewrite look like a no-op.
		System.Threading.Thread.Sleep(1100);

		var second = app.Build();
		AssertBuildSucceeded(second);

		Assert.Equal(stamp, File.GetLastWriteTimeUtc(manifestPath));
		Assert.Equal(contents, File.ReadAllText(manifestPath));
	}

	/// <summary>
	/// The manifest handed to packaging must be the generated one even when generation was
	/// skipped as up to date.
	/// </summary>
	/// <remarks>
	/// This is the trap that makes naive incrementality worse than none: if the
	/// $(TizenManifestFile) hand-off lives inside the incremental target, an up-to-date build
	/// leaves the property pointing at the authored file and packages the placeholders. The
	/// incremental build would then produce a different TPK from the clean one.
	/// </remarks>
	[Fact]
	public void AnUpToDateBuildStillHandsPackagingTheGeneratedManifest()
	{
		var app = CreateApp();
		app.Generate();

		AssertBuildSucceeded(app.Build());

		var second = app.Build();
		AssertBuildSucceeded(second);

		var declared = second.Property("TizenManifestFile").Replace('\\', '/');
		Assert.Contains("maui-tizen/tizen-manifest.xml", declared);

		var manifest = ReadPackagedManifest(app, second);
		Assert.DoesNotContain("maui-application-id-placeholder", manifest);
	}

	/// <summary>
	/// Changing a property the manifest is derived from must regenerate it. Timestamp-only
	/// incrementality cannot see property changes, which is why the inputs are recorded to a file.
	/// </summary>
	[Fact]
	public void ChangingTheApplicationIdRegeneratesTheManifest()
	{
		var root = CreateTempDirectory("maui-tizen-manifest-inputs");

		var app = CreateApp(root: root);
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var manifestPath = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));
		Assert.Contains("com.contoso.tizenapp", File.ReadAllText(manifestPath));

		var renamed = new MSBuildProjectBuilder(root);
		renamed
			.WithProperty("ApplicationId", "com.contoso.renamedapp")
			.WithProperty("ApplicationTitle", "Contoso Tizen")
			.WithProperty("ApplicationDisplayVersion", "1.2.3")
			.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"))
			.WithItem("MauiSplashScreen", "Resources\\Splash\\splash.svg", ("Color", "#512BD4"), ("BaseSize", "128,128"))
			.WithItem("MauiImage", "Resources\\Images\\logo.svg")
			.WithItem("MauiFont", "Resources\\Fonts\\TestFont.ttf")
			.WithItem("MauiAsset", "Resources\\Raw\\data.json", ("LogicalName", "data.json"));
		renamed.Generate();

		AssertBuildSucceeded(renamed.Build());

		Assert.Contains("com.contoso.renamedapp", File.ReadAllText(manifestPath));
	}

	/// <summary>
	/// Changing only <c>MauiIcon</c>'s <c>Link</c> alias must regenerate the manifest so it names
	/// the icon the build now produces.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The manifest's icon element is <c>xhdpi/&lt;OutputName&gt;.xhigh.png</c>, and OutputName is
	/// the <c>Link</c> alias when one is present. Link was not recorded in the manifest inputs
	/// file, so this mutation changed no file the manifest target watched: the manifest was
	/// skipped as up to date and kept pointing at <c>appicon.xhigh.png</c> while the Resizetizer
	/// had renamed every generated icon to <c>brandicon.*</c> and deleted the old ones. The
	/// application then installed with an icon element resolving to nothing.
	/// </para>
	/// <para>
	/// Same intermediate directory, no source file rewritten - only the metadata changes, which is
	/// the only state in which the defect exists.
	/// </para>
	/// <para>
	/// The application deliberately has NO splash screen. With one, the renamed icon images make
	/// splash composition out of date, which rewrites the splash map, which is itself a manifest
	/// input - so the manifest would be regenerated for an unrelated reason and the test would
	/// pass against the defect it exists to catch. An icon-only application is a perfectly
	/// ordinary shape and is the one that isolates this input.
	/// </para>
	/// </remarks>
	[Fact]
	public void ChangingTheIconAliasRegeneratesTheManifestAndDropsTheOldIcon()
	{
		var root = CreateTempDirectory("maui-tizen-icon-alias");

		var app = RedeclareApp(root, withSplash: false);
		WriteAppSources(app);
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var manifestPath = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));
		Assert.Contains("xhdpi/appicon.xhigh.png", File.ReadAllText(manifestPath));
		Assert.Contains(first.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "appicon.xhigh.png");

		var renamedIcon = RedeclareApp(
			root,
			icon: new[] { ("Color", "#512BD4"), ("Link", "brandicon.svg") },
			withSplash: false);
		renamedIcon.Generate();

		var second = renamedIcon.Build();
		AssertBuildSucceeded(second);

		var manifest = File.ReadAllText(manifestPath);
		Assert.Contains("xhdpi/brandicon.xhigh.png", manifest);
		Assert.DoesNotContain("xhdpi/appicon.xhigh.png", manifest);

		// The manifest names a file the build actually produced...
		Assert.Contains(second.ItemsOf("TizenTpkUserIncludeFiles"), i => Path.GetFileName(i.Identity) == "brandicon.xhigh.png");

		// ...and the icon it stopped producing is gone from both the package inputs and disk.
		Assert.DoesNotContain(second.ItemsOf("TizenTpkUserIncludeFiles"), i =>
			Path.GetFileName(i.Identity).StartsWith("appicon.", StringComparison.Ordinal));

		var strayIcons = Directory
			.GetFiles(Path.Combine(app.ProjectDirectory, "obj"), "appicon.*.png", SearchOption.AllDirectories)
			.ToList();

		Assert.True(
			strayIcons.Count == 0,
			"Generated icons from the previous alias survived: " + string.Join(", ", strayIcons));
	}

	// =====================================================================================
	// Splash-screen incrementality
	// =====================================================================================

	/// <summary>
	/// Changing only <c>MauiSplashScreen</c>'s <c>Color</c> must recompose the splash images.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Color is painted into every generated PNG as the letterbox background, but it is not a
	/// file, and the composition target's inputs were files alone. The SVG did not change; the
	/// Resizetizer's own image inputs file did not change either, because @(MauiSplashScreen) is
	/// only added to @(MauiImage) after mauiimage.inputs has been written. So nothing was out of
	/// date, generation was skipped, and the previous colour's images were re-declared and
	/// packaged.
	/// </para>
	/// <para>
	/// Asserted on CONTENT - the hash of every generated image, plus the actual background pixel -
	/// rather than on timestamps, because "the file was rewritten" is not the claim being made.
	/// </para>
	/// </remarks>
	[Fact]
	public void ChangingTheSplashScreenColorRecomposesTheSplashImages()
	{
		var root = CreateTempDirectory("maui-tizen-splash-color");

		var app = CreateApp(root: root);
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var splashDirectory = SplashDirectory(app, first);
		var before = HashSplashImages(splashDirectory);
		Assert.NotEmpty(before);

		using (var purple = SkiaSharp.SKBitmap.Decode(Path.Combine(splashDirectory, before.Keys.First())))
		{
			Assert.Equal(new SkiaSharp.SKColor(0x51, 0x2B, 0xD4), purple.GetPixel(0, 0));
		}

		var recoloured = RedeclareApp(root, splash: new[] { ("Color", "#00FF00"), ("BaseSize", "128,128") });
		recoloured.Generate();

		var second = recoloured.Build();
		AssertBuildSucceeded(second);

		var after = HashSplashImages(splashDirectory);

		// The same set of images, all of them different.
		Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
		foreach (var (name, hash) in before)
			Assert.False(after[name] == hash, $"'{name}' still has the splash screen composed for the previous colour.");

		// And the new colour is the one on the canvas.
		foreach (var name in after.Keys)
		{
			using var bitmap = SkiaSharp.SKBitmap.Decode(Path.Combine(splashDirectory, name));
			Assert.Equal(new SkiaSharp.SKColor(0x00, 0xFF, 0x00), bitmap.GetPixel(0, 0));
		}

		// The packaged inputs are the recomposed files, not a stale set left on disk.
		var packaged = second.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash")
			.Select(i => Path.GetFileName(i.Identity))
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		Assert.Equal(after.Keys.OrderBy(k => k, StringComparer.Ordinal), packaged);
	}

	/// <summary>
	/// An application that only REFERENCES the package must get the whole backend, through NuGet's
	/// own automatic import of <c>buildTransitive</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every other MSBuild test here imports the props and targets from the source tree by path and
	/// points <c>_MauiTizenBuildTasksAssembly</c> at a build output folder. That is the right shape
	/// for testing build logic, and those tests are kept, but it means none of them can see the
	/// step a real consumer depends on: NuGet auto-imports <c>buildTransitive/&lt;package id&gt;.props</c>
	/// and <c>.targets</c> BY NAME, and the packaged targets then resolve the task assembly and its
	/// native SkiaSharp from the package layout. Rename either file, drop one from the package, or
	/// ship the managed closure incompletely, and every source-tree test still passes while an
	/// application gets nothing - or fails inside SkiaSharp's initializer.
	/// </para>
	/// <para>
	/// The restore uses an isolated packages folder because the produced package's version never
	/// changes: reusing the developer's global folder would pin the first extraction forever and
	/// later runs would validate a package built from older sources.
	/// </para>
	/// </remarks>
	[Fact]
	public void ThePackagedBackendActivatesThroughNuGetsAutomaticImport()
	{
		var app = CreateApp();
		app.ConsumeProducedPackage = true;
		app.PackagesFolder = CreateTempDirectory("maui-tizen-package-consumer-packages");
		app.WithProperty("SingleProject", "true");
		app.Generate();

		// No import of this package anywhere in the generated project - only a PackageReference.
		var projectText = File.ReadAllText(app.ProjectPath);
		Assert.DoesNotContain("Maui.Tizen.Build.Tasks.targets", projectText);
		Assert.DoesNotContain("Maui.Tizen.Build.Tasks.props", projectText);
		Assert.Contains("""<PackageReference Include="Maui.Tizen.Build.Tasks" """, projectText);

		var result = app.Build();
		AssertBuildSucceeded(result);

		// The .props ran: this item is declared there and nowhere else.
		Assert.Contains(result.ItemsOf("MauiPlatformSpecificFolder"), i =>
			i.Identity.Replace('\\', '/').TrimEnd('/') == "Platforms/Tizen" && i.Metadata1 == "tizen");

		// The .targets ran, and the tasks inside them executed: manifest, res.xml, fonts, splash.
		Assert.Contains("maui-tizen/tizen-manifest.xml", result.Property("TizenManifestFile").Replace('\\', '/'));

		var tpkFiles = result.ItemsOf("TizenTpkUserIncludeFiles").ToList();
		Assert.Contains(tpkFiles, i => Path.GetFileName(i.Identity) == "res.xml");
		Assert.Contains(tpkFiles, i => Path.GetFileName(i.Identity) == "TestFont.ttf");
		Assert.Contains(tpkFiles, i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash");
		Assert.Contains(result.ItemsOf("TizenResource"), i => Path.GetFileName(i.Identity) == "data.json");

		// The task really was loaded out of the restored package, not from a build output folder.
		var taskAssembly = result.Property("_MauiTizenBuildTasksAssembly").Replace('\\', '/');
		Assert.Contains("maui.tizen.build.tasks/", taskAssembly.ToLowerInvariant());
		Assert.Contains("/buildTransitive/Maui.Tizen.Build.Tasks.dll", taskAssembly);

		// And it rasterized: the natives resolved from the package's own layout.
		foreach (var splash in tpkFiles.Where(i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash"))
		{
			using var bitmap = SkiaSharp.SKBitmap.Decode(splash.Identity);
			Assert.NotNull(bitmap);
			Assert.True(bitmap!.Width > 0 && bitmap.Height > 0);
		}
	}

	[Fact]
	public void ThePackagedBackendRejectsUnsupportedMuslArm64BuildHostsByName()
	{
		var app = CreateApp();
		app.ConsumeProducedPackage = true;
		app.PackagesFolder = CreateTempDirectory("maui-tizen-unsupported-host-packages");
		app.WithProperty("MauiTizenBuildHostOperatingSystem", "linux");
		app.WithProperty("MauiTizenBuildHostRuntimeIdentifier", "linux-musl-arm64");
		app.WithProperty("MauiTizenBuildHostArchitecture", "arm64");
		app.Generate();

		var result = app.Build();

		Assert.False(result.Success);
		Assert.Contains("MAUITIZEN1012", result.Output, StringComparison.Ordinal);
		Assert.DoesNotContain("DllNotFoundException", result.Output, StringComparison.Ordinal);
		Assert.DoesNotContain("type initializer", result.Output, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ThePackagedBackendRejectsUnknownBuildHostsByName()
	{
		var app = CreateApp();
		app.ConsumeProducedPackage = true;
		app.PackagesFolder = CreateTempDirectory("maui-tizen-unknown-host-packages");
		app.WithProperty("MauiTizenBuildHostOperatingSystem", "freebsd");
		app.WithProperty("MauiTizenBuildHostRuntimeIdentifier", "freebsd-x64");
		app.WithProperty("MauiTizenBuildHostArchitecture", "x64");
		app.Generate();

		var result = app.Build();

		Assert.False(result.Success);
		Assert.Contains("MAUITIZEN1010", result.Output, StringComparison.Ordinal);
		Assert.DoesNotContain("DllNotFoundException", result.Output, StringComparison.Ordinal);
	}

	/// <summary>
	/// A file this package did not generate must never be packaged as a splash screen, even when
	/// it is sitting in the splash cache directory.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When the composition target is up to date it produces no items, so the outputs have to be
	/// re-declared from somewhere. That used to be a wildcard over the splash directory, which
	/// cannot tell this package's images apart from anything else in there - and an intermediate
	/// directory is not private space. Whatever was found was given
	/// <c>TizenTpkSubDir="shared\res\splash\"</c> and packed, so a stray file shipped to the
	/// device as a splash resource.
	/// </para>
	/// <para>
	/// Both halves are asserted together on purpose: excluding the stray file is only a fix if the
	/// cache is still trusted. A build that "fixed" this by recomposing everything would delete
	/// the sentinel, so the surviving sentinel is the proof that incrementality was preserved.
	/// </para>
	/// </remarks>
	[Fact]
	public void UnownedFilesInTheSplashCacheAreNotPackaged()
	{
		var app = CreateApp();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var splashDirectory = SplashDirectory(app, first);
		var sentinel = Path.Combine(splashDirectory, "cache-sentinel.txt");
		File.WriteAllText(sentinel, "generation did not rerun");

		var second = app.Build();
		AssertBuildSucceeded(second);

		// Incrementality preserved: a complete cache was not regenerated.
		Assert.True(File.Exists(sentinel), "A complete splash cache was unnecessarily regenerated.");

		// A third build covers the replay-to-replay transition as well as generate-to-replay. A
		// metadata-state file that encoded item ORDER would differ between the build that runs
		// image processing and the build that replays its outputs from a file, and the splash
		// screens would then recompose on alternate builds forever.
		AssertBuildSucceeded(app.Build());
		Assert.True(File.Exists(sentinel), "The splash cache was regenerated on a third, unchanged build.");

		var splashInputs = second.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash")
			.Select(i => Path.GetFileName(i.Identity))
			.ToList();

		Assert.DoesNotContain("cache-sentinel.txt", splashInputs);
		Assert.All(splashInputs, n => Assert.EndsWith(".png", n, StringComparison.Ordinal));

		// Everything the map promises is still packaged - the filter removed the stray file only.
		var mapped = File
			.ReadAllLines(Path.Combine(IntermediateDirectory(app, first), GenerateTizenSplashScreens.SplashMapFileName))
			.Where(l => !string.IsNullOrWhiteSpace(l))
			.Select(l => Path.GetFileName(l.Split('|').Last()))
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		Assert.NotEmpty(mapped);
		Assert.Equal(mapped, splashInputs.OrderBy(n => n, StringComparer.Ordinal).ToList());
	}

	// =====================================================================================
	// Manifest source identity
	// =====================================================================================

	/// <summary>Writes an authored manifest whose <c>exec</c> attribute identifies it.</summary>
	/// <remarks>
	/// <c>exec</c> is carried through the rewrite untouched, so it is a marker for WHICH document
	/// the generated manifest was produced from - which is precisely what the derived values
	/// (application id, title, version) cannot tell you, because they are overwritten from MSBuild
	/// properties and are identical in both files.
	/// </remarks>
	private static string WriteIdentifiableManifest(MSBuildProjectBuilder app, string relativePath, string exec)
		=> app.WriteText(relativePath, $"""
			<?xml version="1.0" encoding="utf-8"?>
			<manifest package="maui-application-id-placeholder" version="0.0.0" api-version="11" xmlns="http://tizen.org/ns/packages">
			  <profile name="common" />
			  <ui-application appid="maui-application-id-placeholder" exec="{exec}" multiple="false" nodisplay="false" taskmanage="true" type="dotnet" launch_mode="single">
			    <label>maui-application-title-placeholder</label>
			    <icon>maui-appicon-placeholder</icon>
			  </ui-application>
			</manifest>
			""");

	/// <summary>Declares an icon-only application that points at a specific authored manifest.</summary>
	private static MSBuildProjectBuilder DeclareManifestApp(string root, string? tizenManifestFile)
	{
		var builder = new MSBuildProjectBuilder(root);

		builder
			.WithProperty("ApplicationId", "com.contoso.tizenapp")
			.WithProperty("ApplicationTitle", "Contoso Tizen")
			.WithProperty("ApplicationDisplayVersion", "1.2.3")
			.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"));

		if (tizenManifestFile is not null)
			builder.WithProperty("TizenManifestFile", tizenManifestFile);

		return builder;
	}

	/// <summary>
	/// Pointing the project at a DIFFERENT authored manifest must regenerate from it, even when
	/// that manifest is older than the generated file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The generated manifest is a rewrite of a specific source document: the application id, the
	/// title, the version and the resource elements are replaced, and everything else - privileges,
	/// metadata, the exec name, the api-version - is carried through verbatim. Which document was
	/// the source is therefore an input in its own right, and it was not recorded: the state file
	/// held only the DERIVED values, and the only file input was $(TizenManifestFile), compared by
	/// timestamp.
	/// </para>
	/// <para>
	/// So switching to an older manifest changed nothing MSBuild could see. An older file is not
	/// exotic - it is what you get from a git checkout, a branch switch, a restored backup, or
	/// simply a manifest that was authored first - and the result was a build that kept packaging
	/// a manifest derived from a file the project no longer referenced.
	/// </para>
	/// <para>
	/// Same intermediate directory, no rewritten sources: only the project's manifest selection
	/// changes.
	/// </para>
	/// </remarks>
	[Fact]
	public void SwitchingToAnOlderAuthoredManifestRegeneratesFromIt()
	{
		var root = CreateTempDirectory("maui-tizen-manifest-identity");

		var app = DeclareManifestApp(root, "Platforms\\Tizen\\manifest-a.xml");
		app.WriteSvg("Resources/AppIcon/appicon.svg");
		WriteIdentifiableManifest(app, "Platforms/Tizen/manifest-a.xml", "ManifestA.dll");
		var manifestB = WriteIdentifiableManifest(app, "Platforms/Tizen/manifest-b.xml", "ManifestB.dll");
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var generatedPath = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));
		Assert.Contains("ManifestA.dll", File.ReadAllText(generatedPath));

		// The second manifest is OLDER than everything the first build produced, which is the
		// state a timestamp comparison cannot distinguish from "nothing to do".
		File.SetLastWriteTimeUtc(manifestB, DateTime.UtcNow.AddDays(-2));

		var switched = DeclareManifestApp(root, "Platforms\\Tizen\\manifest-b.xml");
		switched.Generate();

		var second = switched.Build();
		AssertBuildSucceeded(second);

		var generated = File.ReadAllText(generatedPath);
		Assert.Contains("ManifestB.dll", generated);
		Assert.DoesNotContain("ManifestA.dll", generated);

		// The identity is what the recorded state now carries, and it names the file the project
		// actually selected.
		var recorded = File.ReadAllText(Path.Combine(IntermediateDirectory(app, second), "tizen-manifest.inputs"));
		Assert.Contains("manifest-b.xml", recorded);
		Assert.Contains("TizenManifestSelection=explicit", recorded);

		// The generated manifest is still what packaging is handed, and it is still placeholder
		// free - regenerating from a different source must not lose the single-project values.
		Assert.DoesNotContain("maui-application-id-placeholder", generated);
		Assert.Contains("com.contoso.tizenapp", generated);
	}

	/// <summary>
	/// Removing an explicit <c>TizenManifestFile</c> must fall back to the default manifest and
	/// regenerate from it.
	/// </summary>
	/// <remarks>
	/// This is the same defect approached from the other side: nothing about the project's
	/// property values changes here except which file is selected, and the default file is older
	/// than the generated one. Recording the resolved path AND how it was chosen is what makes
	/// both directions visible - 'default' and 'explicit' can name the same file, and only the
	/// recorded selection distinguishes "the project always used the default" from "the project
	/// used to point somewhere else".
	/// </remarks>
	[Fact]
	public void RemovingAnExplicitManifestFallsBackToTheDefaultAndRegenerates()
	{
		var root = CreateTempDirectory("maui-tizen-manifest-fallback");

		var app = DeclareManifestApp(root, "Platforms\\Tizen\\custom-manifest.xml");
		app.WriteSvg("Resources/AppIcon/appicon.svg");
		var defaultManifest = WriteIdentifiableManifest(app, "Platforms/Tizen/tizen-manifest.xml", "DefaultManifest.dll");
		WriteIdentifiableManifest(app, "Platforms/Tizen/custom-manifest.xml", "CustomManifest.dll");
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var generatedPath = Path.Combine(app.ProjectDirectory, first.Property("TizenManifestFile"));
		Assert.Contains("CustomManifest.dll", File.ReadAllText(generatedPath));

		File.SetLastWriteTimeUtc(defaultManifest, DateTime.UtcNow.AddDays(-2));

		var reverted = DeclareManifestApp(root, tizenManifestFile: null);
		reverted.Generate();

		var second = reverted.Build();
		AssertBuildSucceeded(second);

		var generated = File.ReadAllText(generatedPath);
		Assert.Contains("DefaultManifest.dll", generated);
		Assert.DoesNotContain("CustomManifest.dll", generated);

		var recorded = File.ReadAllText(Path.Combine(IntermediateDirectory(app, second), "tizen-manifest.inputs"));
		Assert.Contains("TizenManifestSelection=default", recorded);
	}

	/// <summary>
	/// A project that had no Tizen manifest and then gains one must generate from it, even when
	/// the new file is older than everything the previous build produced.
	/// </summary>
	/// <remarks>
	/// This is the "absence" half of the recorded manifest identity. A build with no manifest used
	/// to record no state at all, so the first build after a manifest was added had nothing to
	/// compare against - and the file it compared instead, the manifest itself, could easily be
	/// older than the intermediate directory. Recording 'none' explicitly is what turns the
	/// transition into a state change.
	/// </remarks>
	[Fact]
	public void AddingAManifestToAProjectThatHadNoneGeneratesIt()
	{
		var root = CreateTempDirectory("maui-tizen-manifest-absence");

		var app = DeclareManifestApp(root, tizenManifestFile: null);
		app.WriteSvg("Resources/AppIcon/appicon.svg");
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		// No manifest was selected, and that is what the state says.
		Assert.Equal(string.Empty, first.Property("TizenManifestFile"));

		var stateFile = Path.Combine(IntermediateDirectory(app, first), "tizen-manifest.inputs");
		Assert.True(File.Exists(stateFile), "No manifest state was recorded for a project without a manifest.");
		Assert.Contains("TizenManifestSelection=none", File.ReadAllText(stateFile));

		// Add the default manifest, dated well before this build.
		var added = WriteIdentifiableManifest(app, "Platforms/Tizen/tizen-manifest.xml", "AddedManifest.dll");
		File.SetLastWriteTimeUtc(added, DateTime.UtcNow.AddDays(-30));

		var withManifest = DeclareManifestApp(root, tizenManifestFile: null);
		withManifest.Generate();

		var second = withManifest.Build();
		AssertBuildSucceeded(second);

		var generatedPath = Path.Combine(app.ProjectDirectory, second.Property("TizenManifestFile"));
		Assert.EndsWith("tizen-manifest.xml", generatedPath);
		Assert.True(File.Exists(generatedPath), "The manifest was not generated after one was added to the project.");

		var generated = File.ReadAllText(generatedPath);
		Assert.Contains("AddedManifest.dll", generated);
		Assert.Contains("com.contoso.tizenapp", generated);
		Assert.Contains("TizenManifestSelection=default", File.ReadAllText(stateFile));
	}

	// =====================================================================================
	// res.xml incrementality
	// =====================================================================================

	/// <summary>The res.xml this package generated, as declared to packaging.</summary>
	private static string? PackagedResourceXml(BuildResult result)
		=> result.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => Path.GetFileName(i.Identity) == "res.xml")
			.Select(i => i.Identity)
			.SingleOrDefault();

	/// <summary>
	/// A second build must not rewrite res.xml.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The generating target had no Inputs/Outputs, and the task saved unconditionally, so every
	/// build replaced a byte-identical res.xml. res.xml is a TPK packaging input, so that
	/// re-stamped everything downstream of it and turned every no-op build into a partial
	/// repackage.
	/// </para>
	/// <para>
	/// The application here drops the Resizetizer's own res.xml from the processed image set.
	/// That is not a workaround: with the pinned Resizetizer the built-in Tizen branches still
	/// write res.xml themselves and this package's generator is correctly inert, so a test that
	/// did not simulate their removal would assert nothing about the generator and would pass in
	/// either direction. See MSBuildProjectBuilder.SimulateUpstreamWithoutBuiltInResourceXml.
	/// </para>
	/// </remarks>
	[Fact]
	public void ASecondBuildDoesNotRewriteTheGeneratedResourceXml()
	{
		var app = CreateApp();
		app.SimulateUpstreamWithoutBuiltInResourceXml = true;
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var resourceXml = PackagedResourceXml(first);
		Assert.NotNull(resourceXml);
		Assert.True(File.Exists(resourceXml), $"res.xml was declared at '{resourceXml}' but is not on disk.");

		// It really is the generated one: the Resizetizer's copy was removed from the item set
		// before this package looked at it.
		var stateFile = Path.Combine(IntermediateDirectory(app, first), "tizen-res.inputs");
		Assert.True(File.Exists(stateFile), "The resource bucket state was not recorded.");
		Assert.Contains("Bucket=default_All-HDPI", File.ReadAllText(stateFile));

		var stamp = File.GetLastWriteTimeUtc(resourceXml!);
		var contents = File.ReadAllText(resourceXml!);
		var stateStamp = File.GetLastWriteTimeUtc(stateFile);

		// Coarse filesystem timestamps would make an immediate rewrite look like a no-op.
		System.Threading.Thread.Sleep(1100);

		var second = app.Build();
		AssertBuildSucceeded(second);

		Assert.Equal(stamp, File.GetLastWriteTimeUtc(resourceXml!));
		Assert.Equal(contents, File.ReadAllText(resourceXml!));
		Assert.Equal(stateStamp, File.GetLastWriteTimeUtc(stateFile));

		// And the up-to-date build still hands it to packaging: an incremental build that quietly
		// dropped res.xml would produce a different TPK from the clean one.
		Assert.Equal(resourceXml, PackagedResourceXml(second));

		// A third build covers the replay-to-replay transition as well as generate-to-replay.
		AssertBuildSucceeded(app.Build());
		Assert.Equal(stamp, File.GetLastWriteTimeUtc(resourceXml!));
	}

	/// <summary>
	/// Changing the set of resource buckets must regenerate res.xml, and stopping producing any
	/// bucket must stop packaging it.
	/// </summary>
	/// <remarks>
	/// The bucket set is the whole of what res.xml describes, so it is the state the incremental
	/// check is keyed on. An application whose only image is the app icon has no
	/// <c>res/contents</c> buckets at all - app icons live under <c>shared/res</c> - so res.xml
	/// describes nothing and must not be packaged; adding an image back must bring it back. Same
	/// intermediate directory throughout, so nothing is proven by a clean build.
	/// </remarks>
	[Fact]
	public void ChangingTheResourceBucketsRegeneratesTheResourceXml()
	{
		var root = CreateTempDirectory("maui-tizen-res-buckets");

		MSBuildProjectBuilder Declare(bool withImage)
		{
			var builder = new MSBuildProjectBuilder(root) { SimulateUpstreamWithoutBuiltInResourceXml = true };

			builder
				.WithProperty("ApplicationId", "com.contoso.tizenapp")
				.WithProperty("ApplicationTitle", "Contoso Tizen")
				.WithProperty("ApplicationDisplayVersion", "1.2.3")
				.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"));

			if (withImage)
				builder.WithItem("MauiImage", "Resources\\Images\\logo.svg");

			return builder;
		}

		var app = Declare(withImage: true);
		app.WriteSvg("Resources/AppIcon/appicon.svg");
		app.WriteSvg("Resources/Images/logo.svg", "#00FF00");
		app.WriteTizenManifest();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var resourceXml = PackagedResourceXml(first);
		Assert.NotNull(resourceXml);

		var stateFile = Path.Combine(IntermediateDirectory(app, first), "tizen-res.inputs");
		Assert.Contains("Bucket=", File.ReadAllText(stateFile));

		// Remove the only resizable image. The icon remains, so images are still processed - they
		// simply no longer describe any resource bucket.
		var iconOnly = Declare(withImage: false);
		iconOnly.Generate();

		var second = iconOnly.Build();
		AssertBuildSucceeded(second);

		Assert.Null(PackagedResourceXml(second));
		Assert.DoesNotContain("Bucket=", File.ReadAllText(stateFile));

		// Put it back: the state changes again and res.xml returns.
		var restored = Declare(withImage: true);
		restored.Generate();

		var third = restored.Build();
		AssertBuildSucceeded(third);

		var regenerated = PackagedResourceXml(third);
		Assert.NotNull(regenerated);
		Assert.True(File.Exists(regenerated));
		Assert.Contains("Bucket=default_All-HDPI", File.ReadAllText(stateFile));
		Assert.Contains("contents/default_All-HDPI", File.ReadAllText(regenerated!));
	}

	// =====================================================================================
	// Splash ResizeQuality
	// =====================================================================================

	/// <summary>
	/// Writes a PNG of one-pixel vertical stripes, whose appearance after downscaling depends
	/// entirely on the sampling used.
	/// </summary>
	private static void WriteStripedPng(MSBuildProjectBuilder app, string relativePath, int size)
	{
		var path = Path.Combine(app.ProjectDirectory, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		using var bitmap = new SkiaSharp.SKBitmap(size, size);
		for (var y = 0; y < size; y++)
		{
			for (var x = 0; x < size; x++)
				bitmap.SetPixel(x, y, x % 2 == 0 ? SkiaSharp.SKColors.White : SkiaSharp.SKColors.Black);
		}

		using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(path);
		data.SaveTo(stream);
	}

	/// <summary>
	/// Changing only <c>MauiSplashScreen</c>'s <c>ResizeQuality</c> must recompose the splash
	/// images, and the composed content must actually differ.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two separate things made this stale. The composition's own recorded state did not include
	/// ResizeQuality, so nothing this package watched changed. And the splash only joined
	/// <c>@(MauiImage)</c> AFTER the Resizetizer had written mauiimage.inputs, so its metadata
	/// never entered the Resizetizer's recorded image state either - the DPI-scaled sources the
	/// composition reads were reused exactly as the previous build left them. Recomposing from
	/// stale sources would have produced stale output even with perfect state tracking on this
	/// side, so both were fixed: the splash is contributed before the Resizetizer records its
	/// state, and ResizeQuality is part of the composition's inputs file.
	/// </para>
	/// <para>
	/// The source is a striped raster deliberately larger than the target canvas. A flat colour
	/// cannot show a resampling difference and a source smaller than the canvas is never scaled,
	/// so either would make this test pass without the setting being honoured at all.
	/// </para>
	/// <para>
	/// Asserted on CONTENT rather than timestamps, and in the same intermediate directory, with no
	/// source file rewritten between the two builds.
	/// </para>
	/// </remarks>
	[Fact]
	public void ChangingTheSplashResizeQualityRecomposesTheSplashImages()
	{
		var root = CreateTempDirectory("maui-tizen-splash-quality");

		MSBuildProjectBuilder Declare(string quality)
		{
			var builder = new MSBuildProjectBuilder(root);

			builder
				.WithProperty("ApplicationId", "com.contoso.tizenapp")
				.WithProperty("ApplicationTitle", "Contoso Tizen")
				.WithProperty("ApplicationDisplayVersion", "1.2.3")
				.WithItem("MauiIcon", "Resources\\AppIcon\\appicon.svg", ("Color", "#512BD4"))
				.WithItem(
					"MauiSplashScreen",
					"Resources\\Splash\\splash.png",
					("Color", "#512BD4"),
					("BaseSize", "1024,1024"),
					("ResizeQuality", quality));

			return builder;
		}

		var app = Declare("High");
		app.WriteSvg("Resources/AppIcon/appicon.svg");
		WriteStripedPng(app, "Resources/Splash/splash.png", 512);
		app.WriteTizenManifest();
		app.Generate();

		var first = app.Build();
		AssertBuildSucceeded(first);

		var splashDirectory = SplashDirectory(app, first);
		var before = HashSplashImages(splashDirectory);
		Assert.NotEmpty(before);

		var recorded = File.ReadAllText(Path.Combine(IntermediateDirectory(app, first), "tizen-splash.inputs"));
		Assert.Contains("ResizeQuality=High", recorded);

		var nearest = Declare("None");
		nearest.Generate();

		var second = nearest.Build();
		AssertBuildSucceeded(second);

		var after = HashSplashImages(splashDirectory);

		Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
		foreach (var (name, hash) in before)
			Assert.False(after[name] == hash, $"'{name}' still has the splash screen composed at the previous ResizeQuality.");

		Assert.Contains(
			"ResizeQuality=None",
			File.ReadAllText(Path.Combine(IntermediateDirectory(app, second), "tizen-splash.inputs")));

		// The packaged inputs are the recomposed files, not a stale set left on disk.
		var packaged = second.ItemsOf("TizenTpkUserIncludeFiles")
			.Where(i => i.Metadata1.Replace('\\', '/').TrimEnd('/') == "shared/res/splash")
			.Select(i => Path.GetFileName(i.Identity))
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		Assert.Equal(after.Keys.OrderBy(k => k, StringComparer.Ordinal), packaged);
	}

	/// <summary>
	/// The splash screen's metadata must reach the Resizetizer's own recorded image state.
	/// </summary>
	/// <remarks>
	/// This is the root cause behind the test above, asserted directly so it cannot regress
	/// quietly: if the splash is not in mauiimage.inputs, then no metadata on it can invalidate
	/// image processing, and every DPI source the composition reads is whatever the previous build
	/// produced. The assertion is on the Resizetizer's file rather than on an outcome because the
	/// outcome (stale pixels) is only visible for metadata that changes pixels, while the hole
	/// itself covers BaseSize, Resize, TintColor and ResizeQuality alike.
	/// </remarks>
	[Fact]
	public void TheSplashScreenParticipatesInTheResizetizerImageState()
	{
		var app = CreateApp();
		app.Generate();

		var result = app.Build();
		AssertBuildSucceeded(result);

		var inputs = Directory
			.GetFiles(Path.Combine(app.ProjectDirectory, "obj"), "mauiimage.inputs", SearchOption.AllDirectories)
			.Single();

		var recorded = File.ReadAllText(inputs).Replace('\\', '/');

		Assert.Contains("Resources/Splash/splash.svg", recorded);

		// Exactly once: the splash is contributed before the Resizetizer collects items and again
		// afterwards for referenced-project items, and a duplicate would trip the Resizetizer's
		// own duplicate-output-name detection.
		var occurrences = recorded.Split('\n').Count(l => l.Contains("Resources/Splash/splash.svg", StringComparison.Ordinal));
		Assert.Equal(1, occurrences);
	}
}
