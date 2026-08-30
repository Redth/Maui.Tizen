using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the package boundary between the backend and the sample, and the ownership of the
	/// PublicAPI baselines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An MSBuild review found two merge blockers that these tests exist to prevent recurring, both
	/// of which were reproduced before being fixed.
	/// </para>
	/// <para>
	/// The real sample evaluated <c>Compile=[]</c>. TizenPackage.props defaults
	/// EnableDefaultCompileItems to false for the not-yet-ported projects, and the sample never
	/// opted back in, so it was an application head that "built" while containing no code at all.
	/// Its sources were instead compiled inside the backend lane, producing one merged Core+sample
	/// assembly - which meant the package boundary the sample exists to demonstrate was never
	/// crossed.
	/// </para>
	/// <para>
	/// And because that merged lane had both baseline pairs attached, PublicAPI ownership was
	/// false-green. Moving <c>Microsoft.Maui.Platforms.Tizen.TizenFlyoutView</c> out of the backend
	/// baseline and into the SAMPLE's baseline still built successfully. It now fails RS0016.
	/// </para>
	/// </remarks>
	public class PackageBoundaryTests
	{
		const string RealSample = "samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj";
		const string CoreLane = "tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj";
		const string SampleLane = "tests/Maui.Tizen.Sample.RefPackCompile/Maui.Tizen.Sample.RefPackCompile.csproj";

		static string RepositoryRoot => MSBuildEvaluation.RepositoryRoot;

		static string[] SampleSourceNames => new[]
		{
			"MauiProgram.cs",
			"SampleApplication.cs",
			"SampleViews.cs",
			"Main.cs",
		};

		[Fact]
		public void RealSampleCompilesItsOwnSources()
		{
			// The blocker, stated directly. This assertion is on the EVALUATED item list, which is
			// the only thing that can observe it: the project built happily with Compile=[] and
			// nothing in any log said so.
			var compiled = MSBuildEvaluation.GetItemFileNames(RealSample, "Compile");

			Assert.NotEmpty(compiled);

			foreach (var expected in SampleSourceNames)
				Assert.Contains(expected, compiled, StringComparer.Ordinal);
		}

		[Fact]
		public void RealSampleCompilesEveryCSharpFileItOwns()
		{
			// Stronger than the list above, and self-maintaining: a fifth sample file added later
			// must also be compiled, rather than quietly sitting outside the build.
			var onDisk = Directory
				.EnumerateFiles(Path.Combine(RepositoryRoot, "samples/Maui.Tizen.Sample"), "*.cs", SearchOption.AllDirectories)
				.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Select(Path.GetFileName)
				.OfType<string>()
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			var compiled = MSBuildEvaluation.GetItemFileNames(RealSample, "Compile");

			Assert.Empty(onDisk.Except(compiled, StringComparer.Ordinal));
		}

		[Fact]
		public void SampleLaneCompilesExactlyTheRealSamplesSources()
		{
			// The sample lane stands in for a project that cannot be built without the Samsung
			// workload. That substitution is only honest while the two compile the same files, so
			// compare the evaluated sets rather than trusting the shared item list to stay in sync.
			var real = MSBuildEvaluation.GetItemFileNames(RealSample, "Compile")
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			var lane = MSBuildEvaluation.GetItemFileNames(SampleLane, "Compile")
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			Assert.Equal(real, lane);
		}

		[Fact]
		public void CoreLaneDoesNotCompileTheSamplesSources()
		{
			// The merged-assembly defect. If the backend lane absorbs the sample again, the sample
			// stops crossing an assembly boundary and could freely use backend internals.
			var coreLane = MSBuildEvaluation.GetItemRelativePaths(CoreLane, "Compile");

			var absorbed = coreLane
				.Where(p => p.StartsWith("samples/", StringComparison.Ordinal))
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(absorbed);
		}

		[Fact]
		public void SampleLaneReachesTheBackendThroughAnAssemblyReference()
		{
			// A ProjectReference, not a source include, is what makes the boundary real: the sample
			// then sees only the backend's public surface, exactly as a package consumer does.
			var references = MSBuildEvaluation.GetItemRelativePaths(SampleLane, "ProjectReference");

			Assert.Contains(
				references,
				r => r.EndsWith("Maui.Tizen.Core.RefPackCompile.csproj", StringComparison.Ordinal));

			var compiled = MSBuildEvaluation.GetItemRelativePaths(SampleLane, "Compile");

			Assert.All(compiled, p => Assert.StartsWith("samples/", p, StringComparison.Ordinal));
		}

		[Fact]
		public void SampleLaneReferencesTheRealProductAssemblyName()
		{
			// Referencing an assembly called "Maui.Tizen.Core.RefPackCompile" would exercise a
			// boundary that does not exist in the shipped package.
			Assert.Equal("Maui.Tizen.Core", MSBuildEvaluation.GetProperty(CoreLane, "AssemblyName"));
		}

		[Theory]
		[InlineData(CoreLane, "src/Maui.Tizen.Core/PublicAPI/slice/", "samples/")]
		[InlineData(SampleLane, "samples/Maui.Tizen.Sample/PublicAPI/", "src/Maui.Tizen.Core/PublicAPI/slice/")]
		public void EachLaneSeesOnlyItsOwnPublicApiBaseline(string lane, string ownPrefix, string foreignPrefix)
		{
			// The structural half of the false-green fix. Ownership is only meaningful while each
			// compilation is checked against its own baseline and nothing else - with both attached
			// the analyzer sees one merged surface and any entry satisfies any assembly.
			var additionalFiles = MSBuildEvaluation.GetItemRelativePaths(lane, "AdditionalFiles");

			var publicApiFiles = additionalFiles
				.Where(f => Path.GetFileName(f).StartsWith("PublicAPI.", StringComparison.Ordinal))
				.ToArray();

			Assert.NotEmpty(publicApiFiles);
			Assert.All(publicApiFiles, f => Assert.StartsWith(ownPrefix, f, StringComparison.Ordinal));
			Assert.DoesNotContain(publicApiFiles, f => f.StartsWith(foreignPrefix, StringComparison.Ordinal));
		}

		[Fact]
		public void BaselineEntriesLiveInTheAssemblyThatOwnsThem()
		{
			// The fast half. RS0016 proves this properly, but only during a full ref-pack build;
			// this catches a cross-baseline move in the unit-test lane in milliseconds, which is
			// where someone doing the moving is most likely to notice.
			var core = BaselineTypeNames("src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt");
			var sample = BaselineTypeNames("samples/Maui.Tizen.Sample/PublicAPI/PublicAPI.Unshipped.txt");

			Assert.NotEmpty(core);
			Assert.NotEmpty(sample);

			Assert.All(core, entry =>
				Assert.StartsWith("Microsoft.Maui.Platforms.Tizen", entry, StringComparison.Ordinal));

			Assert.All(sample, entry =>
				Assert.StartsWith("Maui.Tizen.Sample", entry, StringComparison.Ordinal));
		}

		static string[] BaselineTypeNames(string relativePath) => File
			.ReadAllLines(Path.Combine(RepositoryRoot, relativePath))
			.Select(l => l.Trim())
			.Where(l => l.Length > 0 && l != "#nullable enable")
			// Entries are declaration lines; the namespace-qualified name is the first token that
			// is not a C# modifier.
			.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault(t => t is not ("static" or "virtual" or "override" or "abstract" or "sealed" or "const" or "readonly" or "extension")))
			.Where(t => t is { Length: > 0 })
			.Select(t => t!)
			.ToArray();
	}
}
