using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

public class GenerateTizenManifestTests : TestBase
{
	private const string TemplateManifest = """
		<?xml version="1.0" encoding="utf-8"?>
		<manifest package="maui-application-id-placeholder" version="0.0.0" api-version="11" xmlns="http://tizen.org/ns/packages">
		  <profile name="common" />
		  <ui-application appid="maui-application-id-placeholder" exec="MauiTizenApp.dll" multiple="false" nodisplay="false" taskmanage="true" type="dotnet" launch_mode="single">
		    <label>maui-application-title-placeholder</label>
		    <icon>maui-appicon-placeholder</icon>
		    <icon dpi="xhdpi">maui-appicon-placeholder</icon>
		    <icon dpi="hdpi">maui-appicon-placeholder</icon>
		  </ui-application>
		</manifest>
		""";

	private static readonly XNamespace Ns = "http://tizen.org/ns/packages";

	private (GenerateTizenManifest Task, RecordingBuildEngine Engine, string Output) CreateTask(string? manifestXml = null)
	{
		var root = CreateTempDirectory();
		var manifestPath = Path.Combine(root, "tizen-manifest.xml");
		File.WriteAllText(manifestPath, manifestXml ?? TemplateManifest);

		var intermediate = Path.Combine(root, "obj");

		var task = new GenerateTizenManifest
		{
			IntermediateOutputPath = intermediate,
			TizenManifestFile = manifestPath,
			GeneratedFilename = "tizen-manifest.xml",
		};

		var engine = task.UseRecordingEngine();

		return (task, engine, Path.Combine(intermediate, "tizen-manifest.xml"));
	}

	[Fact]
	public void AppliesSingleProjectIdentity()
	{
		var (task, engine, output) = CreateTask();
		task.ApplicationId = "com.contoso.app";
		task.ApplicationTitle = "Contoso";
		task.ApplicationDisplayVersion = "2.3.4";

		Assert.True(task.Execute(), engine.AllErrors());

		var doc = XDocument.Load(output);
		var manifest = doc.Root!;
		var uiApplication = manifest.Element(Ns + "ui-application")!;

		Assert.Equal("com.contoso.app", manifest.Attribute("package")!.Value);
		Assert.Equal("2.3.4", manifest.Attribute("version")!.Value);
		Assert.Equal("com.contoso.app", uiApplication.Attribute("appid")!.Value);
		Assert.Equal("Contoso", uiApplication.Element(Ns + "label")!.Value);
		// api-version is authored, never rewritten.
		Assert.Equal("11", manifest.Attribute("api-version")!.Value);
	}

	[Theory]
	[InlineData("1", "1.0.0")]
	[InlineData("1.2", "1.2.0")]
	[InlineData("1.2.3", "1.2.3")]
	public void NormalizesDisplayVersion(string input, string expected)
	{
		Assert.True(GenerateTizenManifest.TryMergeVersionNumbers(input, out var actual));
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData("1.2.3.4")]
	[InlineData("256.0.0")]
	[InlineData("0.0.65536")]
	[InlineData("not-a-version")]
	public void RejectsInvalidDisplayVersion(string input)
	{
		Assert.False(GenerateTizenManifest.TryMergeVersionNumbers(input, out _));
	}

	[Fact]
	public void WarnsButSucceedsForInvalidDisplayVersion()
	{
		var (task, engine, output) = CreateTask();
		task.ApplicationDisplayVersion = "1.2.3.4";

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Contains("was not a valid version for Tizen", engine.AllWarnings());
		Assert.Equal("0.0.0", XDocument.Load(output).Root!.Attribute("version")!.Value);
	}

