using System.Text.Json;

namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Enforces the repository-level contracts that no single project owns.
/// </summary>
/// <remarks>
/// The repository-root <c>Directory.Build.props</c> states that its target-framework values "mirror
/// eng/baselines.json &gt; target" and that "the CI validation lane checks that they match". This
/// suite is that check. MSBuild cannot read JSON at evaluation time, so the duplication is
/// unavoidable and the only defence against drift is an explicit assertion.
/// </remarks>
public class RepositoryContractTests
{
    [Fact]
    public void DirectoryBuildProps_MirrorsBaselineTargetContract()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");
        ValidationSkip.WhenPathMissing(RepoLayout.RootDirectoryBuildProps, "foundation import");

        var properties = MSBuildPropertyReader.ReadFile(RepoLayout.RootDirectoryBuildProps);
        var target = RepositoryBaselines.Target;

        Assert.Equal(target.DotNetVersion, properties.Value("DotNetVersion"));
        Assert.Equal(target.TizenPlatformVersion, properties.Value("TizenPlatformVersion"));
        Assert.Equal(target.TizenManifestApiVersion, properties.Value("TizenManifestApiVersion"));

        // Composed from DotNetVersion + TizenPlatformVersion; compared after expansion so a broken
        // composition is caught rather than assumed correct.
        Assert.Equal(target.TargetFramework, properties.Value("MauiTizenTargetFramework"));
    }

    [Fact]
    public void GlobalJson_PinsAnSdkVersionWithinTheBaselineBand()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.GlobalJsonFile, "foundation import");
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");

        using var document = JsonDocument.Parse(File.ReadAllText(RepoLayout.GlobalJsonFile));

        var sdkVersion = document.RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? string.Empty;
        var band = RepositoryBaselines.Target.SdkBand;

        // global.json pins a CONCRETE version (e.g. 11.0.100-preview.7.26381.103) while
        // eng/baselines.json records the BAND (11.0.100-preview.7). They are deliberately not
        // equal: actions/setup-dotnet looks the global.json value up verbatim and a bare band is
        // not a resolvable SDK version. The invariant is membership, not equality.
        //
        // The band-plus-separator check matters: a plain StartsWith would let band "11.0.100-preview.7"
        // wrongly accept a version like "11.0.100-preview.70.1".
        Assert.True(
            sdkVersion == band || sdkVersion.StartsWith(band + ".", StringComparison.Ordinal),
            $"global.json pins SDK '{sdkVersion}', which is not in the band '{band}' declared by " +
            "eng/baselines.json > target.sdkBand.");
    }

    [Fact]
    public void TestRunnerSplit_IsRecordedRatherThanAssumed()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.GlobalJsonFile, "foundation import");

        using var document = JsonDocument.Parse(File.ReadAllText(RepoLayout.GlobalJsonFile));

        var optedIntoMicrosoftTestingPlatform =
            document.RootElement.TryGetProperty("test", out var test) &&
            test.TryGetProperty("runner", out var runner) &&
            runner.GetString() == "Microsoft.Testing.Platform";

        // The repository currently runs two test stacks: xunit v2 on VSTest (tests/UnitTests)
        // and xunit v3 on Microsoft.Testing.Platform (the validation suites). The .NET 10+ SDK
        // dropped VSTest support for Microsoft.Testing.Platform, so a single `dotnet test`
        // cannot serve both, and the v3 suites are executed as binaries instead.
        //
        // This test does not force either choice. It fails only if global.json opts into the
        // Microsoft.Testing.Platform runner while the v2 project is still present, because that
        // combination breaks `dotnet test` for tests/UnitTests.
        var legacyProject = Path.Combine(RepoLayout.Tests, "UnitTests", "Maui.Tizen.UnitTests.csproj");

        if (optedIntoMicrosoftTestingPlatform && File.Exists(legacyProject))
        {
            Assert.Fail(
                """
                global.json opts into the Microsoft.Testing.Platform runner while
                tests/UnitTests still uses xunit v2 on VSTest. `dotnet test` will fail for that
                project. Either migrate tests/UnitTests to xunit v3 or remove the opt-in.
                See docs/validation/hosted-lane.md.
                """);
        }
    }

    [Fact]
    public void BaselineDeclaresTheSamsungWorkloadBlockerExplicitly()
    {
        ValidationSkip.WhenPathMissing(RepoLayout.BaselinesFile, "foundation import");

        var manifest = RepositoryBaselines.Target.WorkloadManifest;

        Assert.False(string.IsNullOrWhiteSpace(manifest.Id));

        // Not an assertion that the workload is missing forever - an assertion that its status is
        // recorded. When Samsung publishes it, this test is the reminder to flip the record and
        // enable the device lane by default.
        Assert.True(
            manifest.Status is "available" or "unavailable",
            $"eng/baselines.json > target.workloadManifest.status must be 'available' or " +
            $"'unavailable' but was '{manifest.Status}'.");

        if (manifest.IsUnavailable)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(manifest.Note),
                "An unavailable workload manifest must carry a note explaining the external blocker.");
        }
    }

    [Fact]
    public void PropertyReader_ExpandsComposedValues()
    {
        // Guards the reader itself: composed TFMs are the values most likely to drift unnoticed.
        var properties = MSBuildPropertyReader.Read(
            """
            <Project>
              <PropertyGroup>
                <DotNetVersion>11.0</DotNetVersion>
                <DotNetTfm>net$(DotNetVersion)</DotNetTfm>
                <TizenPlatformVersion>11.0</TizenPlatformVersion>
                <MauiTizenTargetFramework>$(DotNetTfm)-tizen$(TizenPlatformVersion)</MauiTizenTargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Assert.Equal("net11.0", properties.Value("DotNetTfm"));
        Assert.Equal("net11.0-tizen11.0", properties.Value("MauiTizenTargetFramework"));
    }

    [Fact]
    public void PropertyReader_LastDefinitionWins()
    {
        var properties = MSBuildPropertyReader.Read(
            """
            <Project>
              <PropertyGroup><Value>first</Value></PropertyGroup>
              <PropertyGroup><Value>second</Value></PropertyGroup>
            </Project>
            """);

        Assert.Equal("second", properties.Value("Value"));
    }
}
