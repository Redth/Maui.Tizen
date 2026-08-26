using System.Text.RegularExpressions;

namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Checks that a release actually has the artifacts it claims to have been validated against.
/// </summary>
/// <remarks>
/// <para>
/// Most of the packaging, consumer-restore and visual-baseline suites skip when their inputs do
/// not exist, which is right for a pull request and dangerous for a release: a release could
/// otherwise be waved through by suites that all skipped.
/// </para>
/// <para>
/// These tests invert that. Under <c>MAUI_TIZEN_RELEASE_VALIDATION=1</c> a missing artifact is a
/// failure rather than a skip, so "everything skipped" cannot be mistaken for "everything passed".
/// </para>
/// </remarks>
public partial class ReleaseReadinessTests
{
    /// <summary>Set by the release workflow; absent on ordinary runs.</summary>
    public static bool IsReleaseValidation =>
        Environment.GetEnvironmentVariable("MAUI_TIZEN_RELEASE_VALIDATION") == "1";

    static void SkipUnlessRelease() =>
        ValidationSkip.When(
            !IsReleaseValidation,
            "Release-only gate. Set MAUI_TIZEN_RELEASE_VALIDATION=1 to run it; the release " +
            "workflow does this automatically. See docs/validation/ci.md.");

    static string PackagesDirectory => Path.Combine(RepoLayout.Root, "artifacts", "packages");

    static string DeviceResultsDirectory => Path.Combine(RepoLayout.Root, "artifacts", "device-results");

