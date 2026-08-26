using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Guards the invariants that the dotnet/maui -> Maui.Tizen extraction depends on.
///
/// The failure modes these catch are all ones that are silent at build time and
/// expensive to diagnose later: baselines drifting from the build configuration,
/// a neutral TFM fallback sneaking in and making the build "green" while producing
/// assemblies that cannot run on Tizen, or the provenance record losing the commits
/// that justify the import.
/// </summary>
public class RepositoryInvariantTests
{
	static readonly string RepoRoot = RepositoryPaths.Root;

	static string ReadRepoFile(string relativePath)
	{
		var full = Path.Combine(RepoRoot, relativePath);
		Assert.True(File.Exists(full), $"Expected repository file to exist: {relativePath}");
		return File.ReadAllText(full);
	}

	static JsonElement ReadRepoJson(string relativePath)
	{
		using var doc = JsonDocument.Parse(
			ReadRepoFile(relativePath),
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		return doc.RootElement.Clone();
	}

	// ---------------------------------------------------------------------
	// Baseline pins
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData("sourceBaseline")]
	[InlineData("requiredAncestor")]
	[InlineData("behaviorBaseline")]
	public void BaselineCommitsArePinnedToFullShas(string baselineName)
	{
		// Abbreviated SHAs and branch names are both rejected. origin/net11.0 advanced
		// mid-import (ee4d06cde6 -> bedd1b18b7), which is precisely why a branch name is
		// not a baseline.
		var commit = ReadRepoJson("eng/baselines.json")
			.GetProperty("source").GetProperty(baselineName).GetProperty("commit").GetString();

		Assert.NotNull(commit);
		Assert.Matches("^[0-9a-f]{40}$", commit!);
	}

	[Fact]
	public void RequiredAncestorIsThePr36657Commit()
	{
		// The Essentials/MainThread extensibility work is the minimum API floor for an
		// out-of-tree platform implementation. Losing this pin would silently reintroduce
		// a baseline that cannot support this repository at all.
		var required = ReadRepoJson("eng/baselines.json")
			.GetProperty("source").GetProperty("requiredAncestor");

		Assert.Equal("0b3bb76d2dd68d76b7c1302f43a76270d5949564", required.GetProperty("commit").GetString());
		Assert.Equal(36657, required.GetProperty("pullRequest").GetInt32());
	}

	// ---------------------------------------------------------------------
	// Build configuration agrees with the baselines
	// ---------------------------------------------------------------------

	[Fact]
	public void DirectoryBuildPropsTargetFrameworkMatchesBaselines()
	{
		var expected = ReadRepoJson("eng/baselines.json")
			.GetProperty("target").GetProperty("targetFramework").GetString();

		var props = ReadRepoFile("Directory.Build.props");
		var dotnet = Regex.Match(props, @"<DotNetVersion>([^<]+)</DotNetVersion>");
		var tizen = Regex.Match(props, @"<TizenPlatformVersion>([^<]+)</TizenPlatformVersion>");

		Assert.True(dotnet.Success, "Directory.Build.props must declare <DotNetVersion>");
		Assert.True(tizen.Success, "Directory.Build.props must declare <TizenPlatformVersion>");

		Assert.Equal(expected, $"net{dotnet.Groups[1].Value}-tizen{tizen.Groups[1].Value}");
	}

	[Fact]
	public void GlobalJsonSdkMatchesDeclaredBand()
	{
		var band = ReadRepoJson("eng/baselines.json")
			.GetProperty("target").GetProperty("sdkBand").GetString();
		var sdk = ReadRepoJson("global.json")
			.GetProperty("sdk").GetProperty("version").GetString();

		Assert.NotNull(band);
		Assert.NotNull(sdk);
		Assert.StartsWith(band!, sdk!);

		// A bare band is not a resolvable SDK version. actions/setup-dotnet@v5 looks the
		// value up verbatim and fails with "Could not find .NET Core SDK with version =
		// 11.0.100-preview.7". v4 tolerated it, so this only surfaced on upgrade.
		Assert.NotEqual(band, sdk);
	}

