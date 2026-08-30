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

	[Fact]
	public void BaselinePinRecordsWhatItExcludes()
	{
		// A pin is only genuinely reproducible if what it EXCLUDES is written down too.
		//
		// sourceBaseline (ee4d06cde6) is 4 commits after requiredAncestor, none touching
		// Tizen. But net11.0 continued past the pin, and three later commits do - all of
		// them touching only src/Controls/src/Core/PublicAPI/net-tizen/PublicAPI.Unshipped.txt.
		// So the Controls net-tizen Unshipped baseline sits three API additions behind
		// current net11.0: relevant to API baseline diffing, not to source migration.
		//
		// This survived several review-fix rounds by luck rather than design. Whoever
		// regenerates API baselines needs it, and it is the kind of prose that quietly
		// disappears in a rebase, so it is pinned here.
		var gap = ReadRepoJson("eng/baselines.json")
			.GetProperty("source").GetProperty("sourceBaseline").GetProperty("knownGapAfterThisPin");

		var commits = gap.GetProperty("commits").EnumerateArray().ToList();
		Assert.Equal(3, commits.Count);

		var pullRequests = commits.Select(c => c.GetProperty("pullRequest").GetInt32()).ToHashSet();
		Assert.Equal(new HashSet<int> { 37420, 37671, 37755 }, pullRequests);

		// The characterisation matters as much as the list: these are API surface
		// declarations, not imported implementation. Losing that distinction would make
		// the gap look like missing code.
		var provenance = ReadRepoFile("PROVENANCE.md");
		Assert.Contains("What the pin excludes", provenance);
		Assert.Contains("PublicAPI.Unshipped.txt", provenance);
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
	public void MauiPackageVersionsMatchTheDeclaredDevelopmentBaseline()
	{
		// Directory.Packages.props and eng/baselines.json both state which MAUI package set
		// this repository builds against. They are edited at different times for different
		// reasons, so they drift - and the symptom (API baselines generated against one
		// version while the build consumes another) is slow and confusing to diagnose.
		var baseline = ReadRepoJson("eng/baselines.json")
			.GetProperty("source").GetProperty("developmentPackageBaseline")
			.GetProperty("version").GetString();

		var packages = ReadRepoFile("Directory.Packages.props");

		// Microsoft.Maui.DevFlow.* is deliberately exempt. Those packages come from
		// dotnet/maui-labs, not from the MAUI product build, and have their own independent
		// version line (0.1.0-preview.*). Holding them to the MAUI baseline would be asserting
		// that two unrelated release trains ship in lockstep, which they do not.
		var mismatched = Regex.Matches(packages, @"Include=""(Microsoft\.Maui\.[^""]+)"" Version=""([^""]+)""")
			.Where(m => !m.Groups[1].Value.StartsWith("Microsoft.Maui.DevFlow.", StringComparison.Ordinal))
			.Where(m => m.Groups[2].Value != baseline)
			.Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value}")
			.ToList();

		Assert.True(
			mismatched.Count == 0,
			$"These MAUI packages do not match developmentPackageBaseline ({baseline}): "
				+ string.Join(", ", mismatched));
	}

	[Fact]
	public void AspNetCoreFloorMatchesTheDeclaredDependencyFloor()
	{
		// The ASP.NET Core floor is declared by WebView.Maui's own nuspec and does NOT
		// track the MAUI stamp - it stayed at 26381.103 across the MAUI bump from
		// 26418.3 to 26426.4. Recorded in baselines.json so a future bump has something
		// to check against rather than an assumption to make.
		var floor = ReadRepoJson("eng/baselines.json")
			.GetProperty("source").GetProperty("developmentPackageBaseline")
			.GetProperty("aspNetCoreDependencyFloor").GetProperty("version").GetString();

		var packages = ReadRepoFile("Directory.Packages.props");

		foreach (Match m in Regex.Matches(packages, @"Include=""(Microsoft\.AspNetCore\.[^""]+|Microsoft\.JSInterop)"" Version=""([^""]+)"""))
		{
			// The .Maui bridge package IS a MAUI package and legitimately uses the MAUI stamp.
			if (m.Groups[1].Value.EndsWith(".Maui", StringComparison.Ordinal))
				continue;

			Assert.True(
				m.Groups[2].Value == floor,
				$"{m.Groups[1].Value} is pinned to {m.Groups[2].Value} but the declared "
					+ $"ASP.NET Core floor is {floor}.");
		}
	}

	[Fact]
	public void ReadmePackItemIsDeclaredAfterProjectEvaluation()
	{
		// The README <None Pack="true"> item belongs in Directory.Build.targets, not
		// Directory.Build.props.
		//
		// It is conditioned on $(IsPackable), which shipping projects opt into from their
		// own body. From .props that still works - MSBuild evaluates all properties,
		// including the project body, in an earlier pass than any item - and a
		// shipping-shaped project does pack with README.md present. But two reviewers
		// independently read the .props placement as an NU5039 bug, because it only works
		// if you know the multi-pass rule.
		//
		// Correctness that depends on recalling evaluation-pass ordering is correctness
		// nobody can review at a glance, so it lives after the project body where it is
		// obviously right. eng/tests/PackReadmeProbe pins the behaviour itself.
		var pack = new Regex(@"<None\s+Include=""\$\(RepositoryRoot\)README\.md""[^>]*Pack=""true""");

		Assert.Matches(pack, ReadRepoFile("Directory.Build.targets"));
		Assert.DoesNotMatch(pack, ReadRepoFile("Directory.Build.props"));
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

		// tests/fixtures/** is exempt. Those projects are INPUTS to tests - the MSBuild
		// behaviour suite invokes `dotnet msbuild` on them directly and asserts on evaluated
		// property values. They ship their own empty Directory.Build.props/targets specifically
		// so they do NOT inherit this repository's conventions; adding them to the solution
		// would build them as solution members and destroy the isolation that makes their
		// results attributable to the targets under test.
		var fixtures = $"{Path.DirectorySeparatorChar}fixtures{Path.DirectorySeparatorChar}";

		var orphans = Directory
			.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(p => !p.Contains(fixtures, StringComparison.Ordinal))
			.Where(p => !IsTemplateContent(p))
			.Select(p => Path.GetRelativePath(RepoRoot, p))
			.Where(p => !referenced.Contains(p))
			.ToList();

		Assert.True(
			orphans.Count == 0,
			"These project files are not in Maui.Tizen.slnx. Either add them, or park them as "
				+ "'.csproj.orphan' and document them in samples/README.md: "
				+ string.Join(", ", orphans));
	}

	/// <summary>
	/// A `dotnet new` template's project file is package CONTENT, not a project.
	/// </summary>
	/// <remarks>
	/// It cannot be added to the solution: it carries unresolved template placeholders (its TFM is
	/// literally <c>net11.0-tizenTIZEN_PLATFORM_VERSION</c>), so loading it fails by design. It
	/// equally must not be parked as <c>.csproj.orphan</c>, because the whole point is that
	/// instantiating the template produces a real <c>.csproj</c>.
	///
	/// The orphan rule's concern - that a folder-level build or IDE scan would try to load it -
	/// does not apply, because Maui.Tizen.Templates sets EnableDefaultItems/EnableDefaultCompileItems
	/// to false and packs <c>templates/**</c> purely as content. That is asserted by
	/// <c>TemplateIsShippedAsContentAndNotBuilt</c> below, so this exclusion cannot silently widen
	/// into "any csproj under any folder named templates".
	/// </remarks>
	static bool IsTemplateContent(string projectPath)
	{
		for (var directory = Path.GetDirectoryName(projectPath); directory is not null; directory = Path.GetDirectoryName(directory))
		{
			// The marker is the template configuration itself, not the folder name.
			if (Directory.Exists(Path.Combine(directory, ".template.config")))
				return true;

			if (string.Equals(directory, RepoRoot, StringComparison.Ordinal))
				break;
		}

		return false;
	}

	[Fact]
	public void TemplateIsShippedAsContentAndNotBuilt()
	{
		var templatesProject = ReadRepoFile(Path.Combine("src", "Maui.Tizen.Templates", "Maui.Tizen.Templates.csproj"));

		// Nothing under templates/ may be compiled or globbed as project items.
		Assert.Contains("<EnableDefaultItems>false</EnableDefaultItems>", templatesProject);
		Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", templatesProject);
		Assert.Contains("<PackageType>Template</PackageType>", templatesProject);
		Assert.Contains("templates\\**\\*", templatesProject.Replace('/', '\\'));

		// And every template project file must sit next to a .template.config, which is what
		// makes the orphan exclusion above legitimate.
		var templateRoot = Path.Combine(RepoRoot, "src", "Maui.Tizen.Templates", "templates");
		if (!Directory.Exists(templateRoot))
			return;

		foreach (var project in Directory.EnumerateFiles(templateRoot, "*.csproj", SearchOption.AllDirectories))
		{
			Assert.True(
				Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, ".template.config")),
				$"'{Path.GetRelativePath(RepoRoot, project)}' is under templates/ but has no .template.config beside it, "
					+ "so it is an orphan project rather than template content.");
		}
	}

	[Fact]
	public void PublicApiAnalyzerIsReferencedSoBaselinesAreEnforced()
	{
		// Without the analyzer, PublicAPI files are inert text and the API surface this
		// repository exists to preserve could drift silently.
		var props = ReadRepoFile("eng/targets/TizenPackage.props");

		Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", props);
		Assert.Matches(
			new Regex(@"Microsoft\.CodeAnalysis\.PublicApiAnalyzers""\s+PrivateAssets=""all"""),
			props);
	}

	[Fact]
	public void PublicApiBaselinesAreNotAttachedAutomatically()
	{
		// A `PublicAPI/**` glob in shared props is wrong here in two independent ways, both
		// measured before this test was written:
		//
		// 1. It attaches upstream's MONOLITHIC per-assembly baseline to our SPLIT assembly.
		//    src/Maui.Tizen.Core's imported baseline has 3,268 entries; only 447 are the
		//    Microsoft.Maui.Platform types this assembly will contain. The rest become
		//    RS0017 the moment the project compiles - thousands of errors describing a
		//    mismatch between two different assemblies, not an API regression.
		//
		// 2. It silently matched NOTHING for half the projects. Rooted at the project
		//    directory, it found 2 items each for Core/Essentials/BlazorWebView and 0 each
		//    for Controls/Maps/Graphics, whose baselines are nested a level deeper because
		//    those packages merge two upstream assemblies. Enforcement that is silently
		//    absent is worse than none, because it looks present.
		//
		// Opt-in is per project, with a baseline describing that assembly.
		var props = ReadRepoFile("eng/targets/TizenPackage.props");

		Assert.DoesNotMatch(new Regex(@"<AdditionalFiles\s+Include=""PublicAPI/\*\*"), props);
	}

	[Fact]
	public void NoProjectConsumesTheImportedUpstreamBaselines()
	{
		// The imported src/**/PublicAPI/** files are provenance fixtures recording what
		// upstream shipped for net-tizen. They are not this repository's API contract, and
		// pointing a compiled assembly at one reintroduces the RS0017 flood above.
		var offenders = new List<string>();

		foreach (var project in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
		{
			if (project.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				continue;

			var text = File.ReadAllText(project);
			foreach (Match m in Regex.Matches(text, @"<AdditionalFiles\s+Include=""([^""]+)"""))
			{
				// net-tizen is upstream's directory name for the imported baselines.
				if (m.Groups[1].Value.Contains("net-tizen", StringComparison.OrdinalIgnoreCase))
					offenders.Add($"{Path.GetRelativePath(RepoRoot, project)} -> {m.Groups[1].Value}");
			}
		}

		Assert.True(
			offenders.Count == 0,
			"These projects point the analyzer at imported upstream baselines, which describe a "
				+ "different (unsplit) assembly and will produce RS0017: " + string.Join(", ", offenders));
	}

	[Fact]
	public void PublicApiAnalyzerDiagnosticsAreNotGloballySuppressed()
	{
		// RS0016 (public API not in the baseline) and RS0017 (baseline entry not in the
		// assembly) are the entire value of the analyzer. Silencing them repo-wide to make
		// a mismatched baseline quiet would discard the enforcement while leaving the
		// wiring in place to look reassuring.
		var editorconfig = ReadRepoFile(".editorconfig");

		foreach (var rule in new[] { "RS0016", "RS0017" })
			Assert.Matches(new Regex($@"dotnet_diagnostic\.{rule}\.severity\s*=\s*error"), editorconfig);

		// A blanket NoWarn in shared props would defeat it just as effectively.
		foreach (var file in new[] { "Directory.Build.props", "eng/targets/TizenPackage.props" })
		{
			foreach (Match m in Regex.Matches(ReadRepoFile(file), @"<NoWarn>([^<]*)</NoWarn>"))
			{
				Assert.DoesNotContain("RS0016", m.Groups[1].Value);
				Assert.DoesNotContain("RS0017", m.Groups[1].Value);
			}
		}
	}

	[Fact]
	public void WorkloadDetectionIsRestrictedToTheCurrentFeatureBand()
	{
		// An unrestricted `sdk-manifests/*/samsung.net.sdk.tizen/` glob treats a Samsung
		// workload installed for .NET 9 or .NET 10 as satisfying net11 - the gate lifts and
		// the build fails much later with an unrelated-looking missing-reference-pack error.
		//
		// The pattern is 11.0.* rather than the exact band, because this SDK
		// (11.0.100-preview.7.26381.103) ships manifests under BOTH 11.0.100-preview.6 and
		// 11.0.100-preview.7: bands drift within a feature line, and pinning the preview
		// segment would be a false negative on a correctly configured machine.
		Assert.Contains("TizenWorkloadBandPattern", ReadRepoFile("Directory.Build.props"));

		var targets = ReadRepoFile("Directory.Build.targets");
		Assert.DoesNotMatch(new Regex(@"sdk-manifests/\)?\*/samsung\.net\.sdk\.tizen"), targets);
		Assert.Contains("$(TizenWorkloadBandPattern)/samsung.net.sdk.tizen", targets);
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

	// ---------------------------------------------------------------------
	// Lane integrity
	// ---------------------------------------------------------------------

	/// <summary>
	/// Every explicit failure in the workload-free lane must also be counted.
	/// </summary>
	/// <remarks>
	/// The lane's exit code is driven by a $FAILURES counter. One branch - the README probe
	/// being unable to read the produced package - printed a red FAIL line and did not
	/// increment it, so the script reported a failure and exited 0. A check that reports but
	/// does not enforce is worse than no check, because the red line trains people to ignore it.
	///
	/// The `fail` call inside the shared `check` helper is exempt: `check` increments the
	/// counter itself.
	/// </remarks>
	[Fact]
	public void EveryExplicitFailureInTheWorkloadFreeLaneIsCounted()
	{
		var lines = ReadRepoFile("eng/build-workload-free.sh").Replace("\r\n", "\n").Split('\n');

		var uncounted = new List<string>();

		for (var i = 0; i < lines.Length; i++)
		{
			var trimmed = lines[i].Trim();

			// The definition of `fail`, its use inside `check`, and the final summary line.
			if (!trimmed.StartsWith("fail \"", StringComparison.Ordinal))
				continue;
			if (trimmed.Contains("$label", StringComparison.Ordinal))
				continue;
			if (trimmed.Contains("check(s) failed", StringComparison.Ordinal))
				continue;

			var next = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;

			if (!next.StartsWith("FAILURES=", StringComparison.Ordinal))
				uncounted.Add($"line {i + 1}: {trimmed}");
		}

		Assert.True(
			uncounted.Count == 0,
			"These failures are reported but not counted, so the lane would exit 0: "
				+ string.Join("; ", uncounted));
	}

	/// <summary>
	/// MSBuild fixtures must use the repository's own approved feeds.
	/// </summary>
	/// <remarks>
	/// The fixtures reference the pinned Microsoft.Maui.Resizetizer, which is a net11
	/// prerelease that exists only on the dotnet11 feed. A fixture NuGet.config listing only
	/// nuget.org therefore passed on any machine with a warm global packages folder and failed
	/// on a cold CI agent with NU1101 - a fixture defect that reads as a test failure.
	/// </remarks>
	[Fact]
	public void MSBuildFixturesUseTheRepositoryPackageSources()
	{
		var builder = ReadRepoFile(Path.Combine("tests", "UnitTests", "MSBuildProjectBuilder.cs"));

		Assert.Contains("ReadRepositoryNuGetConfig()", builder);
		Assert.DoesNotContain("<packageSources", builder);
		Assert.DoesNotContain("api.nuget.org", builder);
	}

	/// <summary>
	/// A Tizen project must not be able to fall back to a neutral target framework by accident.
	/// </summary>
	[Fact]
	public void ANonTizenTargetFrameworkOnATizenProjectIsAnError()
	{
		var targets = ReadRepoFile("Directory.Build.targets");

		Assert.Contains("MAUITIZEN0002", targets);
		Assert.Contains("ValidateTizenTargetFramework", targets);

		// The gate has to run before anything that would consume the wrong framework.
		var gate = Regex.Match(
			targets,
			@"<Target Name=""ValidateTizenTargetFramework""(.*?)>",
			RegexOptions.Singleline);

		Assert.True(gate.Success, "Directory.Build.targets must declare the ValidateTizenTargetFramework target.");
		Assert.Contains("Restore", gate.Groups[1].Value);
		Assert.Contains("Build", gate.Groups[1].Value);
	}

	/// <summary>
	/// Full-framework MSBuild execution stays a real CI lane rather than a local simulation.
	/// </summary>
	/// <remarks>
	/// The build tasks load native SkiaSharp differently on .NET Framework hosts (Visual Studio,
	/// MSBuild.exe), where there is no NativeLibrary resolver. That path cannot be executed from
	/// this test suite - the tests run on .NET, and MSBuild.exe does not exist on macOS or Linux -
	/// so it is executed on a Windows agent instead of being approximated with a stub host or a
	/// conditional skip. This test exists so the lane cannot quietly disappear and leave the gap
	/// unacknowledged.
	/// </remarks>
	[Fact]
	public void FullFrameworkExecutionIsCoveredByADedicatedCIJob()
	{
		var workflow = ReadRepoFile(Path.Combine(".github", "workflows", "ci.yml"));

		Assert.Contains("windows-full-framework:", workflow);
		Assert.Contains("runs-on: windows-latest", workflow);

		// It has to invoke msbuild.exe; `dotnet build` would run on .NET and prove nothing.
		Assert.Matches(new Regex(@"^\s*msbuild ", RegexOptions.Multiline), workflow);
	}

	/// <summary>
	/// The full-framework lane must consume the PACKAGE, not the build output directory.
	/// </summary>
	/// <remarks>
	/// The lane previously built the tasks and pointed <c>_MauiTizenBuildTasksAssembly</c> at
	/// artifacts/bin/.../netstandard2.0, which carries the whole runtime closure because of
	/// CopyLocalLockFileAssemblies. Every dependency therefore resolved from beside the task
	/// assembly regardless of whether the package shipped it - and the package did not ship
	/// System.Memory and its three companions, which .NET Framework MSBuild does not provide.
	/// The only lane that could have observed the gap was arranged so that it could not, which
	/// is the failure mode this test pins shut.
	/// </remarks>
	[Fact]
	public void TheFullFrameworkLaneConsumesTheProducedPackage()
	{
		var workflow = ReadRepoFile(Path.Combine(".github", "workflows", "ci.yml"));

		var job = workflow[workflow.IndexOf("windows-full-framework:", StringComparison.Ordinal)..];

		// Comment lines are stripped: the job explains the defect below in prose, and naming a
		// thing in order to say it must not be used is not the same as using it.
		var executable = string.Join(
			'\n',
			job.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal)));

		Assert.Contains("dotnet pack src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj", executable);
		Assert.Contains("""<PackageReference Include="Maui.Tizen.Build.Tasks" Version="$version" />""", executable);

		// No redirect to a build output folder, and no import of the source-tree targets: both
		// would bypass exactly what the package has to get right.
		Assert.DoesNotContain("_MauiTizenBuildTasksAssembly", executable);
		Assert.DoesNotContain("Maui.Tizen.Build.Tasks.targets", executable);
	}

	// ---------------------------------------------------------------------
	// Provenance wiring
	//
	// The DECISION these guard is executed for real by PackageInputCleanlinessTests, which runs
	// eng/check-package-inputs-clean.sh against purpose-built repositories. What is left to pin is
	// the WIRING: that the lane consults the gate at all, that the local-validation override
	// cannot survive into CI, and that the container run is handed a verified revision instead of
	// silently losing its repository identity. Those are properties of how the scripts are
	// assembled, so they are asserted as such.
	// ---------------------------------------------------------------------

	/// <summary>
	/// The lane must consult the cleanliness gate before claiming provenance.
	/// </summary>
	[Fact]
	public void TheLaneGatesProvenanceOnCommittedPackageInputs()
	{
		var lane = ReadRepoFile("eng/build-workload-free.sh");

		Assert.Contains("eng/check-package-inputs-clean.sh", lane);

		// Fail-closed: a dirty tree without the override is a counted failure, not a warning.
		Assert.Contains("package inputs do not match HEAD, so the packages cannot claim its provenance", lane);
	}

	/// <summary>
	/// The dirty-provenance override must be refused on CI and release runs.
	/// </summary>
	/// <remarks>
	/// An escape hatch that works everywhere is not an escape hatch, it is the new default. This
	/// one exists so an in-progress patch can still be validated locally; the moment it can be set
	/// in CI or on a publishing run it stops being a provenance gate at all.
	/// </remarks>
	[Fact]
	public void TheDirtyProvenanceOverrideIsRefusedOnAutomatedRuns()
	{
		var lane = ReadRepoFile("eng/build-workload-free.sh");

		var refusal = Regex.Match(
			lane,
			@"if \[\[ ""\$ALLOW_DIRTY_PROVENANCE"" == ""1"" && \$IS_AUTOMATED_RUN -eq 1 \]\]; then(?<body>.*?)\nfi",
			RegexOptions.Singleline);

		Assert.True(refusal.Success, "eng/build-workload-free.sh no longer refuses the dirty-provenance override on automated runs.");
		Assert.Contains("the override is refused", refusal.Groups["body"].Value);
		Assert.Contains("ALLOW_DIRTY_PROVENANCE=0", refusal.Groups["body"].Value);

		// CI, GitHub Actions and an explicit release run all count as automated.
		Assert.Contains("${CI:-}", lane);
		Assert.Contains("${GITHUB_ACTIONS:-}", lane);
		Assert.Contains("${MAUI_TIZEN_RELEASE:-0}", lane);
	}

	/// <summary>
	/// The container lane must be handed a verified revision, and must not be handed a broken
	/// repository instead.
	/// </summary>
	/// <remarks>
	/// <para>
	/// eng/run-linux-checks.sh copies the working tree into a container, and the lane inside it
	/// needs the repository identity to stamp packages with. Excluding only <c>.git/</c> was
	/// subtly wrong for this repository: development happens in git WORKTREES, where <c>.git</c>
	/// is a FILE pointing into the main repository's <c>.git/worktrees</c>. A directory-only
	/// pattern let that file through, and git inside the container then followed a pointer to a
	/// path that does not exist - which fails in a way that reads as a broken checkout rather than
	/// a missing exclusion.
	/// </para>
	/// <para>
	/// Both spellings are excluded and the revision is resolved and verified on the host instead.
	/// The cleanliness VERDICT travels with it, because passing the revision alone would just move
	/// the false provenance claim across the container boundary.
	/// </para>
	/// </remarks>
	[Fact]
	public void TheLinuxContainerRunReceivesAVerifiedRevision()
	{
		var script = ReadRepoFile("eng/run-linux-checks.sh");

		Assert.Contains("--exclude '.git'", script);
		Assert.Contains("--exclude '.git/'", script);

		Assert.Contains("MAUI_TIZEN_SOURCE_REVISION=$SOURCE_REVISION", script);
		Assert.Contains("MAUI_TIZEN_SOURCE_REVISION_STATE=$SOURCE_REVISION_STATE", script);
		Assert.Contains("eng/check-package-inputs-clean.sh", script);

		// And the lane consumes exactly those, rejecting anything that is not a full commit id.
		var lane = ReadRepoFile("eng/build-workload-free.sh");
		Assert.Contains("MAUI_TIZEN_SOURCE_REVISION:-", lane);
		Assert.Contains("MAUI_TIZEN_SOURCE_REVISION_STATE:-", lane);
		Assert.Contains("^[0-9a-f]{40}$", lane);
	}

	/// <summary>
	/// Every script the provenance story depends on must be present and executable.
	/// </summary>
	[Theory]
	[InlineData("eng/check-package-inputs-clean.sh")]
	[InlineData("eng/run-linux-checks.sh")]
	[InlineData("eng/build-workload-free.sh")]
	public void ProvenanceScriptsAreValidShell(string relativePath)
	{
		var path = Path.Combine(RepoRoot, relativePath);
		Assert.True(File.Exists(path), $"{relativePath} is missing.");

		var startInfo = new System.Diagnostics.ProcessStartInfo("bash")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		startInfo.ArgumentList.Add("-n");
		startInfo.ArgumentList.Add(path);

		using var process = System.Diagnostics.Process.Start(startInfo)!;
		var log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();

		Assert.True(process.ExitCode == 0, $"{relativePath} is not valid shell:{Environment.NewLine}{log}");
	}
}