    [Fact]
    public void EveryDeclaredPackageWasProduced()
    {
        SkipUnlessRelease();

        var declared = PackageContentContract.EnumerateDeclaredPackageIds();

        Assert.True(
            declared.Count > 0,
            "No package-content contracts are declared, so a release would ship unverified packages.");

        Assert.True(
            Directory.Exists(PackagesDirectory),
            $"'{RepoLayout.Relative(PackagesDirectory)}' does not exist. A release must build and " +
            "pack the shipping packages before validation.");

        var missing = declared
            .Where(id => !Directory
                .EnumerateFiles(PackagesDirectory, $"{id}.*.nupkg")
                .Any(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"""
             These declared packages were not produced for the release:
             {string.Join(Environment.NewLine, missing.Select(m => "    " + m))}

             Every package with a content contract must be packed, or the contract is asserting
             nothing.
             """);
    }

    [Fact]
    public void EveryProducedPackageSatisfiesItsContentContract()
    {
        SkipUnlessRelease();

        foreach (var id in PackageContentContract.EnumerateDeclaredPackageIds())
        {
            using var package = NuPkg.OpenFromDirectory(PackagesDirectory, id);
            var evaluation = PackageContentContract.Load(id).Evaluate(package.Entries);

            Assert.True(evaluation.Passed, evaluation.Describe(package.Entries));
        }
    }

    [Fact]
    public void EveryRequiredVisualBaselineExists()
    {
        SkipUnlessRelease();
        ValidationSkip.When(!ControlCatalog.Exists, "The control catalog manifest is not present.");

        var apiLevel = RepositoryBaselines.Target.TizenFxApiLevel;
        var missing = new List<string>();

        foreach (var profile in TizenProfiles.ReleaseGatingProfiles)
        {
            foreach (var theme in profile.Themes)
            {
                foreach (var density in profile.Densities)
                {
                    var variant = new BaselineVariant(profile.Id, theme, density);

                    foreach (var catalogCase in ControlCatalog.BaselineCasesFor(profile.Id))
                    {
                        var image = VisualBaselines.ImagePath(variant, apiLevel, catalogCase.Id);

                        if (!File.Exists(image))
                            missing.Add(RepoLayout.Relative(image));
                    }
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"""
             {missing.Count} required visual baseline(s) are missing for this release:
             {string.Join(Environment.NewLine, missing.Take(25).Select(m => "    " + m))}
             {(missing.Count > 25 ? $"    ... and {missing.Count - 25} more" : string.Empty)}

             Every catalog case with capturesBaseline=true must have an image for every
             release-gating profile, theme and density. See docs/validation/visual-baselines.md.
             """);
    }

    [Fact]
    public void EveryRequiredProfileReportedADeviceResult()
    {
        SkipUnlessRelease();

        Assert.True(
            Directory.Exists(DeviceResultsDirectory),
            $"'{RepoLayout.Relative(DeviceResultsDirectory)}' does not exist. The release workflow " +
            "must download the per-profile device results before this gate runs.");

        foreach (var profile in TizenProfiles.ReleaseGatingProfiles)
        {
            var path = Path.Combine(DeviceResultsDirectory, $"device-result-{profile.Id}.txt");

            Assert.True(
                File.Exists(path),
                $"Profile '{profile.Id}' gates a release but produced no device result file.");

            var content = File.ReadAllText(path);

            Assert.True(
                content.Contains("lane_available=true", StringComparison.Ordinal),
                $"Profile '{profile.Id}' did not run on hardware:{Environment.NewLine}{content}");

            Assert.True(
                content.Contains("status=pass", StringComparison.Ordinal),
                $"Profile '{profile.Id}' did not pass:{Environment.NewLine}{content}");
        }
    }

    [Fact]
    public void ReleaseValidationFlagIsWiredIntoTheReleaseWorkflow()
    {
        // Runs on every build, not just releases. If the workflow stopped exporting the flag,
        // every release-only gate above would silently skip and the release would sail through.
        var workflow = Path.Combine(RepoLayout.Root, ".github", "workflows", "tizen-device-validation.yml");
        Assert.True(File.Exists(workflow), $"Missing {RepoLayout.Relative(workflow)}");

        var text = File.ReadAllText(workflow);

        Assert.Contains("MAUI_TIZEN_RELEASE_VALIDATION", text, StringComparison.Ordinal);
        Assert.Contains("evaluate-release-gate.sh", text, StringComparison.Ordinal);
    }
}

/// <summary>
/// Guards against workflows referencing things that do not exist.
/// </summary>
/// <remarks>
/// A code review found the device workflow building
/// <c>samples/Maui.Tizen.Catalog/Maui.Tizen.Catalog.csproj</c>, which has never existed. Because
/// the lane is unavailable, that step never ran, so the phantom path sat there looking plausible.
/// </remarks>
public partial class WorkflowReferenceTests
{
    [GeneratedRegex(@"(?<![\w./-])(?:eng|src|tests|samples)/[\w./-]+\.(?:csproj|sh|json|props|targets)")]
    private static partial Regex RepositoryPathPattern { get; }

    static IEnumerable<string> WorkflowFiles()
    {
        var directory = Path.Combine(RepoLayout.Root, ".github", "workflows");

        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.yml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
            : [];
    }

    [Fact]
    public void WorkflowsReferenceOnlyFilesThatExist()
    {
        var workflows = WorkflowFiles().ToList();
        Assert.NotEmpty(workflows);

        var missing = new List<string>();

        foreach (var workflow in workflows)
        {
            foreach (Match match in RepositoryPathPattern.Matches(File.ReadAllText(workflow)))
            {
                var relative = match.Value;
                var absolute = Path.Combine(RepoLayout.Root, relative.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                    missing.Add($"{Path.GetFileName(workflow)} -> {relative}");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"""
             Workflows reference repository paths that do not exist:
             {string.Join(Environment.NewLine, missing.Distinct().Select(m => "    " + m))}

             A step that builds a non-existent project looks plausible for as long as the job never
             runs, which for the device lane could be indefinitely.
             """);
    }

    [Fact]
    public void ScriptsInvokedByWorkflowsAreExecutable()
    {
        var missing = new List<string>();

        foreach (var workflow in WorkflowFiles())
        {
            foreach (Match match in RepositoryPathPattern.Matches(File.ReadAllText(workflow)))
            {
                if (!match.Value.EndsWith(".sh", StringComparison.Ordinal))
                    continue;

                var absolute = Path.Combine(RepoLayout.Root, match.Value.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(absolute))
                    continue;

                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(absolute) & UnixFileMode.UserExecute) == 0)
                    missing.Add(match.Value);
            }
        }

        Assert.True(
            missing.Count == 0,
            "These scripts are invoked by a workflow but are not executable: " +
            string.Join(", ", missing.Distinct()));
    }
}
