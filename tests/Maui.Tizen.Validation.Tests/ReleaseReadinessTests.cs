using System.Text.RegularExpressions;
using System.Text.Json;

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
            .Where(id => NuPkg.FindPackagePaths(PackagesDirectory, id).Count == 0)
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
            foreach (var density in profile.Densities)
            {
                var target = $"{profile.Id}-{density}";
                var path = Path.Combine(DeviceResultsDirectory, $"device-result-{target}.txt");

                Assert.True(
                    File.Exists(path),
                    $"Visual target '{target}' gates a release but produced no device result file.");

                var content = File.ReadAllText(path);

                Assert.True(
                    content.Contains("lane_available=true", StringComparison.Ordinal),
                    $"Visual target '{target}' did not run on hardware:{Environment.NewLine}{content}");

                Assert.True(
                    content.Contains("status=pass", StringComparison.Ordinal),
                    $"Visual target '{target}' did not pass:{Environment.NewLine}{content}");
            }
        }
    }

    [Fact]
    public void NoActiveEssentialsExternalApiBlockers()
    {
        SkipUnlessRelease();

        var path = Path.Combine(
            RepoLayout.Root,
            "eng",
            "validation",
            "essentials-external-blockers.json");
        Assert.True(File.Exists(path), $"Missing {RepoLayout.Relative(path)}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var active = document.RootElement
            .GetProperty("blockers")
            .EnumerateArray()
            .Where(blocker =>
                string.Equals(
                    blocker.GetProperty("status").GetString(),
                    "active",
                    StringComparison.OrdinalIgnoreCase))
            .Select(blocker => blocker.GetProperty("id").GetString())
            .ToList();

        Assert.True(
            active.Count == 0,
            $"Essentials still has active public-API blockers: {string.Join(", ", active)}. " +
            "See docs/validation/blockers.md.");
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

/// <summary>Release workflow supply-chain invariants that must remain fail-closed.</summary>
public class ReleaseWorkflowSecurityTests
{
    static string ReadWorkflow(string name) =>
        File.ReadAllText(Path.Combine(RepoLayout.Root, ".github", "workflows", name));

    [Fact]
    public void EveryExternalActionIsPinnedToAnImmutableCommit()
    {
        var workflowDirectory = Path.Combine(RepoLayout.Root, ".github", "workflows");
        var mutable = new List<string>();
        var pattern = new Regex(@"^\s*uses:\s+(?<target>\S+)", RegexOptions.Multiline);

        foreach (var workflow in Directory.EnumerateFiles(workflowDirectory, "*.yml"))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(workflow)))
            {
                var target = match.Groups["target"].Value;
                if (target.StartsWith("./", StringComparison.Ordinal))
                    continue;

                var at = target.LastIndexOf('@');
                var reference = at >= 0 ? target[(at + 1)..] : string.Empty;
                if (!Regex.IsMatch(reference, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
                    mutable.Add($"{Path.GetFileName(workflow)} -> {target}");
            }
        }

        Assert.True(
            mutable.Count == 0,
            "External actions must be immutable commit pins:" + Environment.NewLine +
            string.Join(Environment.NewLine, mutable.Select(item => "    " + item)));
    }

    [Fact]
    public void ReleasePolicyRequiresTheFullFrameworkBuildTaskLane()
    {
        const string context = "Build tasks under full-framework MSBuild (final lane)";
        var ci = ReadWorkflow("ci.yml");
        using var policy = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoLayout.Root, "eng", "release", "release-policy.json")));
        var required = policy.RootElement
            .GetProperty("requiredStatusChecks")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Contains($"name: {context}", ci, StringComparison.Ordinal);
        Assert.Contains(context, required);
    }

    [Fact]
    public void SharedPackageMetadataCarriesTheReleaseContractTags()
    {
        var props = File.ReadAllText(Path.Combine(RepoLayout.Root, "Directory.Build.props"));

        Assert.Contains(
            "<PackageTags>maui;tizen;dotnet;samsung</PackageTags>",
            props,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneBaselineIncludesThePublicBuildTasksAssembly()
    {
        var generator = File.ReadAllText(
            Path.Combine(
                RepoLayout.Root,
                "eng",
                "scripts",
                "generate-release-api-baseline.ps1"));
        var contract = File.ReadAllText(
            Path.Combine(RepoLayout.Root, "eng", "release", "release-contract.py"));

        Assert.Contains(
            "$normalized -ieq \"buildTransitive/$id.dll\"",
            generator,
            StringComparison.Ordinal);
        Assert.Contains(
            "f\"buildtransitive/{package_id}.dll\"",
            contract,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseSourceGateChecksTheExactShaAndSupportsOnlyConfiguredBranches()
    {
        var release = ReadWorkflow("release.yml");
        var contract = File.ReadAllText(
            Path.Combine(RepoLayout.Root, "eng", "release", "release-contract.py"));

        Assert.Contains("checks: read", release, StringComparison.Ordinal);
        Assert.Contains("commits/$REVIEWED_SHA/check-runs", release, StringComparison.Ordinal);
        Assert.Contains("verify-required-checks", release, StringComparison.Ordinal);
        Assert.Contains("--source-branch \"$SOURCE_BRANCH\"", release, StringComparison.Ordinal);
        Assert.Contains("- \"release/**\"", ReadWorkflow("ci.yml"), StringComparison.Ordinal);
        Assert.Contains("configured_release_branches", contract, StringComparison.Ordinal);
        Assert.Contains("servicingBranches", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseArtifactIsAttemptBoundAndPassedIntoReusableValidation()
    {
        var release = ReadWorkflow("release.yml");
        var device = ReadWorkflow("tizen-device-validation.yml");

        Assert.Contains("cancel-in-progress: false", release, StringComparison.Ordinal);
        Assert.Contains("${{ inputs.package_version }}", release, StringComparison.Ordinal);
        Assert.Contains("artifact-id", release, StringComparison.Ordinal);
        Assert.Contains("artifact-digest", release, StringComparison.Ordinal);
        Assert.Contains("-run-${GITHUB_RUN_ID}-attempt-${GITHUB_RUN_ATTEMPT}", release, StringComparison.Ordinal);
        Assert.Contains("unsigned_artifact_id:", release, StringComparison.Ordinal);
        Assert.Contains("unsigned_artifact_name:", release, StringComparison.Ordinal);
        Assert.Contains("unsigned_artifact_digest:", release, StringComparison.Ordinal);
        Assert.Contains("unsigned_manifest_sha256:", release, StringComparison.Ordinal);
        Assert.Contains("source_run_attempt:", release, StringComparison.Ordinal);

        Assert.Contains("artifact-ids: ${{ inputs.unsigned_artifact_id }}", device, StringComparison.Ordinal);
        Assert.Contains("--digest \"$RELEASE_ARTIFACT_DIGEST\"", device, StringComparison.Ordinal);
        Assert.Contains("--version \"$RELEASE_PACKAGE_VERSION\"", device, StringComparison.Ordinal);
        Assert.Contains("verify-installed-workload", device, StringComparison.Ordinal);
        Assert.DoesNotContain("tizen-device-lane.sh pack", device, StringComparison.Ordinal);
        Assert.DoesNotContain("shipping-packages", device, StringComparison.Ordinal);

        var combined = release + Environment.NewLine + device;
        Assert.Equal(
            Regex.Matches(combined, @"^\s+artifact-ids:", RegexOptions.Multiline).Count,
            Regex.Matches(
                combined,
                @"^\s+artifact-ids:[^\n]*\n\s+merge-multiple:\s+true$",
                RegexOptions.Multiline).Count);
    }

    [Fact]
    public void WorkflowExpressionsAreNotInterpolatedIntoShellScripts()
    {
        foreach (var workflowName in new[] { "release.yml", "tizen-device-validation.yml" })
        {
            var lines = ReadWorkflow(workflowName).Split('\n');
            var runIndent = -1;

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var indent = line.TakeWhile(char.IsWhiteSpace).Count();
                if (runIndent >= 0 && line.Trim().Length > 0 && indent <= runIndent)
                    runIndent = -1;

                if (line.TrimStart().StartsWith("run:", StringComparison.Ordinal))
                {
                    runIndent = indent;
                    continue;
                }

                Assert.False(
                    runIndent >= 0 && line.Contains("${{", StringComparison.Ordinal),
                    $"{workflowName}:{index + 1} interpolates a workflow expression directly into a script: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void ReusableDeviceWorkflowAuthenticatesItsCallerAndProtectsEverySelfHostedJob()
    {
        var workflow = ReadWorkflow("tizen-device-validation.yml");

        Assert.Contains("expected_repository=\"Redth/Maui.Tizen\"", workflow, StringComparison.Ordinal);
        Assert.Contains("CALLER_WORKFLOW_REF", workflow, StringComparison.Ordinal);
        Assert.Contains("REF_PROTECTED", workflow, StringComparison.Ordinal);
        Assert.Contains("servicingBranches", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "release device validation source ref is not policy-approved",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "[[ \"$REQUESTED_RUN_ATTEMPT\" == \"$GITHUB_RUN_ATTEMPT\" ]]",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("repository: Redth/Maui.Tizen", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(workflow, @"group:\s+maui-tizen-release").Count);
        Assert.Contains("NUGET_PACKAGES: ${{ runner.temp }}/maui-tizen-nuget-", workflow, StringComparison.Ordinal);

        var consumerStart = workflow.IndexOf("\n  package-consumer:", StringComparison.Ordinal);
        var gateStart = workflow.IndexOf("\n  release-gate:", consumerStart, StringComparison.Ordinal);
        var consumer = workflow[consumerStart..gateStart];
        Assert.Contains("environment: tizen-device-lab", consumer, StringComparison.Ordinal);
        Assert.Contains("needs.device-matrix.result == 'success'", consumer, StringComparison.Ordinal);
        Assert.DoesNotContain("always()", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseGatesSignerProvenanceAndPublishing()
    {
        var release = ReadWorkflow("release.yml");

        Assert.Contains("GATE_PASSED", release, StringComparison.Ordinal);
        Assert.Contains("!= \"true\"", release, StringComparison.Ordinal);
        Assert.Contains("--signer-workflow", release, StringComparison.Ordinal);
        Assert.Contains("--signer-digest", release, StringComparison.Ordinal);
        Assert.Contains("--source-digest", release, StringComparison.Ordinal);
        Assert.Contains("--source-ref", release, StringComparison.Ordinal);
        Assert.Contains("--run-attempt", release, StringComparison.Ordinal);
        Assert.Contains("--subject-digest", release, StringComparison.Ordinal);
        Assert.Contains("source_run_attempt: ${{ github.run_attempt }}", release, StringComparison.Ordinal);
        Assert.Contains("--run-attempt \"$GITHUB_RUN_ATTEMPT\"", release, StringComparison.Ordinal);
        Assert.Contains("RELEASE_GOVERNANCE_AUDIT_TOKEN", release, StringComparison.Ordinal);
        Assert.Contains("environment: nuget-signing", release, StringComparison.Ordinal);
        Assert.Contains("environment: nuget-publish", release, StringComparison.Ordinal);
        Assert.DoesNotContain("nuget-production", release, StringComparison.Ordinal);
        Assert.Contains("Publishing remains disabled", release, StringComparison.Ordinal);
        Assert.Contains("exit 1", release, StringComparison.Ordinal);

        var publish = release[release.IndexOf("\n  publish:", StringComparison.Ordinal)..];
        Assert.DoesNotMatch(
            new Regex(@"^\s+id-token:\s+write$", RegexOptions.Multiline),
            publish);
    }

    [Fact]
    public void ShippingProjectListIncludesBuildTasks()
    {
        var build = File.ReadAllText(Path.Combine(RepoLayout.Root, "eng", "build-tizen.sh"));
        Assert.Contains(
            "src/Maui.Tizen.Build.Tasks/Maui.Tizen.Build.Tasks.csproj",
            build,
            StringComparison.Ordinal);
        Assert.Contains("Pack shipping projects exactly once", build, StringComparison.Ordinal);
    }
}

/// <summary>
/// Ordering constraints in the device workflow.
/// </summary>
/// <remarks>
/// Two ordering mistakes were found in review, and neither could fail visibly while the device lane
/// is unavailable: cross-profile gates ran inside a per-profile job, and the agent was queried
/// before the app was launched. Both are asserted here so they cannot come back unnoticed.
/// </remarks>
public class WorkflowOrderingTests
{
    static string WorkflowPath =>
        Path.Combine(RepoLayout.Root, ".github", "workflows", "tizen-device-validation.yml");

    static string Workflow => File.ReadAllText(WorkflowPath);

    /// <summary>Extracts a single job's block, so ordering is asserted within the right job.</summary>
    static string JobBlock(string jobName)
    {
        var text = Workflow;
        var start = text.IndexOf($"\n  {jobName}:", StringComparison.Ordinal);

        Assert.True(start >= 0, $"Job '{jobName}' not found in {RepoLayout.Relative(WorkflowPath)}.");

        // The next top-level job starts at a line with exactly two spaces of indent.
        var next = System.Text.RegularExpressions.Regex.Match(
            text[(start + 1)..], @"\n  [a-z][a-z0-9-]*:\n");

        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }

    static void AssertOrder(string block, string jobName, params string[] markers)
    {
        var previousIndex = -1;
        var previousMarker = string.Empty;

        foreach (var marker in markers)
        {
            var index = block.IndexOf(marker, StringComparison.Ordinal);

            Assert.True(index >= 0, $"'{marker}' is missing from the '{jobName}' job.");
            Assert.True(
                index > previousIndex,
                $"In job '{jobName}', '{marker}' must come after '{previousMarker}'.");

            previousIndex = index;
            previousMarker = marker;
        }
    }

    [Fact]
    public void CrossProfileReadinessRunsInTheGateAfterAllProfileArtifactsAreDownloaded()
    {
        // Run per profile, these gates read a result file that still says status=running for the
        // current profile and cannot see the other profile's artifact at all - so they could only
        // ever fail.
        var gate = JobBlock("release-gate");

        AssertOrder(
            gate,
            "release-gate",
            "Download per-profile results",
            "Download exact unsigned release artifact",
            "Release readiness gates",
            "Evaluate");
    }

    [Fact]
    public void TheDeviceMatrixDoesNotRunCrossProfileReadinessGates()
    {
        var matrix = JobBlock("device-matrix");

        Assert.DoesNotContain("Release readiness gates", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("MAUI_TIZEN_RELEASE_VALIDATION", matrix, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppIsLaunchedAndTheAgentAwaitedBeforeAnyQuery()
    {
        // Installing does not start anything, and the agent binds its port during startup.
        var matrix = JobBlock("device-matrix");

        AssertOrder(
            matrix,
            "device-matrix",
            "tizen-device-lane.sh install",
            "tizen-device-lane.sh launch",
            "tizen-device-lane.sh forward",
            "tizen-device-lane.sh wait-for-agent",
            "tizen-device-lane.sh agent-status");
    }

    [Fact]
    public void InteractionStepsRunAfterTheAgentIsConfirmedReady()
    {
        var matrix = JobBlock("device-matrix");

        AssertOrder(
            matrix,
            "device-matrix",
            "Deploy, launch, tunnel and wait for the agent",
            "On-device conventions",
            "Capture and compare visual baselines",
            "Record the result");
    }

    [Fact]
    public void TheApplicationIsBuiltWithTheValidationConfiguration()
    {
        // A plain Release build excludes AddMauiDevFlowAgent(), which is conventionally '#if DEBUG'.
        // The lane would then install an app the driver can never talk to.
        var script = File.ReadAllText(
            Path.Combine(RepoLayout.ValidationConfig, "scripts", "tizen-device-lane.sh"));

        Assert.Contains("-p:MauiTizenValidation=true", script, StringComparison.Ordinal);

        var buildSection = script[script.IndexOf("cmd_build()", StringComparison.Ordinal)..];
        var packageIndex = buildSection.IndexOf("-t:Package", StringComparison.Ordinal);

        Assert.True(packageIndex > 0, "cmd_build must produce a TPK.");
        Assert.Contains(
            "-p:MauiTizenValidation=true",
            buildSection[..packageIndex],
            StringComparison.Ordinal);
    }

    [Fact]
    public void DevFlowQueriesFailOnHttpErrors()
    {
        // Without --fail, curl exits 0 on a 5xx and writes the error body to the output file, so a
        // 501 gets saved as a .png and the capture step reports success.
        var script = File.ReadAllText(
            Path.Combine(RepoLayout.ValidationConfig, "scripts", "tizen-device-lane.sh"));

        var devflow = script[script.IndexOf("devflow()", StringComparison.Ordinal)..];
        var body = devflow[..devflow.IndexOf('}', StringComparison.Ordinal)];

        Assert.Contains("--fail", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BaselineCaptureIsFollowedByComparison()
    {
        // Capturing without comparing cannot fail on a rendering regression.
        var script = File.ReadAllText(
            Path.Combine(RepoLayout.ValidationConfig, "scripts", "tizen-device-lane.sh"));

        var baselines = script[script.IndexOf("cmd_baselines()", StringComparison.Ordinal)..];

        Assert.Contains("MAUI_TIZEN_COMPARE_BASELINES=1", baselines, StringComparison.Ordinal);
        Assert.Contains("run-hosted-validation.sh", baselines, StringComparison.Ordinal);
    }
}
