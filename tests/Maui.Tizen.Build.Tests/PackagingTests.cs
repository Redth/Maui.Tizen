namespace Maui.Tizen.Build.Tests;

/// <summary>
/// Restore, build, pack and package-content assertions that need no Tizen workload.
/// </summary>
/// <remarks>
/// <para>
/// The shipping packages target <c>net11.0-tizen11.0</c> and cannot be produced by anyone until the
/// Samsung workload ships. Those assertions therefore bind to the artifacts directory and skip when
/// it is empty.
/// </para>
/// <para>
/// The synthetic cases are not a stand-in for that. They exist so the packaging harness itself -
/// pack invocation, nupkg reading, contract evaluation - is proven end to end on every hosted build,
/// rather than being first exercised on the day the workload finally arrives.
/// </para>
/// </remarks>
public class PackagingTests
{
    const string PackableProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>netstandard2.0</TargetFramework>
            <PackageId>Maui.Tizen.HarnessProbe</PackageId>
            <!-- Assembly name follows the package id, matching eng/targets/TizenPackage.props. -->
            <AssemblyName>Maui.Tizen.HarnessProbe</AssemblyName>
            <Version>1.0.0</Version>
            <Authors>Maui.Tizen Contributors</Authors>
            <Description>Synthetic package used to verify the packaging harness.</Description>
            <IncludeSymbols>false</IncludeSymbols>
            <GenerateDocumentationFile>false</GenerateDocumentationFile>
            <EnableDefaultNoneItems>true</EnableDefaultNoneItems>
          </PropertyGroup>
          <ItemGroup>
            <None Include="buildTransitive/Maui.Tizen.HarnessProbe.targets"
                  Pack="true"
                  PackagePath="buildTransitive/" />
          </ItemGroup>
        </Project>
        """;

    const string TransitiveTargets =
        """
        <Project>
          <PropertyGroup>
            <MauiTizenHarnessProbeImported>true</MauiTizenHarnessProbeImported>
          </PropertyGroup>
        </Project>
        """;

    static async Task<(TempWorkspace Workspace, string OutputDirectory)> PackProbeAsync()
    {
        var workspace = TempWorkspace.Create("pack-probe");

        workspace.WriteFile("Probe/Probe.csproj", PackableProject);
        workspace.WriteFile("Probe/buildTransitive/Maui.Tizen.HarnessProbe.targets", TransitiveTargets);
        // Block-scoped namespace: netstandard2.0 defaults to C# 7.3, where file-scoped
        // namespaces are not available.
        workspace.WriteFile("Probe/Library.cs", "namespace Probe { public static class Marker { public static int Value { get { return 1; } } } }");

        // An empty Directory.Build.props/targets pair isolates the fixture from the repository's
        // own build configuration, which would otherwise apply CPM and Tizen conventions to it.
        workspace.WriteFile("Directory.Build.props", "<Project />");
        workspace.WriteFile("Directory.Build.targets", "<Project />");
        workspace.WriteFile("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

        var output = workspace.CreateSubdirectory("packages");

        var result = await DotNetCli
            .RunAsync(["pack", workspace.Combine("Probe", "Probe.csproj"), "--nologo", "--output", output],
                workingDirectory: workspace.Path,
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        result.EnsureSucceeded();
        return (workspace, output);
    }

    [Fact]
    public async Task PackagingHarness_ReadsARealPackageAndEvaluatesAContract()
    {
        var (workspace, output) = await PackProbeAsync().ConfigureAwait(true);

        using (workspace)
        {
            using var package = NuPkg.OpenFromDirectory(output, "Maui.Tizen.HarnessProbe");

            var contract = PackageContentContract.Parse(
                "Maui.Tizen.HarnessProbe",
                "in-memory",
                [
                    "require lib/netstandard2.0/Maui.Tizen.HarnessProbe.dll",
                    "require buildTransitive/Maui.Tizen.HarnessProbe.targets",
                    "forbid **/*.pdb",
                ]);

            var evaluation = contract.Evaluate(package.Entries);

            Assert.True(evaluation.Passed, evaluation.Describe(package.Entries));
        }
    }

    [Fact]
    public async Task PackagingHarness_FailsLoudlyWhenContentIsMissing()
    {
        // Confirms the harness can actually fail. A content check that only ever passes is worse
        // than none, because it manufactures confidence.
        var (workspace, output) = await PackProbeAsync().ConfigureAwait(true);

        using (workspace)
        {
            using var package = NuPkg.OpenFromDirectory(output, "Maui.Tizen.HarnessProbe");

            var contract = PackageContentContract.Parse(
                "Maui.Tizen.HarnessProbe",
                "in-memory",
                ["require lib/net11.0-tizen11.0/Maui.Tizen.HarnessProbe.dll"]);

            var evaluation = contract.Evaluate(package.Entries);

            Assert.False(evaluation.Passed);
            Assert.Contains("matched no entry", evaluation.Describe(package.Entries), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TemplatesPackage_MatchesItsReleaseContentContract()
    {
        using var workspace = TempWorkspace.Create("templates-package-contract");
        var output = workspace.CreateSubdirectory("packages");
        var project = Path.Combine(
            RepoLayout.Root,
            "src",
            "Maui.Tizen.Templates",
            "Maui.Tizen.Templates.csproj");

        var result = await DotNetCli
            .RunAsync(
                ["pack", project, "--nologo", "-c", "Release", "--output", output],
                workingDirectory: RepoLayout.Root,
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        result.EnsureSucceeded();
        using var package = NuPkg.OpenFromDirectory(output, "Maui.Tizen.Templates");
        var contract = PackageContentContract.Load("Maui.Tizen.Templates");
        var evaluation = contract.Evaluate(package.Entries);

        Assert.True(evaluation.Passed, evaluation.Describe(package.Entries));
    }

    [Fact]
    public async Task NuspecReader_ReturnsPackageIdentityAndDependencies()
    {
        var (workspace, output) = await PackProbeAsync().ConfigureAwait(true);

        using (workspace)
        {
            using var package = NuPkg.OpenFromDirectory(output, "Maui.Tizen.HarnessProbe");
            var nuspec = package.ReadNuspec();

            var id = nuspec.Root?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;

            Assert.Equal("Maui.Tizen.HarnessProbe", id);
            Assert.NotNull(package.ReadDependencies());
        }
    }

    [Fact]
    public void DeclaredPackageContracts_UseTheRepositoryPackageIdPrefix()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");

        var ids = PackageContentContract.EnumerateDeclaredPackageIds();
        ValidationSkip.When(ids.Count == 0, "No package-content contracts are declared yet.");

        var prefix = RepositoryBaselines.Policy.PackageIdPrefix;

        foreach (var id in ids)
        {
            Assert.True(
                id.StartsWith(prefix, StringComparison.Ordinal),
                $"Package-content contract '{id}' does not use the '{prefix}' prefix required by " +
                "eng/baselines.json > policy.packageIdPrefix. Publishing under Microsoft.Maui.* from " +
                "this repository would collide with the real packages.");
        }
    }

    [Fact]
    public void ProducedPackages_MatchTheirDeclaredContracts()
    {
        var ids = PackageContentContract.EnumerateDeclaredPackageIds();
        ValidationSkip.When(ids.Count == 0, "No package-content contracts are declared yet.");

        var packagesDirectory = Path.Combine(RepoLayout.Root, "artifacts", "packages");

        // Presence of ANY .nupkg is the wrong question. Other probes in this repository pack
        // internal artifacts into the same directory (eng/tests/PackReadmeProbe produces
        // Maui.Tizen.Internal.PackReadmeProbe), so a non-empty directory does not mean the
        // SHIPPING packages were built. Ask per declared package instead.
        var produced = ids
            .Where(id => NuPkg.FindPackagePaths(packagesDirectory, id).Count > 0)
            .ToList();

        ValidationSkip.When(
            produced.Count == 0,
            $"None of the {ids.Count} declared shipping package(s) have been produced in " +
            $"'{RepoLayout.Relative(packagesDirectory)}'. They target the Tizen framework and " +
            "cannot be packed until the Samsung workload is available; see " +
            "docs/validation/blockers.md. (Release runs assert this instead of skipping - see " +
            "ReleaseReadinessTests.)");

        // Whatever HAS been produced is held to its contract, so a partial pack still gets checked.
        foreach (var id in produced)
        {
            using var package = NuPkg.OpenFromDirectory(packagesDirectory, id);

            var evaluation = PackageContentContract.Load(id).Evaluate(package.Entries);

            Assert.True(evaluation.Passed, evaluation.Describe(package.Entries));
        }
    }

    [Fact]
    public async Task PackageLookupRejectsFilenamePrefixSpoofing()
    {
        var (workspace, output) = await PackProbeAsync().ConfigureAwait(true);

        using (workspace)
        {
            var real = Assert.Single(Directory.EnumerateFiles(output, "*.nupkg"));
            var spoof = Path.Combine(output, "Maui.Tizen.Core.Extra.1.0.0.nupkg");
            File.Move(real, spoof);

            Assert.Empty(NuPkg.FindPackagePaths(output, "Maui.Tizen.Core"));
            Assert.Single(NuPkg.FindPackagePaths(output, "maui.tizen.harnessprobe"));
        }
    }
}
