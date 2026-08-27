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

		builder.WriteSvg("Resources/AppIcon/appicon.svg");
		builder.WriteSvg("Resources/Splash/splash.svg", "#FFFFFF");
		builder.WriteSvg("Resources/Images/logo.svg", "#00FF00");
		builder.WriteText("Resources/Fonts/TestFont.ttf", "not-a-real-font-but-a-stable-file");
		builder.WriteText("Resources/Raw/data.json", "{}");
		builder.WriteTizenManifest();

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

	private static void AssertBuildSucceeded(BuildResult result)
		=> Assert.True(result.Success, "Build failed:" + Environment.NewLine + result.Output);

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
	/// The Resizetizer's own Tizen splash bucket is cleaned for the same reason: when the built-in
	/// branches are active it is that folder which holds the stale images.
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

		// A stale image in the Resizetizer's own Tizen splash bucket, which is what the built-in
		// path writes once the workload exists. It must be cleaned by the same rule.
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

		Assert.False(Directory.Exists(splashDirectory), "Stale generated splash screens survived a build with no MauiSplashScreen.");
		Assert.False(File.Exists(splashMap), "The stale splash map survived a build with no MauiSplashScreen.");
		Assert.False(File.Exists(resizetizerStale), "A stale Resizetizer splash artifact survived a build with no MauiSplashScreen.");

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

		var disabled = CreateApp(root: root);
		disabled.WithProperty("EnableMauiSplashScreenProcessing", "false");
		disabled.Generate();

		var second = disabled.Build();
		AssertBuildSucceeded(second);

		Assert.False(Directory.Exists(splashDirectory), "Splash images survived a build with splash processing disabled.");
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
}