	[Fact]
	public void RepositoryDoesNotSupportDotNet10()
	{
		// .NET 11+ only. A net10.0 target would drag the whole repository back to a
		// baseline that predates the Essentials extensibility work.
		var policy = ReadRepoJson("eng/baselines.json").GetProperty("policy");

		Assert.False(policy.GetProperty("supportsDotNet10").GetBoolean());
		Assert.Equal("11.0", policy.GetProperty("minimumDotNet").GetString());
	}

	[Fact]
	public void NoProjectFallsBackToANeutralTargetFramework()
	{
		// The tempting "fix" for the missing Samsung workload is to fall back to neutral
		// net11.0 so CI goes green. That would produce assemblies that cannot run on
		// Tizen, turning a visible external gate into an invisible correctness bug.
		//
		// Test projects are exempt: they are hosts, not shipping Tizen artifacts.
		var offenders = Directory
			.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
			.Where(p =>
			{
				var text = File.ReadAllText(p);
				return Regex.IsMatch(text, @"<TargetFrameworks?>net\d+\.\d+</TargetFrameworks?>");
			})
			.Select(p => Path.GetRelativePath(RepoRoot, p))
			.ToList();

		Assert.True(
			offenders.Count == 0,
			"These projects declare a neutral .NET TFM instead of the Tizen TFM or netstandard2.0: "
				+ string.Join(", ", offenders));
	}

	// ---------------------------------------------------------------------
	// Disposition manifest contract
	// ---------------------------------------------------------------------

	[Fact]
	public void SourceDispositionSchemaIsWellFormedAndVersioned()
	{
		var schema = ReadRepoJson("eng/manifests/source-disposition.schema.json");

		Assert.Equal(1, schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
		Assert.True(schema.GetProperty("$defs").TryGetProperty("entry", out _));
	}

	[Fact]
	public void SchemaForbidsMovingSharedConditionalFiles()
	{
		// A file containing #if TIZEN branches can never be a straight move: the other
		// branches belong to the neutral MAUI assembly, so copying the whole file forks
		// code this repository does not own. The schema must keep enforcing that.
		var schema = ReadRepoFile("eng/manifests/source-disposition.schema.json");
		using var doc = JsonDocument.Parse(schema);

		var allOf = doc.RootElement
			.GetProperty("$defs").GetProperty("entry").GetProperty("allOf");

		var found = allOf.EnumerateArray().Any(rule =>
			rule.TryGetProperty("if", out var cond)
			&& cond.TryGetProperty("properties", out var props)
			&& props.TryGetProperty("kind", out var kind)
			&& kind.TryGetProperty("const", out var c)
			&& c.GetString() == "shared-conditional");

		Assert.True(found, "Schema must constrain the disposition of shared-conditional files.");
	}

	// ---------------------------------------------------------------------
	// Provenance and import reproducibility
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData("eng/import/filter-maui-tizen.sh")]
	[InlineData("eng/import/normalize-layout.sh")]
	[InlineData("eng/import/git-filter-repo")]
	[InlineData("eng/import/tizen-paths.txt")]
	public void ImportToolingIsPresent(string relativePath)
	{
		// The claim "this history is reproducible" is only true while these exist.
		Assert.True(File.Exists(Path.Combine(RepoRoot, relativePath)), $"Missing: {relativePath}");
	}

	[Theory]
	[InlineData("2360")]
	[InlineData("9619")]
	public void ProvenanceRecordsTheOriginatingPullRequests(string pullRequest)
	{
		Assert.Contains(pullRequest, ReadRepoFile("PROVENANCE.md"));
	}

	[Fact]
	public void ProvenanceRecordsTheCompatibilityDeletionCaveat()
	{
		// src/Compatibility exists only at tag 9.0.120. Anyone who baselines against
		// net11.0 alone will silently lose 70 files, so this caveat must stay written down.
		var provenance = ReadRepoFile("PROVENANCE.md");

		Assert.Contains("Compatibility", provenance);
		Assert.Contains("9.0.120", provenance);
	}

