using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Proves the Blazor static-web-asset handoff end to end:
/// <c>StaticWebAsset -&gt; MauiAsset -&gt; MauiProcessedAsset -&gt; TizenResource</c>.
/// </summary>
/// <remarks>
/// The application project in these tests contains NO wwwroot glob of any kind. That is the point:
/// PR #6's sample currently carries a manual <c>&lt;None Include="wwwroot/**/*" /&gt;</c>, which
/// copies files to the build output but contributes nothing to the TPK, so the app would ship
/// without its own web content.
///
/// Real Razor SDK, real <c>Microsoft.AspNetCore.Components.WebView</c> package. That matters for
/// <c>_framework/blazor.webview.js</c>, which is not a file in the project at all: it is a
/// package-provided static web asset whose relative path is a fingerprint expression
/// (<c>blazor.webview#[.{fingerprint}]?.js</c>) that only the SDK's own task resolves correctly.
/// A hand-rolled fixture would prove nothing about that case.
/// </remarks>
[Trait("Category", "MSBuild")]
public class TizenBlazorAssetHandoffTests : TestBase
{
	/// <summary>
	/// An independent stand-in for any package that registers an asset provider. The real Blazor
	/// provider exists under Maui.Tizen.BlazorWebView and has product-level tests; this fixture
	/// keeps the generic Build.Tasks seam test decoupled from that implementation.
	/// </summary>
	private static string AssetProviderFixture =>
		Path.Combine(RepositoryRoot, "tests", "UnitTests", "fixtures", "BlazorAssetProvider.targets");

	/// <summary>
	/// Builds a Razor application that references the WebView package and imports both the
	/// reference asset provider and this package's targets, which is the arrangement a real app
	/// would end up with once Maui.Tizen.BlazorWebView ships that provider.
	/// </summary>
	private (MSBuildProjectBuilder App, BuildResult Result) BuildBlazorApp(
		bool includeDuplicateProvider = false,
		bool includeImage = false,
		bool seedCompressedAssets = false)
	{
		var app = new MSBuildProjectBuilder(CreateTempDirectory("maui-tizen-blazor"))
		{
			ProjectSdk = "Microsoft.NET.Sdk.Razor",
		};

		app.WriteText("wwwroot/index.html", """
			<!DOCTYPE html>
			<html>
			  <head><meta charset="utf-8" /><title>Tizen Blazor</title></head>
			  <body>
			    <div id="app"></div>
			    <script src="_framework/blazor.webview.js" autostart="false"></script>
			  </body>
			</html>
			""");
		app.WriteText("wwwroot/css/app.css", "body { font-family: sans-serif; }");
		app.WriteTizenManifest();

		if (seedCompressedAssets)
		{
			var alternative = app.WriteText("generated/app-compressed", "generated alternative");
			var primaryGzip = app.WriteText("wwwroot/data/archive.gz", "user-authored primary");
			app
				.WithProperty("SeedCompressedAssets", "true")
				.WithProperty("SeedAlternativeAssetPath", alternative)
				.WithProperty("SeedPrimaryGzipAssetPath", primaryGzip);
		}

		if (includeImage)
		{
			// A MauiImage is what causes res.xml to be generated at all, so it has to be present
			// for the "web assets stay out of res.xml" assertion to mean anything.
			app.WriteSvg("Resources/Images/logo.svg", "#00FF00");
			app.WithItem("MauiImage", "Resources\\Images\\logo.svg");
		}

		app
			.WithProperty("ApplicationId", "com.contoso.blazorapp")
			.WithProperty("ApplicationTitle", "Contoso Blazor")
			.WithPackageReference("Microsoft.AspNetCore.Components.WebView", WebViewPackageVersion)
			.WithImport(AssetProviderFixture);

		if (includeDuplicateProvider)
		{
			// Simulates an app that ALSO picks the conversion up from somewhere else, which is what
			// a direct Microsoft.AspNetCore.Components.WebView.Maui reference would do.
			app.WithImport(AssetProviderFixture, alias: "duplicate");
		}

		app.Generate();

		var result = app.Build();
		Assert.True(result.Success, "Build failed:" + Environment.NewLine + result.Output);

		return (app, result);
	}

	private static string TpkPathOf(DumpedItem item) => item.Metadata1.Replace('\\', '/');

