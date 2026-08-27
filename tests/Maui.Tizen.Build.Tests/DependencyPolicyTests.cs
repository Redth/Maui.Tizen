namespace Maui.Tizen.Build.Tests;

/// <summary>
/// Enforces the repository's dependency policy against real published package graphs.
/// </summary>
/// <remarks>
/// A banned package version is almost never referenced directly. It arrives transitively - here,
/// through <c>Tizen.UIExtensions</c> - and on Tizen the symptom is a missing-type failure at
/// runtime on a device rather than a build error. Checking the declared graph on the hosted lane
/// turns that into an ordinary pull-request failure.
/// </remarks>
public class DependencyPolicyTests
{
    [Fact]
    public void PolicyEngine_DetectsABannedTransitiveResolution()
    {
        // Engine check: runs everywhere, no network, no packages.
        var rule = new BannedResolution
        {
            Id = "graphics-6x",
            PackageId = "Microsoft.Maui.Graphics",
            BannedVersionPrefixes = ["6."],
            Reason = "test",
            CommonSources = ["Tizen.UIExtensions.NUI"],
        };

        Assert.True(rule.IsViolatedBy("Microsoft.Maui.Graphics", "6.0.400"));
        Assert.True(rule.IsViolatedBy("microsoft.maui.graphics", "6.0.0"));
        Assert.False(rule.IsViolatedBy("Microsoft.Maui.Graphics", "11.0.0-preview.7.26418.3"));
        Assert.False(rule.IsViolatedBy("Microsoft.Maui.Core", "6.0.400"));
    }

    [Fact]
    public void PolicyEngine_NamesTheDependentThatPulledTheBannedPackageIn()
    {
        using var workspace = TempWorkspace.Create("policy-graph");

        // A minimal but structurally real assets file: the banned package arrives through
        // Tizen.UIExtensions.NUI, never as a direct reference.
        var assets = workspace.WriteFile("obj/project.assets.json",
            """
            {
              "targets": {
                "net11.0-tizen11.0": {
                  "Tizen.UIExtensions.NUI/0.9.2": {
                    "type": "package",
                    "dependencies": { "Microsoft.Maui.Graphics": "6.0.400" }
                  },
                  "Microsoft.Maui.Graphics/6.0.400": { "type": "package" }
                }
              }
            }
            """);

        var graph = RestoreGraph.Load(assets);
        var violations = graph.EvaluatePolicy(TizenProfiles.Matrix.DependencyPolicy);

        var violation = Assert.Single(violations);
        var description = violation.Describe();

        Assert.Contains("Microsoft.Maui.Graphics/6.0.400", description, StringComparison.Ordinal);
        Assert.Contains("Tizen.UIExtensions.NUI/0.9.2", description, StringComparison.Ordinal);
        Assert.Contains("net11.0-tizen11.0", description, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyEngine_PassesACleanGraph()
    {
        using var workspace = TempWorkspace.Create("policy-clean");

        var assets = workspace.WriteFile("obj/project.assets.json",
            """
            {
              "targets": {
                "net11.0-tizen11.0": {
                  "Microsoft.Maui.Graphics/11.0.0-preview.7.26418.3": { "type": "package" }
                }
              }
            }
            """);

        Assert.Empty(RestoreGraph.Load(assets).EvaluatePolicy(TizenProfiles.Matrix.DependencyPolicy));
    }

    [Theory]
    [InlineData("6.0.400", "6.0.400")]
    [InlineData("[6.0.400, )", "6.0.400")]
    [InlineData("[6.0.400, 7.0.0)", "6.0.400")]
    [InlineData("(, 7.0.0)", "")]
    public void VersionRangeLowerBound_IsWhatRestoreWouldPick(string range, string expected) =>
        Assert.Equal(expected, PackageDependencyProbe.LowerBound(range));

    /// <summary>
    /// Tripwire against the real published package.
    /// </summary>
    /// <remarks>
    /// Asserts in both directions. While <c>Tizen.UIExtensions</c> still carries .NET 6-era
    /// Graphics dependencies the rule is recorded as a known violation and this passes. The moment
    /// Samsung republishes without them, this fails and says to flip the rule to enforcing - which
    /// is the only reliable way to stop a stale exemption outliving the problem it described.
    /// </remarks>
    [Fact]
    public async Task PublishedUIExtensions_MatchesItsRecordedDependencyStatus()
    {
        foreach (var rule in TizenProfiles.Matrix.DependencyPolicy.BannedResolutions)
        {
            if (rule.ProbePackage is not { Id.Length: > 0 } probe)
                continue;

            var dependencies = await PackageDependencyProbe
                .TryReadDependenciesAsync(probe.Id, probe.Version, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            ValidationSkip.When(
                dependencies is null,
                $"'{probe.Id}/{probe.Version}' is neither in the NuGet cache nor reachable on " +
                "nuget.org from this runner, so its dependency graph cannot be verified here.");

            var offending = dependencies!
                .Where(d => rule.IsViolatedBy(d.Id, PackageDependencyProbe.LowerBound(d.VersionRange)))
                .ToList();

            if (rule.IsKnownViolation)
            {
                Assert.True(
                    offending.Count > 0,
                    $"""
                     Dependency rule '{rule.Id}' is recorded as expectedStatus="known-violation",
                     but '{probe.Id}/{probe.Version}' no longer declares a banned
                     {rule.Description} dependency.

                     This is good news: the external prerequisite has been met. Update
                     eng/validation/profiles/tizen-profiles.json to set expectedStatus="clean" so
                     the rule starts being enforced, and drop the corresponding note from
                     eng/baselines.json > target.notes.
                     """);
            }
            else
            {
                Assert.True(
                    offending.Count == 0,
                    $"""
                     Dependency rule '{rule.Id}' is enforcing, but '{probe.Id}/{probe.Version}'
                     declares: {string.Join(", ", offending.Select(o => $"{o.Id} {o.VersionRange} ({o.TargetFramework})"))}.

                     {rule.Reason}
                     """);
            }
        }
    }
}