	[Fact]
	public void ThirdPartyNoticesCoverTheApacheLicensedSamsungDependencies()
	{
		var notices = ReadRepoFile("THIRD-PARTY-NOTICES.md");

		Assert.Contains("TizenFX", notices);
		Assert.Contains("Tizen.UIExtensions", notices);
		Assert.Contains("Apache", notices);
	}

	[Fact]
	public void RepositoryIsMitLicensed()
	{
		Assert.Contains("MIT License", ReadRepoFile("LICENSE"));
	}

	// ---------------------------------------------------------------------
	// Solution wiring
	// ---------------------------------------------------------------------

	[Fact]
	public void WorkloadGateRunsBeforeTheSdkRejectsTheTargetFramework()
	{
		// Without the workload the SDK fails first, and WHICH error it raises depends on
		// how far inference got:
		//
		//   NETSDK1013  "TargetFramework value 'net11.0-tizen11.0' was not recognized.
		//                It may be misspelled."      <- inference failed entirely
		//   NETSDK1139  "The target platform identifier tizen was not recognized."
		//                                            <- inference worked, no workload
		//
		// Both send people hunting for a typo that does not exist. The gate only produces
		// its explanatory MAUITIZEN0001 if it is hooked ahead of BOTH SDK pre-checks.
		//
		// This regressed twice: first the gate was hooked on Build only and never fired;
		// then fixing TFM inference changed the symptom from NETSDK1013 to NETSDK1139 and
		// it silently stopped firing again.
		var targets = ReadRepoFile("Directory.Build.targets");

		var gate = Regex.Match(
			targets,
			@"<Target\s+Name=""ValidateTizenWorkloadAvailable""(.*?)>",
			RegexOptions.Singleline);

		Assert.True(gate.Success, "Directory.Build.targets must define ValidateTizenWorkloadAvailable");

		var attributes = gate.Groups[1].Value;
		Assert.Contains("_CheckForUnsupportedTargetFramework", attributes);
		Assert.Contains("_CheckForUnsupportedTargetPlatformIdentifier", attributes);
		Assert.Contains("Restore", attributes);
	}

	[Fact]
	public void TargetFrameworkIsAssignedDuringEvaluationNotInTargets()
	{
		// Directory.Build.targets is imported at the end of Microsoft.Common.targets, long
		// after the SDK has parsed $(TargetFramework) into TargetFrameworkIdentifier and
		// TargetFrameworkVersion. Assigning the TFM there makes inference fall back to
		// identifier "_" and version "v0.0" while still *looking* correct, so everything
		// keyed off the identifier - NuGet's restore graph, framework-conditional items -
		// silently evaluates against a framework that does not exist.
		//
		// TizenPackage.props is imported from the project body, before Sdk.targets, so
		// inference sees the real value.
		var props = ReadRepoFile("eng/targets/TizenPackage.props");
		Assert.Matches(@"<TargetFramework\s+Condition[^>]*>\$\(MauiTizenTargetFramework\)</TargetFramework>", props);

		var targets = ReadRepoFile("Directory.Build.targets");
		Assert.DoesNotMatch(new Regex(@"<TargetFramework>"), targets);
		Assert.DoesNotMatch(new Regex(@"<TargetFrameworks>"), targets);
	}

	[Fact]
	public void WorkloadDetectionDoesNotUseAConstructedManifestPath()
	{
		// The original probe built sdk-manifests/$(NETCoreSdkVersion)/samsung.net.sdk.tizen/
		// by hand. The real layout is sdk-manifests/<feature-band>/<id>/<version>/, and the
		// band is not the SDK version - an 11.0.100-preview.7.26381.103 SDK installs under
		// band 11.0.100-preview.6 - so it could never match, producing a permanent false
		// "workload missing".
		var props = ReadRepoFile("Directory.Build.props");
		Assert.DoesNotContain("$(NETCoreSdkVersion)/samsung", props);
		Assert.DoesNotContain("$(BundledNETCoreAppPackageVersion)/samsung", props);

		// Detection must live in a target, where item globs actually work.
		var targets = ReadRepoFile("Directory.Build.targets");
		Assert.Contains("_DetectTizenWorkload", targets);
		Assert.Contains("samsung.net.sdk.tizen/*/WorkloadManifest.json", targets);
	}

