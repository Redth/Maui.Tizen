using System.Text.Json;

namespace Maui.Tizen.Consumer.Tests;

/// <summary>
/// Verifies that the packages this repository produces can actually be consumed.
/// </summary>
/// <remarks>
/// <para>
/// A source build proves the code compiles. It does not prove that the resulting package restores,
/// that its dependency graph is sane, or that its MSBuild logic works from the outside. Those only
/// fail for consumers, which is the worst place to find out.
/// </para>
/// <para>
/// The synthetic case exercises the full consumer path today: pack, publish to a local feed,
/// generate a consumer project, restore it, and evaluate the dependency policy against the restored
/// graph. The real packages bind to the same harness once they can be produced.
/// </para>
/// </remarks>
public class ConsumerRestoreTests
{
    const string ProducerProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>netstandard2.0</TargetFramework>
            <PackageId>Maui.Tizen.ConsumerProbe</PackageId>
            <AssemblyName>Maui.Tizen.ConsumerProbe</AssemblyName>
            <Version>1.0.0</Version>
            <Authors>Maui.Tizen Contributors</Authors>
            <Description>Synthetic package used to verify the consumer restore harness.</Description>
            <IncludeSymbols>false</IncludeSymbols>
            <EnableDefaultNoneItems>true</EnableDefaultNoneItems>
          </PropertyGroup>
          <ItemGroup>
            <None Include="buildTransitive/Maui.Tizen.ConsumerProbe.targets" Pack="true" PackagePath="buildTransitive/" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Writes the isolation files that keep a generated project away from this repository's own
    /// build configuration.
    /// </summary>
    /// <remarks>
    /// Without these, the generated project inherits central package management and the Tizen
    /// conventions from the repository root, and the test stops being a consumer test.
    /// </remarks>
    /// <summary>
    /// Environment that gives a workspace its own NuGet package cache.
    /// </summary>
    /// <remarks>
    /// Without this the tests are not hermetic. A package produced by one test lands in the shared
    /// global cache and then resolves in another test even when it is absent from the feed under
    /// test, which silently turns a negative restore test into a false pass. That happened here.
    /// </remarks>
    static Dictionary<string, string?> IsolatedNuGetEnvironment(TempWorkspace workspace) =>
        new() { ["NUGET_PACKAGES"] = workspace.CreateSubdirectory("nuget-cache") };

