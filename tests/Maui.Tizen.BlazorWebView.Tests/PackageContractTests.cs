using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Guards the shape of what ships: the package identity and target framework contract, the set of
	/// sources that are compiled into it, and - most importantly - the absence of a duplicate
	/// <c>Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler</c>.
	/// </summary>
	public class PackageContractTests
	{
		private static XDocument LoadProductProject()
		{
			var path = Path.Combine(AppContext.BaseDirectory, "ProductProject", "Maui.Tizen.BlazorWebView.csproj");
			Assert.True(File.Exists(path), $"The product project was not copied to '{path}'.");
			return XDocument.Load(path);
		}

		/// <summary>Walks up from the test output directory to the repository root.</summary>
		private static string FindRepositoryRoot()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
			{
				directory = directory.Parent;
			}

			Assert.True(directory is not null, "Could not locate the repository root from the test output directory.");
			return directory!.FullName;
		}

		private static XDocument LoadBuildConfiguration(string fileName)
		{
			var path = Path.Combine(AppContext.BaseDirectory, "ProductProject", fileName);
			Assert.True(File.Exists(path), $"'{fileName}' was not copied to '{path}'.");
			return XDocument.Load(path);
		}

		private static string? GetProperty(XDocument project, string name)
			=> project.Descendants(name).FirstOrDefault()?.Value;

		[Fact]
		public void PackageInheritsTheRepositoryPackageIdConvention()
		{
			// eng/baselines.json > policy.packageIdPrefix. TizenPackage.props derives both PackageId and
			// AssemblyName from the project name, so the project must not override them.
			var project = LoadProductProject();

			Assert.Null(GetProperty(project, "PackageId"));
			Assert.Null(GetProperty(project, "AssemblyName"));
			Assert.Equal("$(MSBuildProjectName)", GetProperty(LoadBuildConfiguration("TizenPackage.props"), "PackageId"));
		}

		[Fact]
		public void NewImplementationCodeUsesTheMicrosoftMauiPlatformsTizenNamespace()
		{
			// eng/baselines.json > policy.newImplementationNamespacePrefix
			Assert.Equal(
				"Microsoft.Maui.Platforms.Tizen.BlazorWebView",
				GetProperty(LoadProductProject(), "RootNamespace"));
			Assert.StartsWith(
				"Microsoft.Maui.Platforms.Tizen",
				typeof(TizenBlazorWebViewHandler).Namespace,
				StringComparison.Ordinal);
		}

		[Fact]
		public void PackageInheritsTheSingleRepositoryTargetFramework()
		{
			// No neutral net11.0 fallback is permitted: it would produce an assembly that cannot run on
			// Tizen. The TFM comes from Directory.Build.targets via IsTizenProject and must not be
			// overridden or opted out of locally.
			var project = LoadProductProject();

			Assert.Null(GetProperty(project, "TargetFramework"));
			Assert.Null(GetProperty(project, "TargetFrameworks"));
			Assert.NotEqual("false", GetProperty(project, "IsTizenProject"));

			// The TFM is assigned in eng/targets/TizenPackage.props rather than Directory.Build.targets:
			// .targets is imported after the SDK has already parsed $(TargetFramework), so assigning it
			// there makes inference fall back to identifier "_" / version "v0.0".
			Assert.Equal("$(MauiTizenTargetFramework)", GetProperty(LoadBuildConfiguration("TizenPackage.props"), "TargetFramework"));
		}

		[Fact]
		public void PackageOptsBackIntoPacking()
		{
			// TizenPackage.props defaults IsPackable to false. This project must opt back in explicitly:
			// the buildTransitive targets that carry the StaticWebAsset -> MauiAsset conversion only
			// reach consumers through the produced package, so an unpackable project ships nothing and
			// silently breaks the asset pipeline for everyone downstream.
			Assert.Equal("true", GetProperty(LoadProductProject(), "IsPackable"));
			Assert.Equal("false", GetProperty(LoadBuildConfiguration("TizenPackage.props"), "IsPackable"));
		}

		[Fact]
		public void BuildTransitiveTargetsArePackedWhereNuGetWillImportThem()
		{
			// build/ is applied only to the project that references a package DIRECTLY; buildTransitive/
			// is what reaches a transitive consumer. Packing these to the wrong folder is exactly the
			// defect this package exists to work around, so the pack path is asserted rather than assumed.
			var packed = LoadProductProject().Descendants("None")
				.Where(e => (string?)e.Attribute("Pack") == "true")
				.Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("PackagePath")))
				.ToArray();

			Assert.Contains(packed, p => p.Item1 == "buildTransitive/Maui.Tizen.BlazorWebView.props" && p.Item2 == "buildTransitive/");
			Assert.Contains(packed, p => p.Item1 == "buildTransitive/Maui.Tizen.BlazorWebView.targets" && p.Item2 == "buildTransitive/");
			Assert.DoesNotContain(packed, p => (p.Item2 ?? string.Empty).StartsWith("build/", StringComparison.Ordinal));
		}

		[Fact]
		public void SourceBuildsImportTheProviderTargetsExplicitly()
		{
			// A ProjectReference does not import a referenced project's build/ or buildTransitive/
			// assets - only a PackageReference does. So the sample, which consumes this package by
			// project reference, has to import the provider targets itself or its wwwroot and
			// _framework assets never become Tizen resources and the app launches blank.
			var sample = File.ReadAllText(Path.Combine(
				FindRepositoryRoot(), "samples", "BlazorWebView", "Maui.Tizen.BlazorWebView.Sample",
				"Maui.Tizen.BlazorWebView.Sample.csproj"));

			Assert.Contains("buildTransitive/Maui.Tizen.BlazorWebView.props", sample, StringComparison.Ordinal);
			Assert.Contains("buildTransitive/Maui.Tizen.BlazorWebView.targets", sample, StringComparison.Ordinal);
		}

		[Fact]
		public void SampleUsesTheTizenHostAndEntryPoint()
		{
			// UseMauiApp/MauiApplication would bind the sample to a MAUI backend that no longer ships
			// for Tizen, leaving no handler for any view - including the BlazorWebView it exists to show.
			var root = FindRepositoryRoot();
			var program = File.ReadAllText(Path.Combine(
				root, "samples", "BlazorWebView", "Maui.Tizen.BlazorWebView.Sample", "MauiProgram.cs"));
			var entry = File.ReadAllText(Path.Combine(
				root, "samples", "BlazorWebView", "Maui.Tizen.BlazorWebView.Sample", "Platforms", "Tizen", "Main.cs"));

			Assert.Contains("UseMauiAppTizen<App>()", program, StringComparison.Ordinal);
			Assert.DoesNotContain("UseMauiApp<App>()", program, StringComparison.Ordinal);
			Assert.Contains(": TizenMauiApplication", entry, StringComparison.Ordinal);
		}

		[Fact]
		public void PackageRefusesToPackWhileTizenUIExtensionsIsUnshippable()
		{
			// Inherited transitively from the ProjectReference to Maui.Tizen.Core, which depends on
			// Tizen.UIExtensions.NUI 0.9.2 and its .NET 6-era Microsoft.Maui.Graphics. Maui.Tizen.Core
			// already refuses to pack for that reason; this project must too, or the blocker leaks into
			// a published package one level up. See eng/Maui.props.
			var guard = LoadProductProject().Descendants("Target")
				.FirstOrDefault(t => t.Attribute("Name")?.Value == "BlockPackOnUnshippableUIExtensions");

			Assert.NotNull(guard);
			Assert.Equal("'$(TizenUIExtensionsIsShippable)' != 'true'", guard!.Attribute("Condition")?.Value);
			Assert.Equal("MAUITIZEN0101", guard.Descendants("Error").FirstOrDefault()?.Attribute("Code")?.Value);
		}

		[Fact]
		public void PackageDependsOnTheTizenCoreBackend()
		{
			// TizenBlazorWebViewHandler derives from Maui.Tizen.Core's TizenViewHandler<,>.
			var references = LoadProductProject().Descendants("ProjectReference")
				.Select(e => e.Attribute("Include")?.Value)
				.ToArray();

			Assert.Contains("../Maui.Tizen.Core/Maui.Tizen.Core.csproj", references);
		}

		[Fact]
		public void PackageDoesNotCompileTheRawUpstreamImport()
		{
			// The raw dotnet/maui import under Tizen/ redefines
			// Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler. Compiling it would produce
			// a duplicate of a type that now ships in the shared MAUI package.
			// The default glob stays off (TizenPackage.props), and the project must not turn it back on.
			Assert.Equal("false", GetProperty(LoadBuildConfiguration("TizenPackage.props"), "EnableDefaultCompileItems"));
			Assert.NotEqual("true", GetProperty(LoadProductProject(), "EnableDefaultCompileItems"));

			// Compile items come from the shared source list.
			var compiled = LoadProductProject().Descendants("Compile")
				.Select(e => e.Attribute("Include")?.Value)
				.Where(v => v is not null)
				.Select(v => v!)
				.ToArray();

			Assert.Equal(new[] { "@(MauiTizenBlazorWebViewCompile)" }, compiled);

			// ...and that list never reaches into the raw import under Tizen/.
			var globs = LoadBuildConfiguration("Maui.Tizen.BlazorWebView.Sources.props")
				.Descendants("MauiTizenBlazorWebViewCompile")
				.Select(e => e.Attribute("Include")?.Value)
				.Where(v => v is not null)
				.Select(v => v!)
				.ToArray();

			Assert.NotEmpty(globs);
			Assert.All(globs, include => Assert.DoesNotContain("Tizen/", include, StringComparison.Ordinal));
			Assert.All(
				new[] { "Handlers", "Hosting", "Internal", "StaticContent" },
				folder => Assert.Contains(globs, g => g.Contains($"{folder}/**/*.cs", StringComparison.Ordinal)));
		}

		[Fact]
		public void PackageReferencesTheSharedBlazorWebViewPackage()
		{
			var referenced = LoadProductProject().Descendants("PackageReference")
				.Select(e => e.Attribute("Include")?.Value)
				.ToArray();

			Assert.Contains("Microsoft.AspNetCore.Components.WebView.Maui", referenced);
			Assert.Contains("Microsoft.Maui.Core", referenced);
		}

		[Fact]
		public void PackageInheritsTheSharedSamsungWorkloadGate()
		{
			// The gate is repository-wide (Directory.Build.targets > ValidateTizenWorkloadAvailable); this
			// project must not define a competing one.
			var gate = LoadBuildConfiguration("Directory.Build.targets")
				.Descendants("Target")
				.FirstOrDefault(t => t.Attribute("Name")?.Value == "ValidateTizenWorkloadAvailable");

			Assert.NotNull(gate);

			// The message must name the missing Samsung manifest so the failure is actionable rather
			// than looking like a typo in the TFM. Matched case-insensitively and by the stable prefix,
			// because the exact manifest id is a moving target while the band is being pinned down.
			var message = gate!.Descendants("Error").FirstOrDefault()?.Attribute("Text")?.Value ?? string.Empty;
			Assert.Contains("Samsung.NET.Sdk.Tizen.Manifest-", message, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("MSBuildProjectName", message, StringComparison.Ordinal);

			// The project defines no competing workload gate. It does carry the unrelated
			// MAUITIZEN0101 pack guard, so filter by code rather than asserting there are no errors.
			Assert.DoesNotContain(
				LoadProductProject().Descendants("Error"),
				e => e.Attribute("Code")?.Value == "MAUITIZEN0001");
		}

		[Fact]
		public void CentralPackageManagementPinsTheBaselineMauiVersion()
		{
			// eng/baselines.json > source.developmentPackageBaseline.version. Anything older than the build
			// that contains dotnet/maui#36658 cannot register a third-party BlazorWebView handler.
			var baselineVersion = ReadBaselineDevelopmentPackageVersion();
			var pinned = LoadBuildConfiguration("Directory.Packages.props")
				.Descendants("PackageVersion")
				.FirstOrDefault(e => e.Attribute("Include")?.Value == "Microsoft.AspNetCore.Components.WebView.Maui");

			// Asserted against eng/baselines.json rather than a literal: the baseline is bumped
			// deliberately and this test should track it, not have to be edited alongside it.
			Assert.NotNull(pinned);
			Assert.False(string.IsNullOrWhiteSpace(baselineVersion));
			Assert.Equal(baselineVersion, pinned!.Attribute("Version")?.Value);
		}

		[Fact]
		public void AspNetCorePinsMatchTheMauiBlazorNuspecFloor()
		{
			// Microsoft.AspNetCore.Components.WebView and friends ship out of dotnet/aspnetcore on their
			// own schedule, so their versions are NOT the MAUI baseline. They must match the floor the
			// MAUI Blazor package declares in its own nuspec, or CentralPackageTransitivePinningEnabled
			// turns every transitive Microsoft.Extensions.* dependency into NU1100.
			var packages = LoadBuildConfiguration("Directory.Packages.props");
			var mauiBlazorVersion = packages.Descendants("PackageVersion")
				.FirstOrDefault(e => e.Attribute("Include")?.Value == "Microsoft.AspNetCore.Components.WebView.Maui")
				?.Attribute("Version")?.Value;

			Assert.Equal(ReadBaselineDevelopmentPackageVersion(), mauiBlazorVersion);

			foreach (var id in new[]
			{
				"Microsoft.AspNetCore.Components.WebView",
				"Microsoft.AspNetCore.Authorization",
				"Microsoft.JSInterop",
			})
			{
				var pinned = packages.Descendants("PackageVersion")
					.FirstOrDefault(e => e.Attribute("Include")?.Value == id);

				Assert.True(pinned is not null, $"'{id}' must be pinned so transitive pinning can resolve it.");
				Assert.NotEqual(mauiBlazorVersion, pinned!.Attribute("Version")?.Value);
			}
		}

		[Fact]
		public void ProjectsDoNotCarryInlinePackageVersions()
		{
			// Central Package Management: versions live in Directory.Packages.props only.
			foreach (var reference in LoadProductProject().Descendants("PackageReference"))
			{
				Assert.Null(reference.Attribute("Version"));
			}
		}

		private static string? ReadBaselineDevelopmentPackageVersion()
		{
			var path = Path.Combine(AppContext.BaseDirectory, "ProductProject", "baselines.json");
			Assert.True(File.Exists(path), $"eng/baselines.json was not copied to '{path}'.");

			using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
			return document.RootElement
				.GetProperty("source")
				.GetProperty("developmentPackageBaseline")
				.GetProperty("version")
				.GetString();
		}

		[Fact]
		public void DoesNotDefineADuplicateBlazorWebViewHandler()
		{
			// The single most important invariant of this package.
			var duplicate = typeof(TizenBlazorWebViewHandler).Assembly
				.GetTypes()
				.FirstOrDefault(t => t.FullName == "Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler");

			Assert.Null(duplicate);
		}

		[Fact]
		public void UsesTheBlazorWebViewHandlerFromTheSharedMauiPackage()
		{
			var sharedHandler = typeof(IBlazorWebViewHandler).Assembly
				.GetType("Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler");

			Assert.NotNull(sharedHandler);
			Assert.NotEqual(typeof(TizenBlazorWebViewHandler).Assembly, sharedHandler!.Assembly);
		}

		[Fact]
		public void PublicSurfaceIsLimitedToTheHandlerManagerAndRegistrationHelpers()
		{
			const string productNamespaceRoot = "Microsoft.Maui.Platforms.Tizen.BlazorWebView";
			const string testNamespaceRoot = productNamespaceRoot + ".Tests";

			var publicTypes = typeof(TizenBlazorWebViewHandler).Assembly
				.GetTypes()
				.Where(t => t.IsPublic
					&& t.Namespace?.StartsWith(productNamespaceRoot, StringComparison.Ordinal) == true
					&& t.Namespace?.StartsWith(testNamespaceRoot, StringComparison.Ordinal) != true)
				.Select(t => t.FullName)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();

			Assert.Equal(
				new[]
				{
					"Microsoft.Maui.Platforms.Tizen.BlazorWebView.TizenBlazorWebViewHandler",
					"Microsoft.Maui.Platforms.Tizen.BlazorWebView.TizenBlazorWebViewServiceCollectionExtensions",
					"Microsoft.Maui.Platforms.Tizen.BlazorWebView.TizenWebViewManager",
				},
				publicTypes);
		}

		[Fact]
		public void SampleWwwrootShipsTheBlazorHostPage()
		{
			var indexPath = Path.Combine(AppContext.BaseDirectory, "Sample", "wwwroot", "index.html");
			Assert.True(File.Exists(indexPath), $"The sample host page was not copied to '{indexPath}'.");

			var html = File.ReadAllText(indexPath);

			Assert.Contains("id=\"app\"", html, StringComparison.Ordinal);
			Assert.Contains("_framework/blazor.webview.js", html, StringComparison.Ordinal);
		}

		[Fact]
		public void SampleRegistersTheTizenHandler()
		{
			// Compiles and runs the sample's own MauiProgram wiring, so the documented registration
			// snippet is verified rather than merely asserted in prose.
			using var app = global::Maui.Tizen.BlazorWebView.Sample.MauiProgram.CreateMauiApp();

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
		}
	}
}