	[Fact]
	public void WorkloadReportingDoesNotSubstringMatchWorkloadNames()
	{
		// `dotnet workload list | grep -i tizen` matches an unrelated `maui-tizen`
		// workload and would report the external gate as lifted while Samsung's workload
		// was still absent. The script asks MSBuild instead, so there is exactly one
		// detection implementation.
		//
		// Comment lines are stripped before matching: the script explains this very bug in
		// prose, and the first version of this test failed on that comment.
		var executableLines = ReadRepoFile("eng/build-workload-free.sh")
			.Split('\n')
			.Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal));
		var script = string.Join('\n', executableLines);

		Assert.DoesNotMatch(new Regex(@"workload\s+list"), script);
		Assert.Contains("ReportTizenWorkload", script);
	}

	[Fact]
	public void NoProjectTargetsBelowTheDotNetFloor()
	{
		// The repository is .NET 11+ only (eng/baselines.json > policy.minimumDotNet), and
		// that has to hold for tooling and test projects too, not just shipping ones.
		//
		// A project targeting net10.0 still builds on a machine that has the pinned .NET 11
		// SDK, but its testhost then fails at RUN time with "You must install or update
		// .NET to run this application" - unless the machine happens to also carry a .NET 10
		// runtime, which GitHub's hosted images do. So it goes green in CI and fails for
		// anyone whose environment matches global.json exactly. This test closes that gap
		// at the point the project file is written.
		var floor = ReadRepoJson("eng/baselines.json")
			.GetProperty("policy").GetProperty("minimumDotNet").GetString();
		var floorVersion = Version.Parse(floor!);

		var offenders = new List<string>();

		foreach (var project in Directory.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories))
		{
			if (project.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				continue;

			var text = File.ReadAllText(project);
			var relative = Path.GetRelativePath(RepoRoot, project);

			foreach (Match element in Regex.Matches(text, @"<TargetFrameworks?>([^<]+)</TargetFrameworks?>"))
			{
				foreach (var tfm in element.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
				{
					var parsed = Regex.Match(tfm.Trim(), @"^net(\d+)\.(\d+)");
					if (!parsed.Success)
						continue; // netstandard2.0 and friends are version-independent.

					var version = new Version(int.Parse(parsed.Groups[1].Value), int.Parse(parsed.Groups[2].Value));
					if (version < floorVersion)
						offenders.Add($"{relative} targets {tfm.Trim()}");
				}
			}
		}

		Assert.True(
			offenders.Count == 0,
			$"These projects target below the .NET {floor} floor: " + string.Join(", ", offenders));
	}

	[Fact]
	public void EveryProjectReferencedBySolutionExists()
	{
		var solution = ReadRepoFile("Maui.Tizen.slnx");

		var missing = Regex.Matches(solution, @"Path=""([^""]+\.csproj)""")
			.Select(m => m.Groups[1].Value)
			.Where(p => !File.Exists(Path.Combine(RepoRoot, p.Replace('/', Path.DirectorySeparatorChar))))
			.ToList();

		Assert.True(missing.Count == 0, "Solution references missing projects: " + string.Join(", ", missing));
	}

	[Fact]
	public void NoOrphanProjectFilesExistOutsideTheSolution()
	{
		// The history import pulled in `GraphicsTester.Skia.Tizen.csproj`, which cannot
		// load here: its TFM is `$(_MauiDotNetTfm)-tizen` (a dotnet/maui property that
		// does not exist in this repository, so the TFM evaluates to the malformed
		// "-tizen") and both of its ProjectReferences point at projects that were never
		// imported. A folder-level build or an IDE project scan would load it and fail
		// with errors that have nothing to do with this repository.
		//
		// It is parked as `.csproj.orphan` — file and history intact, invisible to project
		// discovery. This test stops another orphan slipping in unnoticed.
		var solution = ReadRepoFile("Maui.Tizen.slnx");
		var referenced = Regex.Matches(solution, @"Path=""([^""]+\.csproj)""")
			.Select(m => m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var orphans = Directory
			.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Select(p => Path.GetRelativePath(RepoRoot, p))
			.Where(p => !referenced.Contains(p))
			.ToList();

		Assert.True(
			orphans.Count == 0,
			"These project files are not in Maui.Tizen.slnx. Either add them, or park them as "
				+ "'.csproj.orphan' and document them in samples/README.md: "
				+ string.Join(", ", orphans));
	}

	[Fact]
	public void PublicApiAnalyzerIsReferencedSoBaselinesAreEnforced()
	{
		// PublicAPI.Shipped/Unshipped files travel with every package project. Without the
		// analyzer they are inert text and the API surface this repository exists to
		// preserve could drift silently.
		var props = ReadRepoFile("eng/targets/TizenPackage.props");

		Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", props);
		Assert.Matches(
			new Regex(@"Microsoft\.CodeAnalysis\.PublicApiAnalyzers""\s+PrivateAssets=""all"""),
			props);
	}

	[Fact]
	public void SourceLinkIsNotPinnedBecauseTheSdkProvidesIt()
	{
		// The .NET SDK has bundled SourceLink since .NET 8. An explicit pin is redundant
		// and risks holding an older version than the SDK ships.
		//
		// Asserts on the PackageVersion element rather than the bare package name: the
		// file explains this decision in a comment, and a substring check fails on its own
		// documentation.
		Assert.DoesNotMatch(
			new Regex(@"<PackageVersion\s+Include=""Microsoft\.SourceLink"),
			ReadRepoFile("Directory.Packages.props"));
	}

	[Fact]
	public void NoDeadCompileUpdateItemsRemain()
	{
		// `<Compile Update="**/*.Tizen.cs" />` with no metadata changes nothing, and every
		// package project sets EnableDefaultCompileItems=false, so there were no items to
		// update in the first place. It read as meaningful configuration while doing
		// nothing at all. Source inclusion is explicit, per project.
		var targets = ReadRepoFile("Directory.Build.targets");
		Assert.DoesNotMatch(new Regex(@"<Compile\s+Update="), targets);
	}

	[Fact]
	public void AspNetCoreDependenciesUseTheirOwnVersionLine()
	{
		// Microsoft.AspNetCore.* are ASP.NET Core packages and do not share MAUI's version
		// stamp. Pinning them to MAUI's 11.0.0-preview.7.26418.3 produced NU1603 (that
		// version does not exist for those packages) followed by NU1109 downgrade errors
		// across the BlazorWebView graph.
		//
		// The correct version is what Microsoft.AspNetCore.Components.WebView.Maui declares
		// in its own nuspec, which also matches the SDK build pinned in global.json.
		var packages = ReadRepoFile("Directory.Packages.props");

		var mauiStamp = Regex.Match(packages, @"Include=""Microsoft\.Maui\.Core"" Version=""([^""]+)""");
		Assert.True(mauiStamp.Success, "Directory.Packages.props must pin Microsoft.Maui.Core");

		foreach (Match m in Regex.Matches(packages, @"Include=""(Microsoft\.AspNetCore\.[^""]+|Microsoft\.JSInterop)"" Version=""([^""]+)"""))
		{
			var id = m.Groups[1].Value;
			var version = m.Groups[2].Value;

			// The .Maui bridge package IS a MAUI package and legitimately shares the stamp.
			if (id.EndsWith(".Maui", StringComparison.Ordinal))
				continue;

			Assert.True(
				version != mauiStamp.Groups[1].Value,
				$"{id} is pinned to MAUI's version stamp ({version}); ASP.NET Core packages "
					+ "are on their own version line and that version does not exist for them.");
		}
	}
}