	[Fact]
	public void AppWwwrootBecomesATizenResourceWithoutAnyGlob()
	{
		var (app, result) = BuildBlazorApp();

		var projectFile = File.ReadAllText(app.ProjectPath);
		Assert.DoesNotContain("wwwroot/**", projectFile);
		Assert.DoesNotContain("wwwroot\\**", projectFile);
		Assert.DoesNotContain("MauiAsset", projectFile);

		var resources = result.ItemsOf("TizenResource").ToList();
		Assert.NotEmpty(resources);

		// res/wwwroot/index.html is exactly where TizenAssetFileProvider looks: it roots at the
		// Tizen resource directory plus a content root of "wwwroot".
		Assert.Contains(resources, i => TpkPathOf(i) == "wwwroot/index.html");
		Assert.Contains(resources, i => TpkPathOf(i) == "wwwroot/css/app.css");
	}

	/// <summary>
	/// The framework script is package-provided and fingerprint-addressed, so it exercises a
	/// different code path from the app's own files.
	/// </summary>
	[Fact]
	public void FrameworkScriptFromTheWebViewPackageBecomesATizenResource()
	{
		var (_, result) = BuildBlazorApp();

		var script = result.ItemsOf("TizenResource")
			.SingleOrDefault(i => TpkPathOf(i) == "wwwroot/_framework/blazor.webview.js");

		Assert.True(
			script is not null,
			"blazor.webview.js was not contributed as a Tizen resource. Resources were: "
				+ string.Join(", ", result.ItemsOf("TizenResource").Select(TpkPathOf)));

		// The item must point at a real file, otherwise packaging would fail much later.
		Assert.True(File.Exists(script!.Identity), $"'{script.Identity}' does not exist on disk.");
	}

	[Fact]
	public void EveryStaticWebAssetLandsUnderTheWwwrootContentRoot()
	{
		var (_, result) = BuildBlazorApp();

		var webContent = result.ItemsOf("TizenResource")
			.Select(TpkPathOf)
			.Where(p => p.EndsWith(".html", StringComparison.Ordinal)
				|| p.EndsWith(".css", StringComparison.Ordinal)
				|| p.EndsWith(".js", StringComparison.Ordinal))
			.ToList();

		Assert.NotEmpty(webContent);
		Assert.All(webContent, p => Assert.StartsWith("wwwroot/", p, StringComparison.Ordinal));
	}

	/// <summary>
	/// Static web assets flow through the same public Resizetizer contract as any other asset, so
	/// they must appear as processed assets rather than being special-cased into the TPK.
	/// </summary>
	[Fact]
	public void StaticWebAssetsTravelThroughTheProcessedAssetContract()
	{
		var (_, result) = BuildBlazorApp();

		var processed = result.ItemsOf("MauiProcessedAsset")
			.Select(i => i.Metadata1.Replace('\\', '/'))
			.ToList();

		Assert.Contains(processed, p => p == "wwwroot/index.html");
		Assert.Contains(processed, p => p == "wwwroot/_framework/blazor.webview.js");
	}

	[Fact]
	public void ProviderDropsAlternativeAssetsButKeepsAPrimaryGzipFile()
	{
		var (_, result) = BuildBlazorApp(seedCompressedAssets: true);
		var processed = result.ItemsOf("MauiProcessedAsset").ToList();
		var resources = result.ItemsOf("TizenResource").ToList();

		Assert.DoesNotContain(processed, i => TpkPathOf(i) == "wwwroot/app.generated.js.gz");
		Assert.Contains(processed, i =>
			TpkPathOf(i) == "wwwroot/data/archive.gz"
			&& i.Metadata2 == "Primary");
		Assert.DoesNotContain(resources, i => TpkPathOf(i) == "wwwroot/app.generated.js.gz");
		Assert.Contains(resources, i =>
			TpkPathOf(i) == "wwwroot/data/archive.gz"
			&& i.Metadata2 == "Primary");
	}

	/// <summary>
	/// Two providers contributing the same file must not pack it twice. An app that references
	/// both this package and something that already converts static web assets would otherwise
	/// produce duplicate TPK entries.
	/// </summary>
	[Fact]
	public void DuplicateProvidersDoNotProduceDuplicateResources()
	{
		var (_, result) = BuildBlazorApp(includeDuplicateProvider: true);

		var byPath = result.ItemsOf("TizenResource")
			.GroupBy(TpkPathOf, StringComparer.Ordinal)
			.Where(g => g.Count() > 1)
			.Select(g => $"{g.Key} x{g.Count()}")
			.ToList();

		Assert.True(byPath.Count == 0, "Duplicate Tizen resources: " + string.Join(", ", byPath));

		Assert.Contains(result.ItemsOf("TizenResource"), i => TpkPathOf(i) == "wwwroot/index.html");
	}

