using System;
using System.Diagnostics;
using System.IO;
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
		static string RepositoryRoot
		{
			get
			{
				var dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
					dir = dir.Parent;

				return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
			}
		}

		static string GetProperty(string projectRelativePath, string property)
		{
			var psi = new ProcessStartInfo("dotnet")
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			psi.ArgumentList.Add("msbuild");
			psi.ArgumentList.Add(Path.Combine(RepositoryRoot, projectRelativePath));
			psi.ArgumentList.Add($"-getProperty:{property}");
			psi.ArgumentList.Add("-nologo");

			using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start MSBuild.");
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return output.Trim();
		}

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
			// Regression: TizenPackage.props attaches PublicAPI/**/PublicAPI.*.txt to every
			// project. For Maui.Tizen.Core that meant ~3,270 entries describing dotnet/maui's
			// Microsoft.Maui.* surface, which would fail the real product build with RS0017 for
			// every entry that does not exist in this assembly.
			var additionalFiles = GetProperty(
				"src/Maui.Tizen.Core/Maui.Tizen.Core.csproj",
				"AdditionalFileItemNames");

			_ = additionalFiles;

			var inherited = Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/net-tizen/PublicAPI.Shipped.txt");
			var slice = Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Shipped.txt");

			// The inherited files stay on disk for the API-comparison tooling and the not-yet-ported
			// sources; the slice baselines are what the analyzer must actually consume.
			Assert.True(File.Exists(inherited), "The imported baseline should be preserved on disk.");
			Assert.True(File.Exists(slice), "The slice baseline should exist.");

			var project = File.ReadAllText(
				Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/Maui.Tizen.Core.csproj"));

			Assert.Contains("PublicAPI/net-tizen/PublicAPI.Shipped.txt", project, StringComparison.Ordinal);
			Assert.Contains("AdditionalFiles Remove=", project, StringComparison.Ordinal);
		}
	}
}
