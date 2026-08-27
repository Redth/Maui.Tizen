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
		private readonly Lazy<IReadOnlyList<MauiAssetItem>> _assetsViaProviderContract;

		public AssetPipelineTests()
		{
			_workDirectory = Path.Combine(Path.GetTempPath(), "maui-tizen-assets-" + Guid.NewGuid().ToString("n"));
			_assets = new Lazy<IReadOnlyList<MauiAssetItem>>(() => BuildAndReadMauiAssets(TargetName));
			_assetsViaProviderContract = new Lazy<IReadOnlyList<MauiAssetItem>>(
				() => BuildAndReadMauiAssets("SimulateAssetProviderContract"));
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
			//
			// Matched by prefix and extension rather than by exact name, because the SDK fingerprints
			// static web assets in some configurations (blazor.webview.<hash>.js). The conversion is
			// agnostic to that - it packages whatever target path the SDK computes - so pinning the
			// unfingerprinted spelling would make this test assert an SDK default rather than our
			// behavior, and break the day that default changes.
			var asset = _assets.Value.FirstOrDefault(a =>
			{
				var path = a.TargetPath.Replace('\\', '/');
				return path.StartsWith("wwwroot/_framework/blazor.webview", StringComparison.OrdinalIgnoreCase)
					&& path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
			});

			Assert.True(
				asset is not null,
				"The Blazor WebView script was not converted. Without it Blazor never starts. Saw: "
					+ string.Join(", ", _assets.Value.Select(a => a.TargetPath)));
			Assert.True(File.Exists(asset!.Identity), $"MauiAsset points at a missing file: '{asset.Identity}'.");
		}

		[Fact]
		public void FrameworkAssetsArePackagedUnderTheContentRoot()
		{
			// Everything must sit under the wwwroot content root, fingerprinted or not: that prefix is
			// the contentRootDir the handler derives from BlazorWebView.HostPage, so an asset outside it
			// is unreachable at runtime even though it shipped.
			Assert.NotEmpty(_assets.Value);

			foreach (var asset in _assets.Value)
			{
				Assert.StartsWith("wwwroot/", asset.TargetPath.Replace('\\', '/'), StringComparison.Ordinal);
			}
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

		[Fact]
		public void ConversionRunsThroughThePublishedAssetProviderContract()
		{
			// Maui.Tizen.Build.Tasks runs every target listed in MauiTizenAssetProviderTargets. The
			// fixture's SimulateAssetProviderContract target does the same and nothing else, so none of
			// the targets the conversion's own BeforeTargets names are scheduled - which means assets
			// here can only have arrived through the registration.
			//
			// This distinction matters: the other tests in this class reach the conversion through the
			// BeforeTargets fallback, because the fixture imports only this package. Without this test
			// the registration could silently stop working and the suite would stay green while real
			// applications - where Maui.Tizen.Build.Tasks drives the contract - broke.
			var viaContract = _assetsViaProviderContract.Value;

			Assert.NotEmpty(viaContract);
			Assert.Contains(
				viaContract,
				a => a.TargetPath.Replace('\\', '/').Equals("wwwroot/index.html", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public void BothEntryPointsProduceTheSameAssets()
		{
			// The fallback and the registration must not diverge: an app gets one or the other
			// depending on whether Maui.Tizen.Build.Tasks is in the graph, and both have to ship the
			// same files.
			var viaFallback = _assets.Value
				.Select(a => a.TargetPath.Replace('\\', '/'))
				.OrderBy(p => p, StringComparer.Ordinal);
			var viaContract = _assetsViaProviderContract.Value
				.Select(a => a.TargetPath.Replace('\\', '/'))
				.OrderBy(p => p, StringComparer.Ordinal);

			Assert.Equal(viaFallback, viaContract);
		}

		private MauiAssetItem? FindByTargetPath(string targetPath) =>
			_assets.Value.FirstOrDefault(a =>
				string.Equals(a.TargetPath.Replace('\\', '/'), targetPath, StringComparison.OrdinalIgnoreCase));

		private IReadOnlyList<MauiAssetItem> BuildAndReadMauiAssets(string target)
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

			// The fixture builds in a temp directory outside the repository, so the repo-level
			// nuget.config is NOT found by NuGet's directory walk - it would fall back to the
			// machine-global config and whatever that has enabled or disabled. Copy the repository's
			// config in so the fixture restores from the same approved feeds as everything else, and
			// point the package cache at a directory under the fixture so a machine-cached copy cannot
			// stand in for a package the approved feeds do not actually serve.
			// overwrite: both entry points are materialized into the same work directory.
			File.Copy(
				Path.Combine(repoRoot, "nuget.config"),
				Path.Combine(_workDirectory, "nuget.config"),
				overwrite: true);
			var output = RunMSBuild(project, target);

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

		private static string RunMSBuild(string project, string target)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = DotNetMuxerPath,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(project)!,
			};

			// Same reasoning as -nodeReuse:false; the build server is a separate long-lived process.
			startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

			// Restore into a cache private to this fixture. Without it a package already present in the
			// developer's global cache satisfies the restore even if the approved feeds could not serve
			// it, so the test would pass on a machine where a real consumer's build fails.
			//
			// Sited as a SIBLING of the project directory, never inside it: the Razor SDK globs the
			// project folder for content and static web assets, so a package cache under it would be
			// swept into the very item groups this test asserts on.
			startInfo.Environment["NUGET_PACKAGES"] =
				Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(project)!)!, "packages-cache");

			startInfo.ArgumentList.Add("msbuild");
			startInfo.ArgumentList.Add(project);
			startInfo.ArgumentList.Add("-restore");
			startInfo.ArgumentList.Add("-nologo");
			// Node reuse must be off. A test that shells out to MSBuild otherwise joins the machine's
			// shared worker-node pool, where it can block indefinitely behind an unrelated build, and
			// leaves long-lived nodes behind on the agent afterwards. Single-node for the same reason:
			// this build is tiny, so parallelism buys nothing and only widens the contention window.
			startInfo.ArgumentList.Add("-nodeReuse:false");
			startInfo.ArgumentList.Add("-m:1");
			startInfo.ArgumentList.Add($"-t:{target}");
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
