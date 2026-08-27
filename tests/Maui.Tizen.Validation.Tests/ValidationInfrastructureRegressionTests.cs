using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace Maui.Tizen.Validation.Tests;

public class ValidationInfrastructureRegressionTests
{
    static string DeviceLaneScript =>
        Path.Combine(RepoLayout.ValidationConfig, "scripts", "tizen-device-lane.sh");

    static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

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

    [Theory]
    [InlineData("mobile", 6)]
    [InlineData("tv", 2)]
    public void DeviceLaneExpandsEveryDeclaredVisualVariant(string profile, int expectedCount)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoLayout.Root,
        };
        psi.Environment["TIZEN_PROFILE"] = profile;
        psi.ArgumentList.Add(DeviceLaneScript);
        psi.ArgumentList.Add("list-baseline-variants");

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, error);
        Assert.Equal(expectedCount, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void DeviceProtocolUsesLeaseProtectedPinnedRoutes()
    {
        var script = File.ReadAllText(DeviceLaneScript);

        Assert.Contains("/api/v1/agent/lease", script, StringComparison.Ordinal);
        Assert.Contains("X-DevFlow-Lease", script, StringComparison.Ordinal);
        Assert.Contains("ui/elements?selector=%2A", script, StringComparison.Ordinal);
        Assert.Contains("storage/preferences/devflow.lifecycle.marker", script, StringComparison.Ordinal);
        Assert.Contains("-X PUT", script, StringComparison.Ordinal);
        Assert.DoesNotContain("storage/preferences?key=", script, StringComparison.Ordinal);
        Assert.DoesNotContain("marker\\\"}\" >/dev/null || true", script, StringComparison.Ordinal);
        Assert.Contains("found_route = node", script, StringComparison.Ordinal);
        Assert.DoesNotContain("found_route = node.lstrip", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceAssertionsExecuteTheAdvertisedAbsoluteRouteUnderAMutationLease()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var workspace = TempWorkspace.Create("device-protocol");
        var log = workspace.Combine("curl.log");
        var curl = workspace.WriteFile(
            "bin/curl",
            """
            #!/usr/bin/env bash
            printf '%s\n' "$*" >> "$CURL_LOG"
            for arg in "$@"; do
              [[ "$arg" == http://* ]] && url="$arg"
            done
            case "$url" in
              */api/v1/agent/lease)
                printf '{"ok":true,"allowed":true,"youHold":true}\n'
                ;;
              */api/v1/agent/capabilities)
                printf '{"extensions":[{"namespace":"org.dotnet.maui.tizen","routes":["/api/v1/ext/org.dotnet.maui.tizen/conventions/run"]}]}\n'
                ;;
              */api/v1/ext/org.dotnet.maui.tizen/conventions/run)
                printf '{"total":2,"failed":[],"skipped":[]}\n'
                ;;
              *)
                printf 'unexpected URL: %s\n' "$url" >&2
                exit 1
                ;;
            esac
            """);
        MakeExecutable(curl);

        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoLayout.Root,
        };
        psi.Environment["PATH"] = workspace.Combine("bin") + Path.PathSeparator + psi.Environment["PATH"];
        psi.Environment["CURL_LOG"] = log;
        psi.Environment["TIZEN_PROFILE"] = "mobile";
        psi.ArgumentList.Add(DeviceLaneScript);
        psi.ArgumentList.Add("device-assertions");

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, output);
        var calls = File.ReadAllText(log);
        Assert.Contains("/api/v1/ext/org.dotnet.maui.tizen/conventions/run", calls, StringComparison.Ordinal);
        Assert.Contains("X-DevFlow-Lease: maui-tizen-mobile-", calls, StringComparison.Ordinal);
        Assert.Contains("\"action\":\"claim\"", calls, StringComparison.Ordinal);
        Assert.Contains("\"action\":\"release\"", calls, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflightRequiresDistinctConnectedMobileAndTvSerials()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var workspace = TempWorkspace.Create("device-preflight");
        var dotnet = workspace.WriteFile(
            "bin/dotnet",
            "#!/usr/bin/env bash\nprintf 'tizen 11.0.100\\n'\n");
        var sdb = workspace.WriteFile(
            "bin/sdb",
            "#!/usr/bin/env bash\nprintf 'mobile-1 device\\ntv-1 device\\n'\n");
        var tizen = workspace.WriteFile("bin/tizen", "#!/usr/bin/env bash\nexit 0\n");
        MakeExecutable(dotnet);
        MakeExecutable(sdb);
        MakeExecutable(tizen);

        var result = RunDeviceScript(
            workspace,
            "preflight",
            new Dictionary<string, string?>
            {
                ["DOTNET"] = dotnet,
                ["TIZEN_PROFILE"] = "mobile",
                ["TIZEN_DEVICE_SERIAL"] = "mobile-1",
                ["TIZEN_MOBILE_SERIAL"] = "mobile-1",
                ["TIZEN_TV_SERIAL"] = "tv-1",
            });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("lane_available=true", result.Output, StringComparison.Ordinal);

        var duplicate = RunDeviceScript(
            workspace,
            "preflight",
            new Dictionary<string, string?>
            {
                ["DOTNET"] = dotnet,
                ["TIZEN_PROFILE"] = "mobile",
                ["TIZEN_DEVICE_SERIAL"] = "same",
                ["TIZEN_MOBILE_SERIAL"] = "same",
                ["TIZEN_TV_SERIAL"] = "same",
            });

        Assert.Equal(0, duplicate.ExitCode);
        Assert.Contains("lane_available=false", duplicate.Output, StringComparison.Ordinal);
        Assert.Contains("distinct targets", duplicate.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowRecordsMissingAppAndVerifiesTheConnectedProfile()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoLayout.Root, ".github", "workflows", "tizen-device-validation.yml"));

        Assert.Contains("TIZEN_MOBILE_SERIAL", workflow, StringComparison.Ordinal);
        Assert.Contains("TIZEN_TV_SERIAL", workflow, StringComparison.Ordinal);
        Assert.Contains("status=skipped\\nreason=no_app", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-device-profile", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "steps.app.outputs.available == 'true'",
            workflow[workflow.IndexOf("- name: Record the result", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void HostedLaneRejectsZeroExecutedTests()
    {
        var script = File.ReadAllText(
            Path.Combine(RepoLayout.ValidationConfig, "run-hosted-validation.sh"));

        Assert.Contains("result file reports zero executed tests", script, StringComparison.Ordinal);
        Assert.Contains("get('executed', '0')", script, StringComparison.Ordinal);
    }

    static (int ExitCode, string Output) RunDeviceScript(
        TempWorkspace workspace,
        string command,
        IReadOnlyDictionary<string, string?> environment)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoLayout.Root,
        };
        psi.Environment["PATH"] = workspace.Combine("bin") + Path.PathSeparator + psi.Environment["PATH"];
        foreach (var (key, value) in environment)
            psi.Environment[key] = value;
        psi.ArgumentList.Add(DeviceLaneScript);
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