	[Fact]
	public void ResolvesAppIconPlaceholdersToGeneratedFileNames()
	{
		var (task, engine, output) = CreateTask();
		var iconPath = Path.Combine(CreateTempDirectory(), "appicon.svg");
		File.WriteAllText(iconPath, "<svg/>");
		task.AppIcon = new[] { Item(iconPath) };

		Assert.True(task.Execute(), engine.AllErrors());

		var icons = XDocument.Load(output).Root!
			.Element(Ns + "ui-application")!
			.Elements(Ns + "icon")
			.ToList();

		// The dpi-less icon falls back to the xhdpi bucket.
		Assert.Equal("xhdpi/appicon.xhigh.png", icons[0].Value);
		// The suffixes match what the Resizetizer actually writes for DpiPath.Tizen.AppIcon.
		Assert.Equal("xhdpi/appicon.xhigh.png", icons[1].Value);
		Assert.Equal("hdpi/appicon.high.png", icons[2].Value);
	}

	[Fact]
	public void UsesLinkAliasForAppIconName()
	{
		var (task, engine, output) = CreateTask();
		var iconPath = Path.Combine(CreateTempDirectory(), "source.svg");
		File.WriteAllText(iconPath, "<svg/>");
		task.AppIcon = new[] { Item(iconPath, ("Link", "renamed.svg")) };

		Assert.True(task.Execute(), engine.AllErrors());

		var icons = XDocument.Load(output).Root!.Element(Ns + "ui-application")!.Elements(Ns + "icon").ToList();
		Assert.Equal("xhdpi/renamed.xhigh.png", icons[0].Value);
	}

	[Fact]
	public void WritesSplashScreensFromEntries()
	{
		var (task, engine, output) = CreateTask();
		var splashPath = Path.Combine(CreateTempDirectory(), "splash.svg");
		File.WriteAllText(splashPath, "<svg/>");

		task.SplashScreen = new[] { Item(splashPath, ("Color", "#512BD4")) };
		task.SplashScreenEntries = new[]
		{
			Item("splash/splash.mdpi.portrait.png", ("Resolution", "mdpi"), ("Orientation", "portrait")),
			Item("splash/splash.hdpi.landscape.png", ("Resolution", "hdpi"), ("Orientation", "landscape")),
		};

		Assert.True(task.Execute(), engine.AllErrors());

		var splashes = XDocument.Load(output).Root!
			.Element(Ns + "ui-application")!
			.Element(Ns + "splash-screens")!
			.Elements(Ns + "splash-screen")
			.ToList();

		Assert.Equal(2, splashes.Count);
		Assert.Equal("splash/splash.mdpi.portrait.png", splashes[0].Attribute("src")!.Value);
		Assert.Equal("mdpi", splashes[0].Attribute("dpi")!.Value);
		Assert.Equal("portrait", splashes[0].Attribute("orientation")!.Value);
		Assert.Equal("img", splashes[0].Attribute("type")!.Value);
		Assert.Equal("false", splashes[0].Attribute("indicator-display")!.Value);
		Assert.Equal("splash/splash.hdpi.landscape.png", splashes[1].Attribute("src")!.Value);
	}

	/// <summary>
	/// On an incremental build the splash composition target is skipped, so the manifest task must
	/// recover the entries from the persisted map instead of silently dropping them. This is the
	/// regression that the upstream static-dictionary implementation could not survive.
	/// </summary>
	[Fact]
	public void RecoversSplashScreensFromMapFileWhenEntriesAreNotSupplied()
	{
		var (task, engine, output) = CreateTask();
		var splashPath = Path.Combine(CreateTempDirectory(), "splash.svg");
		File.WriteAllText(splashPath, "<svg/>");

		var mapFile = Path.Combine(CreateTempDirectory(), GenerateTizenSplashScreens.SplashMapFileName);
		File.WriteAllLines(mapFile, new[]
		{
			"hdpi|portrait|splash/splash.hdpi.portrait.png",
			"mdpi|landscape|splash/splash.mdpi.landscape.png",
		});

		task.SplashScreen = new[] { Item(splashPath) };
		task.SplashScreenMapFile = mapFile;

		Assert.True(task.Execute(), engine.AllErrors());

		var splashes = XDocument.Load(output).Root!
			.Element(Ns + "ui-application")!
			.Element(Ns + "splash-screens")!
			.Elements(Ns + "splash-screen")
			.ToList();

		Assert.Equal(2, splashes.Count);
		Assert.Equal("splash/splash.hdpi.portrait.png", splashes[0].Attribute("src")!.Value);
		Assert.Equal("splash/splash.mdpi.landscape.png", splashes[1].Attribute("src")!.Value);
	}

