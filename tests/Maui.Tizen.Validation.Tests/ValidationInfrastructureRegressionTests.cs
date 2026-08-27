using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace Maui.Tizen.Validation.Tests;

public class ValidationInfrastructureRegressionTests
{
    static string DeviceLaneScript =>
        Path.Combine(RepoLayout.ValidationConfig, "scripts", "tizen-device-lane.sh");

    [Fact]
    public void BaselineSidecarGenerationExecutesWithFixtureMetadata()
    {
        using var workspace = TempWorkspace.Create("baseline-sidecar");
        var png = workspace.Combine("button.png");
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 320);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 240);
        File.WriteAllBytes(png, bytes);

        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoLayout.Root,
        };
        psi.Environment["TIZEN_TFM"] = "net11.0-tizen11.0-fixture";
        psi.Environment["TIZEN_DEVICE_IMAGE"] = "fixture/device:15";
        psi.ArgumentList.Add(DeviceLaneScript);
        psi.ArgumentList.Add("baseline-sidecar");
        psi.ArgumentList.Add(png);
        psi.ArgumentList.Add("button");
        psi.ArgumentList.Add("mobile");
        psi.ArgumentList.Add("15");
        psi.ArgumentList.Add("dark");
        psi.ArgumentList.Add("xhdpi");

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, output);
        using var sidecar = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(png, ".json")));
        var root = sidecar.RootElement;
        Assert.Equal("net11.0-tizen11.0-fixture", root.GetProperty("targetFramework").GetString());
        Assert.Equal("fixture/device:15", root.GetProperty("deviceImage").GetString());
        Assert.Equal(320, root.GetProperty("width").GetInt32());
        Assert.Equal(240, root.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task Api15CompileProbeFailsWhenNoAgentSourcesAreIncluded()
    {
        using var workspace = TempWorkspace.Create("api15-empty-sources");
        var project = Path.Combine(
            RepoLayout.Root, "eng", "tests", "Api15CompileProbe", "Api15CompileProbe.csproj");
        var result = await DotNetCli.RunAsync(
            [
                "msbuild",
                project,
                "-t:ValidateApi15AgentSources",
                $"-p:Api15AgentSourceRoot={workspace.Path}{Path.DirectorySeparatorChar}",
                "-v:q",
            ],
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "found no Tizen DevFlow agent sources",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowDelegatesGateOutputToTheTestedEvaluator()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoLayout.Root, ".github", "workflows", "tizen-device-validation.yml"));

        Assert.Contains(
            "--release-validation \"${{ inputs.release_validation }}\"",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"gate_passed=true\"", workflow, StringComparison.Ordinal);
    }
}
