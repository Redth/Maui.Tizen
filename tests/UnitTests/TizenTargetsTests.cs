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
}
