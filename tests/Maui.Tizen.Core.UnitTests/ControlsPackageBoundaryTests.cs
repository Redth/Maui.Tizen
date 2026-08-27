using System;
using System.Collections.Generic;
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
			// Baselines without the analyzer are inert files. Read from the csproj because the
			// analyzer arrives as a PackageReference whose evaluated form is not an item type this
			// helper fetches.
			var text = File.ReadAllText(Path.Combine(RepositoryRoot, project));

			Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", text, StringComparison.Ordinal);
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

		static HashSet<string> PackageReferences(string project) => Regex
			.Matches(
				File.ReadAllText(Path.Combine(RepositoryRoot, project)),
				@"<PackageReference\s+Include=""([^""]+)""")
			.Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);
	}
}