    static void WriteIsolation(TempWorkspace workspace)
    {
        workspace.WriteFile("Directory.Build.props", "<Project />");
        workspace.WriteFile("Directory.Build.targets", "<Project />");
        workspace.WriteFile(
            "Directory.Packages.props",
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
    }

    static void WriteLocalFeedConfig(TempWorkspace workspace, string feedPath)
    {
        // Three things here are load-bearing:
        //
        //   <clear/> in packageSources    - without it the consumer falls back to nuget.org, and a
        //                                   package missing from the local feed looks like a
        //                                   successful restore of something else entirely.
        //   <clear/> in disabledPackageSources
        //                                 - a developer or agent whose user-level NuGet config
        //                                   disables a source by the same key would otherwise get a
        //                                   registered-but-disabled feed and an NU1101 that looks
        //                                   like a packaging bug. This was a real failure here.
        //   a distinctive key             - keeps the source from colliding with a user-level entry
        //                                   such as "local".
        workspace.WriteFile(
            "nuget.config",
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <packageSources>
                 <clear />
                 <add key="maui-tizen-validation-feed" value="{feedPath}" />
               </packageSources>
               <disabledPackageSources>
                 <clear />
               </disabledPackageSources>
             </configuration>
             """);
    }

    [Fact]
    public async Task ConsumerHarness_RestoresAPackageFromALocalFeedAndAppliesItsTargets()
    {
        using var workspace = TempWorkspace.Create("consumer-probe");
        WriteIsolation(workspace);

        workspace.WriteFile("Producer/Producer.csproj", ProducerProject);
        workspace.WriteFile("Producer/Lib.cs", "namespace Probe { public static class Marker { } }");
        workspace.WriteFile(
            "Producer/buildTransitive/Maui.Tizen.ConsumerProbe.targets",
            """
            <Project>
              <PropertyGroup>
                <MauiTizenConsumerProbeApplied>true</MauiTizenConsumerProbeApplied>
              </PropertyGroup>
            </Project>
            """);

        var feed = workspace.CreateSubdirectory("feed");
        var environment = IsolatedNuGetEnvironment(workspace);

        (await DotNetCli
            .RunAsync(["pack", workspace.Combine("Producer", "Producer.csproj"), "--nologo", "--output", feed],
                workingDirectory: workspace.Path, environment: environment)
            .ConfigureAwait(true))
            .EnsureSucceeded();

        WriteLocalFeedConfig(workspace, feed);

        workspace.WriteFile("Consumer/Consumer.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Maui.Tizen.ConsumerProbe" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var consumer = workspace.Combine("Consumer", "Consumer.csproj");

        (await DotNetCli.RunAsync(["restore", consumer], workingDirectory: workspace.Path, environment: environment)
            .ConfigureAwait(true))
            .EnsureSucceeded();

        // The package resolved...
        var graph = RestoreGraph.LoadFromProjectDirectory(workspace.Combine("Consumer"));
        Assert.Contains(graph.AllPackages, p => p.Id == "Maui.Tizen.ConsumerProbe");

        // ...and its buildTransitive targets actually reached the consumer's evaluation.
        var evaluation = await DotNetCli
            .RunAsync(["msbuild", consumer, "-getProperty:MauiTizenConsumerProbeApplied", "-v:q"],
                workingDirectory: workspace.Path, environment: environment)
            .ConfigureAwait(true);

        evaluation.EnsureSucceeded();
        Assert.Equal("true", evaluation.StandardOutput.Trim());
    }

    [Fact]
    public async Task ConsumerHarness_FailsWhenThePackageIsAbsentFromTheFeed()
    {
        // Proves the harness can fail. A restore test that silently falls back to another source
        // would pass forever regardless of what this repository produces.
        using var workspace = TempWorkspace.Create("consumer-missing");
        WriteIsolation(workspace);

        var feed = workspace.CreateSubdirectory("feed");
        WriteLocalFeedConfig(workspace, feed);

        workspace.WriteFile("Consumer/Consumer.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Maui.Tizen.ConsumerProbe" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await DotNetCli
            .RunAsync(["restore", workspace.Combine("Consumer", "Consumer.csproj")],
                workingDirectory: workspace.Path, environment: IsolatedNuGetEnvironment(workspace))
            .ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.True(
            result.OutputContains("NU1101") || result.OutputContains("Unable to find package"),
            $"Expected a package-not-found failure but got:{Environment.NewLine}{result.CombinedOutput}");
    }

    [Fact]
    public async Task ProducedPackages_RestoreIntoAConsumerProject()
    {
        var packagesDirectory = Path.Combine(RepoLayout.Root, "artifacts", "packages");

        ValidationSkip.When(
            !Directory.Exists(packagesDirectory) ||
            !Directory.EnumerateFiles(packagesDirectory, "*.nupkg").Any(),
            "No packages have been produced. The shipping packages target the Tizen framework and " +
            "cannot be packed until the Samsung workload is available; see docs/validation/blockers.md.");

        using var workspace = TempWorkspace.Create("consumer-real");
        WriteIsolation(workspace);
        WriteLocalFeedConfig(workspace, packagesDirectory);

        var references = string.Join(
            Environment.NewLine,
            PackageContentContract.EnumerateDeclaredPackageIds()
                .Select(id => $"""    <PackageReference Include="{id}" Version="*" />"""));

        workspace.WriteFile("Consumer/Consumer.csproj",
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>{RepoLayout.TizenTargetFramework}</TargetFramework>
                 <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
               </PropertyGroup>
               <ItemGroup>
             {references}
               </ItemGroup>
             </Project>
             """);

        var result = await DotNetCli
            .RunAsync(["restore", workspace.Combine("Consumer", "Consumer.csproj")],
                workingDirectory: workspace.Path, environment: IsolatedNuGetEnvironment(workspace))
            .ConfigureAwait(true);

        result.EnsureSucceeded();

        var graph = RestoreGraph.LoadFromProjectDirectory(workspace.Combine("Consumer"));
        var violations = graph.EvaluatePolicy(TizenProfiles.Matrix.DependencyPolicy);

        Assert.True(
            violations.Count == 0,
            string.Join(Environment.NewLine, violations.Select(v => v.Describe())));
    }
}

/// <summary>
/// Validates the MAUI.Sherpa handoff contract.
/// </summary>
/// <remarks>
/// Sherpa is deliberately validated as a separate consumer head and is not modified by this work.
/// What this repository owns is the contract: which packages Sherpa consumes, how the feed is
/// supplied, and what the smoke run must prove. Checking it here stops the handoff from drifting
/// out of sync with the packages actually produced.
/// </remarks>
public class SherpaSmokeContractTests
{
    static readonly string ContractPath =
        Path.Combine(RepoLayout.ValidationConfig, "consumers", "sherpa-smoke-contract.json");

