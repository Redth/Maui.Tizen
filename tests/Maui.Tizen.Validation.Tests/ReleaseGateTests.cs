using System.Diagnostics;

namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Exhaustive tests for the release gate decision.
/// </summary>
/// <remarks>
/// <para>
/// A code review found that the previous gate passed a release when the device matrix ran with
/// every device step skipped: it only inspected the matrix job's <em>result</em>, and a job whose
/// steps were all conditioned out still reports success. A device lane that validated nothing was
/// indistinguishable from one that passed.
/// </para>
/// <para>
/// That hole was untestable while the logic lived inline in YAML. It now lives in
/// <c>eng/validation/scripts/evaluate-release-gate.sh</c> and is exercised here as a truth table,
/// including the exact case that slipped through.
/// </para>
/// </remarks>
public class ReleaseGateTests
{
    static string ScriptPath =>
        Path.Combine(RepoLayout.ValidationConfig, "scripts", "evaluate-release-gate.sh");

    static (int ExitCode, string Output) Evaluate(
        string required,
        string labEnabled,
        string matrixResult,
        string requiredProfiles,
        string resultsDirectory,
        string? releaseValidation = null,
        string? githubOutput = null,
        string consumerResult = "success")
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoLayout.Root,
        };

        psi.ArgumentList.Add(ScriptPath);
        psi.ArgumentList.Add("--required");
        psi.ArgumentList.Add(required);
        psi.ArgumentList.Add("--release-validation");
        psi.ArgumentList.Add(releaseValidation ?? string.Empty);
        psi.ArgumentList.Add("--lab-enabled");
        psi.ArgumentList.Add(labEnabled);
        psi.ArgumentList.Add("--matrix-result");
        psi.ArgumentList.Add(matrixResult);
        psi.ArgumentList.Add("--consumer-result");
        psi.ArgumentList.Add(consumerResult);
        psi.ArgumentList.Add("--required-profiles");
        psi.ArgumentList.Add(requiredProfiles);
        psi.ArgumentList.Add("--results-dir");
        psi.ArgumentList.Add(resultsDirectory);

        if (githubOutput is not null)
            psi.Environment["GITHUB_OUTPUT"] = githubOutput;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    /// <summary>Writes a per-profile result file of the shape the device job produces.</summary>
    static void WriteResult(TempWorkspace workspace, string profile, string laneAvailable, string status) =>
        workspace.WriteFile($"device-result-{profile}.txt", $"lane_available={laneAvailable}\nstatus={status}\n");

    [Fact]
    public void ScriptExists()
    {
        Assert.True(File.Exists(ScriptPath), $"Missing {RepoLayout.Relative(ScriptPath)}");
    }

    // -----------------------------------------------------------------------------------------
    // Not a release: the lane must never block an ordinary pull request.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("skipped")]
    [InlineData("failure")]
    [InlineData("success")]
    [InlineData("cancelled")]
    public void NonReleaseRun_AlwaysPasses(string matrixResult)
    {
        using var workspace = TempWorkspace.Create("gate-pr");

        var (exitCode, output) = Evaluate("false", "false", matrixResult, "mobile tv", workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("informational", output, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // The regression the review found.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Release_IsBlockedWhenTheMatrixRanButEveryDeviceStepWasSkipped()
    {
        // The exact hole: matrix result is 'success' because the job completed, but the lane was
        // unavailable so nothing ran on hardware.
        using var workspace = TempWorkspace.Create("gate-hole");
        WriteResult(workspace, "mobile", laneAvailable: "false", status: "skipped");
        WriteResult(workspace, "tv", laneAvailable: "false", status: "skipped");

        var (exitCode, output) = Evaluate("true", "true", "success", "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("lane_available='false'", output, StringComparison.Ordinal);
        Assert.Contains("nothing was validated on hardware", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenOnlyOneRequiredProfileWasSkipped()
    {
        // Guards the artifact-per-profile design: matrix job outputs collapse to one value, so a
        // passing profile could otherwise mask a skipped one.
        using var workspace = TempWorkspace.Create("gate-partial");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "false", status: "skipped");

        var (exitCode, output) = Evaluate("true", "true", "success", "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("'tv'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenARequiredProfileProducedNoResultAtAll()
    {
        using var workspace = TempWorkspace.Create("gate-missing");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");

        var (exitCode, output) = Evaluate("true", "true", "success", "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("produced no result file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenAProfileRanOnHardwareButFailed()
    {
        using var workspace = TempWorkspace.Create("gate-failed");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "true", status: "fail");

        var (exitCode, output) = Evaluate("true", "true", "success", "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("status='fail'", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public void Release_IsBlockedWhenTheMatrixDidNotSucceed(string matrixResult)
    {
        using var workspace = TempWorkspace.Create("gate-matrix");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "true", status: "pass");

        var (exitCode, output) = Evaluate("true", "true", matrixResult, "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains(matrixResult, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public void Release_IsBlockedWhenRealPackageConsumerRestoreDidNotSucceed(string consumerResult)
    {
        using var workspace = TempWorkspace.Create("gate-consumer");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");

        var (exitCode, output) = Evaluate(
            "true", "true", "success", "mobile", workspace.Path, consumerResult: consumerResult);

        Assert.Equal(1, exitCode);
        Assert.Contains("real-package consumer restore", output, StringComparison.Ordinal);
        Assert.Contains(consumerResult, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenNoLabIsAttached()
    {
        using var workspace = TempWorkspace.Create("gate-nolab");

        var (exitCode, output) = Evaluate("true", "false", "skipped", "mobile tv", workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("no device lab is attached", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenNoProfilesAreRequired()
    {
        // An empty required list would otherwise vacuously pass, checking nothing.
        using var workspace = TempWorkspace.Create("gate-noprofiles");

        var (exitCode, output) = Evaluate("true", "true", "success", string.Empty, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("nothing would be checked", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenTheResultsDirectoryIsMissing()
    {
        var (exitCode, output) = Evaluate(
            "true", "true", "success", "mobile tv", "/nonexistent-results-directory");

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_IsBlockedWhenAResultFileIsMalformed()
    {
        using var workspace = TempWorkspace.Create("gate-malformed");
        workspace.WriteFile("device-result-mobile.txt", "garbage\n");

        var (exitCode, _) = Evaluate("true", "true", "success", "mobile", workspace.Path);

        Assert.Equal(1, exitCode);
    }

    // -----------------------------------------------------------------------------------------
    // The only passing release path.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Release_PassesOnlyWhenEveryRequiredProfileRanOnHardwareAndPassed()
    {
        using var workspace = TempWorkspace.Create("gate-pass");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "true", status: "pass");

        var (exitCode, output) = Evaluate("true", "true", "success", "mobile tv", workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("release may proceed", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    public void GateOutput_IsFalseWhenReleaseValidationIsOmittedOrFalse(string? releaseValidation)
    {
        using var workspace = TempWorkspace.Create("gate-output-informational");
        var outputFile = workspace.Combine("github-output.txt");

        var (exitCode, _) = Evaluate(
            "false", "false", "success", "mobile tv", workspace.Path, releaseValidation, outputFile);

        Assert.Equal(0, exitCode);
        Assert.Equal("gate_passed=false", File.ReadAllText(outputFile).Trim());
    }

    [Fact]
    public void GateOutput_IsTrueOnlyForAPassingExplicitReleaseValidationCall()
    {
        using var workspace = TempWorkspace.Create("gate-output-release");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "true", status: "pass");
        var outputFile = workspace.Combine("github-output.txt");

        var (exitCode, _) = Evaluate(
            "true", "true", "success", "mobile tv", workspace.Path, "true", outputFile);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["gate_passed=false", "gate_passed=true"],
            File.ReadAllLines(outputFile));
    }

    [Fact]
    public void PassingRequiredTagGateStillOutputsFalseWithoutReleaseValidationInput()
    {
        using var workspace = TempWorkspace.Create("gate-output-tag");
        WriteResult(workspace, "mobile", laneAvailable: "true", status: "pass");
        WriteResult(workspace, "tv", laneAvailable: "true", status: "pass");
        var outputFile = workspace.Combine("github-output.txt");

        var (exitCode, _) = Evaluate(
            "true", "true", "success", "mobile tv", workspace.Path, "false", outputFile);

        Assert.Equal(0, exitCode);
        Assert.Equal("gate_passed=false", File.ReadAllText(outputFile).Trim());
    }

    [Fact]
    public void RequiredProfiles_MatchTheReleaseGatingProfilesInTheMatrix()
    {
        // The workflow passes this list; if it drifted from the profile matrix, the gate would
        // silently stop requiring a profile that is supposed to gate a release.
        var expected = TizenProfiles.ReleaseGatingProfiles.Select(p => p.Id).OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(["mobile", "tv"], expected);

        var requiredTargets = TizenProfiles.ReleaseGatingProfiles
            .SelectMany(p => p.Densities.Select(d => $"{p.Id}-{d}"))
            .OrderBy(p => p, StringComparer.Ordinal);
        Assert.Equal(["mobile-hdpi", "mobile-mdpi", "mobile-xhdpi", "tv-fhd", "tv-uhd"], requiredTargets);
    }
}
