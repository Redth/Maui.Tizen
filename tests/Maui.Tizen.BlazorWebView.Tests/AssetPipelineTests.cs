using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Proves the Blazor asset pipeline actually produces <c>MauiAsset</c> items, by running MSBuild
	/// against a real Razor project rather than asserting anything about the XML.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the regression test for the defect the package exists to fix: the MAUI Blazor package
	/// ships its <c>ConvertStaticWebAssetsToMauiAssets</c> target in <c>build/</c> with no
	/// <c>buildTransitive/</c>, so a transitive consumer never gets it, the <c>wwwroot</c> and
	/// <c>_framework/blazor.webview.js</c> are never packaged, and every request 404s at runtime while
	/// the build stays green. A test that only inspected our own <c>.targets</c> would not have caught
	/// that, because the file looked fine — it was the NuGet folder convention that was wrong.
	/// </para>
	/// <para>
	/// The fixture is a plain <c>net11.0</c> Razor app, not a Tizen or MAUI one: the conversion only
	/// needs the Razor SDK's StaticWebAssets, so this runs with no Samsung workload installed.
	/// The assertions stop at <c>MauiAsset</c>, which is exactly where
	/// <c>Maui.Tizen.Build.Tasks</c> picks up (<c>ProcessMauiAssets</c> →
	/// <c>MauiProcessedAsset</c> → <c>TizenResource</c>).
	/// </para>
	/// </remarks>
	[Trait("Category", "MSBuild")]
	public sealed class AssetPipelineTests : IDisposable
	{
		private const string TargetName = "ConvertStaticWebAssetsToTizenMauiAssets";

		private readonly string _workDirectory;
		private readonly Lazy<IReadOnlyList<MauiAssetItem>> _assets;

		public AssetPipelineTests()
		{
			_workDirectory = Path.Combine(Path.GetTempPath(), "maui-tizen-assets-" + Guid.NewGuid().ToString("n"));
			_assets = new Lazy<IReadOnlyList<MauiAssetItem>>(BuildAndReadMauiAssets);
		}

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(_workDirectory))
				{
					Directory.Delete(_workDirectory, recursive: true);
				}
			}
			catch (IOException)
			{
				// A leftover temp directory must never fail the test run.
			}
		}

		[Fact]
		public void HostPageIsPackagedAtTheContentRoot()
		{
			// BlazorWebView.HostPage is "wwwroot/index.html", and TizenAssetFileProvider resolves it
			// under the Tizen resource directory, so this exact target path is load-bearing.
			var asset = FindByTargetPath("wwwroot/index.html");

			Assert.NotNull(asset);
			Assert.True(File.Exists(asset!.Identity), $"MauiAsset points at a missing file: '{asset.Identity}'.");
		}

		[Fact]
		public void NestedApplicationAssetsKeepTheirRelativePath()
		{
			// A flattened asset would still "exist" but resolve to the wrong URL at runtime.
			var asset = FindByTargetPath("wwwroot/css/app.css");

			Assert.NotNull(asset);
			Assert.True(File.Exists(asset!.Identity), $"MauiAsset points at a missing file: '{asset.Identity}'.");
		}

		[Fact]
		public void BlazorWebViewScriptFromTheFrameworkIsPackaged()
		{
			// The one asset nobody authors and everybody needs: without it Blazor.start() never runs.
			// It arrives as a StaticWebAsset from the Microsoft.AspNetCore.Components.WebView package,
			// which proves the conversion covers framework-provided assets and not just wwwroot/.
			var asset = FindByTargetPath("wwwroot/_framework/blazor.webview.js");

			Assert.NotNull(asset);
			Assert.True(File.Exists(asset!.Identity), $"MauiAsset points at a missing file: '{asset.Identity}'.");
		}

		[Fact]
		public void EveryAssetCarriesLinkMetadataMatchingItsTargetPath()
		{
			// Maui.Tizen.Build.Tasks' MauiTizenProcessAssets derives TizenTpkFileName from %(Link).
			// An empty Link silently packages the asset at the wrong place in the TPK.
			Assert.NotEmpty(_assets.Value);

			foreach (var asset in _assets.Value)
			{
				Assert.False(string.IsNullOrEmpty(asset.Link), $"'{asset.TargetPath}' has no Link metadata.");
				Assert.Equal(asset.TargetPath, asset.Link);
			}
		}

		[Fact]
		public void AssetsAreNotDuplicated()
		{
			// The conversion is idempotent by design, because a consumer that also references the MAUI
			// Blazor package directly gets the upstream conversion as well.
			var duplicates = _assets.Value
				.GroupBy(a => a.TargetPath, StringComparer.OrdinalIgnoreCase)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.ToArray();

			Assert.Empty(duplicates);
		}

		[Fact]
		public void PrecompressedVariantsAreNotPackaged()
		{
			// Assets are served from local storage by the request interceptor, so .gz/.br copies are
			// pure TPK bloat.
			var compressed = _assets.Value
				.Where(a => a.TargetPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
					|| a.TargetPath.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
				.Select(a => a.TargetPath)
				.ToArray();

			Assert.Empty(compressed);
		}

		private MauiAssetItem? FindByTargetPath(string targetPath) =>
			_assets.Value.FirstOrDefault(a =>
				string.Equals(a.TargetPath.Replace('\\', '/'), targetPath, StringComparison.OrdinalIgnoreCase));

		private IReadOnlyList<MauiAssetItem> BuildAndReadMauiAssets()
		{
			var repoRoot = FindRepositoryRoot();
			var fixtureSource = Path.Combine(repoRoot, "tests", "Maui.Tizen.BlazorWebView.Tests", "AssetPipelineFixture");
			var buildTransitive = Path.Combine(repoRoot, "src", "Maui.Tizen.BlazorWebView", "buildTransitive");

			CopyDirectory(fixtureSource, _workDirectory);

			var project = Path.Combine(_workDirectory, "AssetPipelineFixture.csproj");
			var template = File.ReadAllText(Path.Combine(_workDirectory, "AssetPipelineFixture.csproj.fixture"));
			File.WriteAllText(
				project,
				template
					.Replace("__WEBVIEW_VERSION__", ReadPinnedWebViewVersion(repoRoot))
					.Replace("__TARGETS_PROPS__", Path.Combine(buildTransitive, "Maui.Tizen.BlazorWebView.props"))
					.Replace("__TARGETS_FILE__", Path.Combine(buildTransitive, "Maui.Tizen.BlazorWebView.targets")));
			File.Delete(Path.Combine(_workDirectory, "AssetPipelineFixture.csproj.fixture"));

			// A NuGet.config is not copied in: the fixture must resolve packages the same way the
			// repository does, so it relies on the repo-level nuget.config found by directory walk.
			var output = RunMSBuild(project, repoRoot);

			using var document = JsonDocument.Parse(output);
			if (!document.RootElement.TryGetProperty("Items", out var items) ||
				!items.TryGetProperty("MauiAsset", out var mauiAssets))
			{
				return Array.Empty<MauiAssetItem>();
			}

			return mauiAssets.EnumerateArray()
				.Select(e => new MauiAssetItem(
					e.TryGetProperty("FullPath", out var full) ? full.GetString() ?? string.Empty : string.Empty,
					e.TryGetProperty("TargetPath", out var target) ? target.GetString() ?? string.Empty : string.Empty,
					e.TryGetProperty("Link", out var link) ? link.GetString() ?? string.Empty : string.Empty))
				.ToArray();
		}

		private static string ReadPinnedWebViewVersion(string repoRoot)
		{
			// Read the pin rather than hardcoding it, so a baseline bump does not silently test a
			// version the repository no longer uses.
			var packages = File.ReadAllText(Path.Combine(repoRoot, "Directory.Packages.props"));
			var document = System.Xml.Linq.XDocument.Parse(packages);
			var version = document.Descendants("PackageVersion")
				.FirstOrDefault(e => (string?)e.Attribute("Include") == "Microsoft.AspNetCore.Components.WebView")
				?.Attribute("Version")?.Value;

			Assert.False(string.IsNullOrWhiteSpace(version), "Microsoft.AspNetCore.Components.WebView is not pinned.");
			return version!;
		}

		private static string RunMSBuild(string project, string repoRoot)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = DotNetMuxerPath,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(project)!,
			};

			startInfo.ArgumentList.Add("msbuild");
			startInfo.ArgumentList.Add(project);
			startInfo.ArgumentList.Add("-restore");
			startInfo.ArgumentList.Add("-nologo");
			startInfo.ArgumentList.Add($"-t:{TargetName}");
			startInfo.ArgumentList.Add("-getItem:MauiAsset");
			// Central Package Management lives at the repository root and would otherwise reject the
			// fixture's explicit Version attribute.
			startInfo.ArgumentList.Add("-p:ManagePackageVersionsCentrally=false");

			using var process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Failed to start MSBuild.");

			var stdout = process.StandardOutput.ReadToEnd();
			var stderr = process.StandardError.ReadToEnd();
			process.WaitForExit(milliseconds: 10 * 60 * 1000);

			Assert.True(
				process.ExitCode == 0,
				$"MSBuild failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

			// -getItem: prints JSON, but -restore output precedes it.
			var start = stdout.IndexOf('{');
			Assert.True(start >= 0, $"No JSON in MSBuild output:{Environment.NewLine}{stdout}");
			return stdout.Substring(start);
		}

		private static string DotNetMuxerPath
		{
			get
			{
				// Reuse the muxer running the tests so the fixture builds with the same SDK the
				// repository is pinned to in global.json.
				var main = Process.GetCurrentProcess().MainModule?.FileName;
				if (!string.IsNullOrEmpty(main) &&
					Path.GetFileNameWithoutExtension(main).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
				{
					return main!;
				}

				var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
				if (!string.IsNullOrEmpty(root))
				{
					var candidate = Path.Combine(root!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
					if (File.Exists(candidate))
					{
						return candidate;
					}
				}

				return "dotnet";
			}
		}

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

		private static void CopyDirectory(string source, string destination)
		{
			Directory.CreateDirectory(destination);

			foreach (var file in Directory.GetFiles(source))
			{
				File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
			}

			foreach (var directory in Directory.GetDirectories(source))
			{
				CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
			}
		}

		private sealed record MauiAssetItem(string Identity, string TargetPath, string Link);
	}
}