	/// <summary>
	/// Static web assets must NOT appear in res.xml.
	/// </summary>
	/// <remarks>
	/// res.xml exists to tell Tizen which resource bucket to pick for a given screen DPI, and only
	/// describes the DPI-variant image folders under res/contents. Blazor content is addressed by
	/// URL, not by DPI: listing it there would be meaningless at best. Two independent things keep
	/// it out - assets travel as MauiProcessedAsset and never reach the generator, which is only
	/// given MauiProcessedImage, and the generator additionally only accepts folders whose parent
	/// is "contents". This asserts the observable outcome rather than either mechanism.
	/// </remarks>
	[Fact]
	public void StaticWebAssetsAreNotDescribedInTheResourceManifest()
	{
		var (_, result) = BuildBlazorApp(includeImage: true);

		var resourceManifest = result.ItemsOf("TizenTpkUserIncludeFiles")
			.SingleOrDefault(i => Path.GetFileName(i.Identity) == "res.xml");

		Assert.True(resourceManifest is not null, "res.xml was not generated, so this assertion would be vacuous.");

		var xml = File.ReadAllText(resourceManifest!.Identity);

		Assert.DoesNotContain("wwwroot", xml, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("blazor", xml, StringComparison.OrdinalIgnoreCase);

		// The DPI buckets it does describe are still there.
		Assert.Contains("contents/default_All-", xml, StringComparison.Ordinal);

		// And the web content is still packaged, just through TizenResource instead.
		Assert.Contains(result.ItemsOf("TizenResource"), i => TpkPathOf(i) == "wwwroot/index.html");
	}

	/// <summary>
	/// The seam itself: a provider target registered through MauiTizenAssetProviderTargets is
	/// invoked, and its items reach Tizen packaging. Kept independent of Blazor so the contract is
	/// covered even if the Razor SDK's internals change.
	/// </summary>
	[Fact]
	public void RegisteredAssetProviderTargetsAreInvokedBeforeAssetProcessing()
	{
		var app = new MSBuildProjectBuilder(CreateTempDirectory("maui-tizen-provider"));
		var contributed = app.WriteText("generated/hello.txt", "from a provider");
		app.WriteTizenManifest();

		app.WithRawProjectContent($"""
			  <PropertyGroup>
			    <MauiTizenAssetProviderTargets>
			      $(MauiTizenAssetProviderTargets);
			      TestContributeAssets;
			    </MauiTizenAssetProviderTargets>
			  </PropertyGroup>
			  <Target Name="TestContributeAssets">
			    <ItemGroup>
			      <MauiAsset Include="{TestBase.Escape(contributed)}" Link="provided/hello.txt" />
			    </ItemGroup>
			  </Target>
			""");

		app.Generate();

		var result = app.Build();
		Assert.True(result.Success, "Build failed:" + Environment.NewLine + result.Output);

		Assert.Contains(result.ItemsOf("TizenResource"), i => TpkPathOf(i) == "provided/hello.txt");
	}

	[Fact]
	public void PackagingRejectsAlternativeAssetsFromAnyProvider()
	{
		var app = new MSBuildProjectBuilder(CreateTempDirectory("maui-tizen-alternative-assets"));
		var alternative = app.WriteText("generated/app-compressed", "generated alternative");
		var primaryGzip = app.WriteText("generated/archive.gz", "user-authored primary");
		app.WriteTizenManifest();

		app
			.WithItem("MauiAsset", alternative, ("Link", "wwwroot/app.generated.js.gz"), ("AssetRole", "Alternative"))
			.WithItem("MauiAsset", primaryGzip, ("Link", "wwwroot/data/archive.gz"), ("AssetRole", "Primary"));
		app.Generate();

		var result = app.Build();
		Assert.True(result.Success, "Build failed:" + Environment.NewLine + result.Output);

		Assert.DoesNotContain(result.ItemsOf("TizenResource"), i => TpkPathOf(i) == "wwwroot/app.generated.js.gz");
		Assert.Contains(result.ItemsOf("TizenResource"), i =>
			TpkPathOf(i) == "wwwroot/data/archive.gz"
			&& i.Metadata2 == "Primary");
	}
}
