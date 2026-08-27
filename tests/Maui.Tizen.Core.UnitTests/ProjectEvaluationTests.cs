using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Evaluation-level regressions for the Tizen project files.
	/// </summary>
	/// <remarks>
	/// These shell out to MSBuild's <c>-getProperty</c> because the defect being guarded against is
	/// an <em>evaluation ordering</em> problem that no compile-time or runtime test can observe.
	/// </remarks>
	public class ProjectEvaluationTests
	{
		static string RepositoryRoot => MSBuildEvaluation.RepositoryRoot;

		static string GetProperty(string projectRelativePath, string property) =>
			MSBuildEvaluation.GetProperty(projectRelativePath, property);

		[Theory]
		[InlineData("src/Maui.Tizen.Core/Maui.Tizen.Core.csproj")]
		[InlineData("samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj")]
		public void TizenProjectEvaluatesANonEmptyTargetFramework(string project)
		{
			// Regression: the sample set IsTizenProject but did not import TizenPackage.props in
			// the PROPS phase. TargetFramework is derived from IsTizenProject in
			// Directory.Build.targets, which is far too late for the SDK's target-framework
			// inference, so the project evaluated with an EMPTY TargetFramework - producing no
			// inner build at all, silently, with no error anywhere.
			var tfm = GetProperty(project, "TargetFramework");

			Assert.False(string.IsNullOrWhiteSpace(tfm), $"{project} evaluated an empty TargetFramework.");
			Assert.Equal("net11.0-tizen11.0", tfm);
		}

		[Theory]
		[InlineData("src/Maui.Tizen.Core/Maui.Tizen.Core.csproj")]
		[InlineData("samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj")]
		public void TizenProjectIsMarkedAsATizenProject(string project) =>
			Assert.Equal("true", GetProperty(project, "IsTizenProject"));

		[Fact]
		public void ProductDoesNotInheritMauisPublicApiBaselines()
		{
			// Regression: TizenPackage.props attached PublicAPI/**/PublicAPI.*.txt to every project.
			// For Maui.Tizen.Core that meant ~3,270 entries describing dotnet/maui's Microsoft.Maui.*
			// surface, which would fail the real product build with RS0017 for every entry that does
			// not exist in this assembly.
			//
			// This test used to call GetProperty("AdditionalFileItemNames"), assign it to a discard,
			// and then assert on the csproj's raw TEXT instead - so it proved only that two strings
			// appeared somewhere in the file, and would have passed even if the Remove never took
			// effect. A review caught it. It now asserts what MSBuild actually evaluated.
			var additionalFiles = MSBuildEvaluation.GetItemRelativePaths(
				"src/Maui.Tizen.Core/Maui.Tizen.Core.csproj",
				"AdditionalFiles");

			var baselines = additionalFiles
				.Where(f => Path.GetFileName(f).StartsWith("PublicAPI.", StringComparison.Ordinal))
				.ToArray();

			Assert.NotEmpty(baselines);

			// The slice baseline - this package's own surface - must be the only one attached.
			Assert.All(baselines, f =>
				Assert.StartsWith("src/Maui.Tizen.Core/PublicAPI/slice/", f, StringComparison.Ordinal));

			Assert.DoesNotContain(baselines, f => f.Contains("/PublicAPI/net-tizen/", StringComparison.Ordinal));

			// The inherited files stay on disk for the API-comparison tooling and the not-yet-ported
			// sources. Being present but unattached is the whole point.
			Assert.True(
				File.Exists(Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/net-tizen/PublicAPI.Shipped.txt")),
				"The imported baseline should be preserved on disk.");
		}

		[Fact]
		public void ProductCompilesTheBackendSources()
		{
			// The product cannot be BUILT without the Samsung workload, but it can be evaluated,
			// and an empty Compile list is exactly the failure the sample shipped with.
			var compiled = MSBuildEvaluation.GetItemRelativePaths(
				"src/Maui.Tizen.Core/Maui.Tizen.Core.csproj",
				"Compile");

			Assert.NotEmpty(compiled);
			Assert.All(compiled, p => Assert.StartsWith("src/Maui.Tizen.Core/", p, StringComparison.Ordinal));
		}


		[Fact]
		public void TheSampleDeclaresTizenManifestFileAsAProperty()
		{
			// Samsung's targets read $(TizenManifestFile) to locate the manifest when building the
			// .tpk. It was declared as an ItemGroup entry instead - which looks plausible, is never
			// read, and produces no warning, so the manifest would simply have been absent from the
			// package.
			var manifest = MSBuildEvaluation.GetProperty(
				"samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj",
				"TizenManifestFile");

			Assert.Equal("Platforms/Tizen/tizen-manifest.xml", manifest);
		}

		[Fact]
		public void TheDeclaredManifestFileExists()
		{
			// A property pointing at nothing is no better than an unread item.
			var manifest = MSBuildEvaluation.GetProperty(
				"samples/Maui.Tizen.Sample/Maui.Tizen.Sample.csproj",
				"TizenManifestFile");

			Assert.True(
				File.Exists(Path.Combine(RepositoryRoot, "samples/Maui.Tizen.Sample", manifest)),
				$"The manifest '{manifest}' does not exist.");
		}
	}
}