	[Fact]
	public void DoesNotDuplicateExistingSplashScreenEntries()
	{
		const string authored = """
			<?xml version="1.0" encoding="utf-8"?>
			<manifest package="com.contoso.app" version="1.0.0" api-version="11" xmlns="http://tizen.org/ns/packages">
			  <ui-application appid="com.contoso.app" exec="App.dll">
			    <splash-screens>
			      <splash-screen src="custom.png" type="img" dpi="mdpi" orientation="portrait" indicator-display="false" />
			    </splash-screens>
			  </ui-application>
			</manifest>
			""";

		var (task, engine, output) = CreateTask(authored);
		var splashPath = Path.Combine(CreateTempDirectory(), "splash.svg");
		File.WriteAllText(splashPath, "<svg/>");

		task.SplashScreen = new[] { Item(splashPath) };
		task.SplashScreenEntries = new[]
		{
			Item("splash/splash.mdpi.portrait.png", ("Resolution", "mdpi"), ("Orientation", "portrait")),
		};

		Assert.True(task.Execute(), engine.AllErrors());

		var splashes = XDocument.Load(output).Root!
			.Element(Ns + "ui-application")!
			.Element(Ns + "splash-screens")!
			.Elements(Ns + "splash-screen")
			.ToList();

		Assert.Single(splashes);
		Assert.Equal("custom.png", splashes[0].Attribute("src")!.Value);
	}

	[Fact]
	public void PreservesUserSuppliedIdentity()
	{
		const string authored = """
			<?xml version="1.0" encoding="utf-8"?>
			<manifest package="com.explicit.id" version="9.9.9" api-version="11" xmlns="http://tizen.org/ns/packages">
			  <ui-application appid="com.explicit.id" exec="App.dll">
			    <label>Explicit</label>
			  </ui-application>
			</manifest>
			""";

		var (task, engine, output) = CreateTask(authored);
		task.ApplicationId = "com.generated.id";
		task.ApplicationTitle = "Generated";
		task.ApplicationDisplayVersion = "1.0.0";

		Assert.True(task.Execute(), engine.AllErrors());

		var doc = XDocument.Load(output);
		Assert.Equal("com.explicit.id", doc.Root!.Attribute("package")!.Value);
		Assert.Equal("9.9.9", doc.Root!.Attribute("version")!.Value);
		Assert.Equal("Explicit", doc.Root!.Element(Ns + "ui-application")!.Element(Ns + "label")!.Value);
	}

	[Fact]
	public void FailsWithActionableErrorWhenManifestIsMissing()
	{
		var task = new GenerateTizenManifest
		{
			IntermediateOutputPath = CreateTempDirectory(),
			TizenManifestFile = Path.Combine(CreateTempDirectory(), "does-not-exist.xml"),
		};
		var engine = task.UseRecordingEngine();

		Assert.False(task.Execute());
		Assert.Contains("Platforms/Tizen/tizen-manifest.xml", engine.AllErrors());
	}

	[Fact]
	public void GeneratesDeterministicOutput()
	{
		var (task, engine, output) = CreateTask();
		task.ApplicationId = "com.contoso.app";
		task.SplashScreenEntries = new[]
		{
			Item("splash/a.mdpi.portrait.png", ("Resolution", "mdpi"), ("Orientation", "portrait")),
			Item("splash/a.hdpi.portrait.png", ("Resolution", "hdpi"), ("Orientation", "portrait")),
		};
		var splashPath = Path.Combine(CreateTempDirectory(), "splash.svg");
		File.WriteAllText(splashPath, "<svg/>");
		task.SplashScreen = new[] { Item(splashPath) };

		Assert.True(task.Execute(), engine.AllErrors());
		var first = File.ReadAllText(output);

		Assert.True(task.Execute(), engine.AllErrors());
		var second = File.ReadAllText(output);

		Assert.Equal(first, second);
	}
}
