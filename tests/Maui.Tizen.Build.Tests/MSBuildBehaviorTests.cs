namespace Maui.Tizen.Build.Tests;

/// <summary>
/// Behavioural tests for the MSBuild logic that ships to consumers.
/// </summary>
/// <remarks>
/// <para>
/// MSBuild logic is the least tested part of most packages and the most disruptive when wrong: a
/// broken <c>buildTransitive</c> targets file breaks every consumer's build, including consumers who
/// never touched Tizen. These tests run a real <c>dotnet build</c> against fixture projects and
/// assert on evaluated property values.
/// </para>
/// <para>
/// The fixtures under <c>tests/fixtures/msbuild</c> encode the contract that the shipping targets
/// must satisfy. They are exercised on every hosted build; the shipping targets are additionally
/// asserted against once they exist.
/// </para>
/// </remarks>
public class MSBuildBehaviorTests
{
    /// <summary>
    /// Builds a fixture and returns the evaluated value of <paramref name="property"/>.
    /// </summary>
    static async Task<string> EvaluateAsync(string fixtureName, string property, params string[] extraArgs)
    {
        var fixture = Path.Combine(RepoLayout.MSBuildFixtures, fixtureName, $"{fixtureName}.csproj");

        Assert.True(File.Exists(fixture), $"Missing MSBuild fixture: {RepoLayout.Relative(fixture)}");

        var result = await DotNetCli
            .RunAsync(["msbuild", fixture, $"-getProperty:{property}", "-v:q", .. extraArgs],
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        result.EnsureSucceeded();
        return result.StandardOutput.Trim();
    }

    [Fact]
    public async Task BuildTransitiveTargets_SupplyADefaultManifestApiVersion()
    {
        // A consumer that sets nothing must still get a valid api-version in tizen-manifest.xml.
        var value = await EvaluateAsync("BuildTransitiveContract", "TizenManifestApiVersion").ConfigureAwait(true);

        Assert.Equal("11", value);
    }

    [Fact]
    public async Task BuildTransitiveTargets_DoNotOverrideAConsumerSetting()
    {
        // Defaults must be conditional. Targets that unconditionally assign are the reason
        // consumers end up unable to override anything.
        var value = await EvaluateAsync(
                "BuildTransitiveContract",
                "TizenManifestApiVersion",
                "-p:TizenManifestApiVersion=12")
            .ConfigureAwait(true);

        Assert.Equal("12", value);
    }

    [Fact]
    public async Task BuildTransitiveTargets_ReportTheWorkloadGateWithoutFailingEvaluation()
    {
        // The Samsung workload is absent almost everywhere. Evaluation must still succeed so that
        // IDE load, restore and `-getProperty` keep working; only an actual Tizen build should fail.
        var value = await EvaluateAsync("BuildTransitiveContract", "MauiTizenWorkloadGateMessage").ConfigureAwait(true);

        Assert.Contains("samsung.net.sdk.tizen", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShippingBuildTransitiveTargets_AreValidMSBuildWhenPresent()
    {
        var candidates = Directory.Exists(RepoLayout.Src)
            ? Directory.GetFiles(RepoLayout.Src, "*.targets", SearchOption.AllDirectories)
                .Where(p => p.Replace('\\', '/').Contains("/buildTransitive/", StringComparison.Ordinal))
                .ToList()
            : [];

        ValidationSkip.When(
            candidates.Count == 0,
            "No buildTransitive targets ship yet. They arrive with the packaging workstream; this " +
            "test activates automatically once they exist.");

        foreach (var targets in candidates)
        {
            using var workspace = TempWorkspace.Create("targets-probe");

            workspace.WriteFile("Directory.Build.props", "<Project />");
            workspace.WriteFile("Directory.Build.targets", "<Project />");
            workspace.WriteFile("Directory.Packages.props",
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            workspace.WriteFile("Probe/Probe.csproj",
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>netstandard2.0</TargetFramework>
                   </PropertyGroup>
                   <Import Project="{targets.Replace('\\', '/')}" />
                 </Project>
                 """);

            var result = await DotNetCli
                .RunAsync(["msbuild", workspace.Combine("Probe", "Probe.csproj"), "-getProperty:TargetFramework", "-v:q"],
                    workingDirectory: workspace.Path,
                    cancellationToken: TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.True(
                result.Succeeded,
                $"Importing '{RepoLayout.Relative(targets)}' broke evaluation of an otherwise valid " +
                $"project:{Environment.NewLine}{result.CombinedOutput}");
        }
    }
}
