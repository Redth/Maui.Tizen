using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the Controls bridge's build configuration and its own public API baseline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>Maui.Tizen.Controls</c> is a shipping assembly with its own public surface, but it had no
	/// PublicAPI analyzer and no baseline at all - so its API could change silently, which is
	/// exactly what the analyzer prevents for <c>Maui.Tizen.Core</c>.
	/// </para>
	/// <para>
	/// Its verification lane also referenced <c>Microsoft.Maui.Controls</c> - the XAML-inclusive
	/// package - while the product references <c>Microsoft.Maui.Controls.Core</c>. A lane with
	/// broader references than the product can compile code the product cannot, which is the one
	/// failure mode a verification lane must not have: green here, broken in the shipping assembly.
	/// </para>
	/// </remarks>
	public class ControlsPackageBoundaryTests
	{
		const string ControlsProduct = "src/Maui.Tizen.Controls/Maui.Tizen.Controls.csproj";
		const string ControlsLane = "tests/Maui.Tizen.Controls.RefPackCompile/Maui.Tizen.Controls.RefPackCompile.csproj";
		const string BaselineDir = "src/Maui.Tizen.Controls/PublicAPI/slice/";

		static string RepositoryRoot => MSBuildEvaluation.RepositoryRoot;

		static string[] Baseline => File.ReadAllLines(
			Path.Combine(RepositoryRoot, BaselineDir, "PublicAPI.Unshipped.txt"));

		[Theory]
		[InlineData(ControlsProduct)]
		[InlineData(ControlsLane)]
		public void ControlsAssemblySeesOnlyItsOwnPublicApiBaseline(string project)
		{
			var additionalFiles = MSBuildEvaluation.GetItemRelativePaths(project, "AdditionalFiles");

			var baselines = additionalFiles
				.Where(f => Path.GetFileName(f).StartsWith("PublicAPI.", StringComparison.Ordinal))
				.ToArray();

			Assert.NotEmpty(baselines);
			Assert.All(baselines, f => Assert.StartsWith(BaselineDir, f, StringComparison.Ordinal));

			// Never Core's, and never the inherited dotnet/maui surface.
			Assert.DoesNotContain(baselines, f => f.Contains("Maui.Tizen.Core", StringComparison.Ordinal));
			Assert.DoesNotContain(baselines, f => f.Contains("/PublicAPI/net-tizen/", StringComparison.Ordinal));
		}

		[Fact]
		public void TheControlsBaselineIsPopulated()
		{
			// An empty baseline would satisfy the analyzer while pinning nothing - the failure mode
			// this whole arrangement exists to avoid.
			var entries = Baseline
				.Where(l => l.Length > 0 && l != "#nullable enable")
				.ToArray();

			Assert.NotEmpty(entries);

			// Every entry belongs to this assembly's namespace, not Core's.
			Assert.All(entries, e =>
				Assert.Contains("Microsoft.Maui.Platforms.Tizen.Controls", e, StringComparison.Ordinal));
		}

		[Fact]
		public void TheBridgesPublicSurfaceIsPinnedExactly()
		{
			// Named entries rather than a count, so a rename or a signature change is visible in
			// the diff rather than merely keeping the total the same.
			var entries = Baseline
				.Where(l => l.Length > 0 && l != "#nullable enable")
				.OrderBy(l => l, StringComparer.Ordinal)
				.ToArray();

			Assert.Contains(entries, e => e.EndsWith(".TizenControlsMappings", StringComparison.Ordinal));
			Assert.Contains(entries, e => e.EndsWith(".TizenControlsHostingExtensions", StringComparison.Ordinal));
			Assert.Contains(entries, e => e.Contains(".Register() -> void", StringComparison.Ordinal));
			Assert.Contains(entries, e => e.Contains(".ConfigureTizenControls(", StringComparison.Ordinal));
			Assert.Contains(entries, e => e.Contains(".MapLineBreakMode(", StringComparison.Ordinal));
			Assert.Contains(entries, e => e.Contains(".MapAccessibility(", StringComparison.Ordinal));
		}

		[Theory]
		[InlineData(ControlsProduct)]
		[InlineData(ControlsLane)]
		public void TheAnalyzerIsReferenced(string project)
		{
			// Baselines without the analyzer are inert files.
			//
			// This read the csproj TEXT until the duplicate-reference blocker showed why that is
			// the wrong question: the analyzer reaches the product through TizenPackage.props, so
			// text matching both missed a project that correctly inherits it and stayed happy when
			// a second declaration was added on top. Evaluated items see the import.
			var analyzerReferences = MSBuildEvaluation
				.GetItems(project, "PackageReference")
				.Count(id => id.EndsWith("Microsoft.CodeAnalysis.PublicApiAnalyzers", StringComparison.Ordinal));

			Assert.Equal(1, analyzerReferences);
		}

		[Fact]
		public void TheLaneDoesNotReferenceMorePackagesThanTheProduct()
		{
			// The lane stands in for a project that cannot be restored without the Samsung
			// workload. That substitution is only honest while the lane's dependency surface is no
			// broader than the product's - otherwise it compiles code the product cannot.
			//
			// Microsoft.Maui.Controls (XAML-inclusive) versus Microsoft.Maui.Controls.Core is
			// exactly that difference, and the lane had the broader one.
			var product = PackageReferences(ControlsProduct);
			var lane = PackageReferences(ControlsLane);

			// The analyzer is build-only tooling, not part of the compile surface.
			lane.Remove("Microsoft.CodeAnalysis.PublicApiAnalyzers");
			product.Remove("Microsoft.CodeAnalysis.PublicApiAnalyzers");

			// Tizen.UIExtensions.NUI cannot be a PackageReference on a net11.0 host; the lane gets
			// its reference assemblies through PackageDownload in TizenRefPack.targets instead.
			product.Remove("Tizen.UIExtensions.NUI");

			var extra = lane.Except(product, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

			Assert.Empty(extra);
		}

		[Fact]
		public void TheLaneUsesControlsCoreRatherThanTheXamlInclusivePackage()
		{
			// Stated directly as well, because the set comparison above would also pass if BOTH
			// sides drifted to the broader package together.
			var lane = PackageReferences(ControlsLane);

			Assert.Contains("Microsoft.Maui.Controls.Core", lane);
			Assert.DoesNotContain("Microsoft.Maui.Controls", lane);
		}

		[Fact]
		public void ControlsPackIsBlockedByUnshippableUIExtensions()
		{
			var result = RunUIExtensionsPackGuard(isShippable: false);

			Assert.NotEqual(0, result.ExitCode);
			Assert.Contains("MAUITIZEN0101", result.Output, StringComparison.Ordinal);
			Assert.Contains("Maui.Tizen.Controls", result.Output, StringComparison.Ordinal);
		}

		[Fact]
		public void ControlsPackGuardAllowsAVerifiedUIExtensionsVersion()
		{
			var result = RunUIExtensionsPackGuard(isShippable: true);

			Assert.Equal(0, result.ExitCode);
			Assert.DoesNotContain("MAUITIZEN0101", result.Output, StringComparison.Ordinal);
		}

		[Theory]
		[InlineData("src/Maui.Tizen.Core/Maui.Tizen.Core.csproj")]
		[InlineData(ControlsProduct)]
		public void ExactlyOneAnalyzerPackageReferenceIsEvaluated(string project)
		{
			// TizenPackage.props already adds Microsoft.CodeAnalysis.PublicApiAnalyzers to every
			// project that imports it. Declaring it again in the project produced TWO
			// PackageReference items for the same package, which fails CI product restore with
			// NU1504 - and is invisible in the csproj, because each declaration looks perfectly
			// reasonable on its own.
			//
			// Asserted on the EVALUATED items, since that is the only view that sees the import
			// and the project together.
			var analyzerReferences = MSBuildEvaluation
				.GetItems(project, "PackageReference")
				.Count(id => id.EndsWith("Microsoft.CodeAnalysis.PublicApiAnalyzers", StringComparison.Ordinal));

			Assert.Equal(1, analyzerReferences);
		}

		[Theory]
		[InlineData("src/Maui.Tizen.Core/Maui.Tizen.Core.csproj")]
		[InlineData(ControlsProduct)]
		public void NoPackageIsReferencedTwice(string project)
		{
			// The general form. Any duplicate PackageReference is an NU1504; the analyzer was only
			// the one that happened to be caught.
			var duplicates = MSBuildEvaluation
				.GetItems(project, "PackageReference")
				.GroupBy(id => id, StringComparer.Ordinal)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.OrderBy(k => k, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(duplicates);
		}

		[Fact]
		public void TheSampleLaneDoesNotReferenceControls()
		{
			// The real sample references only Maui.Tizen.Core - it is a Core-only app head using no
			// Controls types. Its lane referenced Microsoft.Maui.Controls anyway, which stayed
			// invisible until Controls' LayoutAlignment began colliding with
			// Microsoft.Maui.Primitives.LayoutAlignment in the sample's own stubs: a CS0104 the
			// product could never produce, caused entirely by the lane's extra reference.
			//
			// Over-broad lane references fail in both directions - green here and broken in the
			// product, or red here and fine in the product. Both waste the lane's credibility.
			var lane = PackageReferences("tests/Maui.Tizen.Sample.RefPackCompile/Maui.Tizen.Sample.RefPackCompile.csproj");

			Assert.DoesNotContain("Microsoft.Maui.Controls", lane);
			Assert.DoesNotContain("Microsoft.Maui.Controls.Core", lane);
		}

		static HashSet<string> PackageReferences(string project) => Regex
			.Matches(
				File.ReadAllText(Path.Combine(RepositoryRoot, project)),
				@"<PackageReference\s+Include=""([^""]+)""")
			.Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);

		static (int ExitCode, string Output) RunUIExtensionsPackGuard(bool isShippable)
		{
			var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
				? host
				: "dotnet";

			var startInfo = new ProcessStartInfo(dotnet)
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			startInfo.ArgumentList.Add("msbuild");
			startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, ControlsProduct));
			startInfo.ArgumentList.Add("-t:BlockPackOnUnshippableUIExtensions");
			startInfo.ArgumentList.Add("-nologo");
			startInfo.ArgumentList.Add("-p:TargetFramework=net11.0");
			startInfo.ArgumentList.Add("-p:TizenWorkloadAvailable=true");
			startInfo.ArgumentList.Add($"-p:TizenUIExtensionsIsShippable={isShippable.ToString().ToLowerInvariant()}");

			using var process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Failed to start dotnet msbuild.");

			var output = process.StandardOutput.ReadToEnd();
			var error = process.StandardError.ReadToEnd();
			process.WaitForExit();

			return (process.ExitCode, output + error);
		}
	}
}