    static JsonDocument Load()
    {
        Assert.True(File.Exists(ContractPath), $"Missing {RepoLayout.Relative(ContractPath)}");
        return JsonDocument.Parse(File.ReadAllText(ContractPath));
    }

    [Fact]
    public void ContractIsWellFormedAndComplete()
    {
        using var document = Load();
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual(0, root.GetProperty("packages").GetArrayLength());
        Assert.NotEqual(0, root.GetProperty("smokeSteps").GetArrayLength());

        foreach (var step in root.GetProperty("smokeSteps").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(step.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(step.GetProperty("description").GetString()));

            // Every step must say what a failure means. "The smoke test failed" tells the Sherpa
            // side nothing about whether the defect is theirs or ours.
            Assert.False(
                string.IsNullOrWhiteSpace(step.GetProperty("failureMeaning").GetString()),
                $"Smoke step '{step.GetProperty("id").GetString()}' does not say what a failure means.");
        }
    }

    [Fact]
    public void ContractPackagesAllHaveAContentContract()
    {
        using var document = Load();

        var declared = PackageContentContract.EnumerateDeclaredPackageIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidationSkip.When(declared.Count == 0, "No package-content contracts are declared yet.");

        foreach (var package in document.RootElement.GetProperty("packages").EnumerateArray())
        {
            var id = package.GetString()!;

            Assert.True(
                declared.Contains(id),
                $"The Sherpa contract consumes '{id}' but this repository declares no package-content " +
                "contract for it. Either it is not actually shipped, or its contents are unverified.");
        }
    }

    [Fact]
    public void ContractCarriesNoCredentialsOrPrivateInfrastructure()
    {
        // The feed is supplied as a pipeline parameter. A URL or token committed here would be both
        // a leak and a hard-coded dependency on one organisation's infrastructure.
        var raw = File.ReadAllText(ContractPath);

        Assert.DoesNotContain("https://", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkgs.dev.azure.com", raw, StringComparison.OrdinalIgnoreCase);

        using var document = Load();
        Assert.Equal("parameter", document.RootElement.GetProperty("packageFeed").GetProperty("kind").GetString());
    }

    [Fact]
    public void DeviceDependentStepsAreMarked()
    {
        using var document = Load();

        var launch = document.RootElement.GetProperty("smokeSteps")
            .EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == "launch");

        // Steps needing hardware must be identifiable so the Sherpa pipeline can report them as
        // "not run" rather than failing when no device lab is attached.
        Assert.True(launch.GetProperty("requiresDeviceInfrastructure").GetBoolean());
    }
}
