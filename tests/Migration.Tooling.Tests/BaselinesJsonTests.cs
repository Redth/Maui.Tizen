using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migration.Tooling.Tests;

/// <summary>Structural sanity checks for eng/baselines.json, the single source of truth other
/// tools/scripts read pinned refs from.</summary>
public partial class BaselinesJsonTests
{
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullShaRegex();

    [Fact]
    public void Pinned_commits_are_full_lowercase_shas()
    {
        using var doc = TestPaths.LoadJson("eng/baselines.json");
        var source = doc.RootElement.GetProperty("source");

        Assert.Matches(FullShaRegex(), source.GetProperty("sourceBaseline").GetProperty("commit").GetString()!);
        Assert.Matches(FullShaRegex(), source.GetProperty("requiredAncestor").GetProperty("commit").GetString()!);
        Assert.Matches(FullShaRegex(), source.GetProperty("behaviorBaseline").GetProperty("commit").GetString()!);
        Assert.Matches(FullShaRegex(), source.GetProperty("developmentPackageBaseline").GetProperty("nuspecRepositoryCommit").GetString()!);
    }

    [Fact]
    public void Development_package_baseline_commit_is_a_documented_ref()
    {
        // The dnceng dotnet11 preview package's nuspec repository commit must be a ref this
        // manifest already documents an explanation for -- either the minimum API floor commit
        // itself (PR #36657), or a later commit explicitly recorded as the observed point of
        // eng/baselines.json's source.sourceBaseline.knownGapAfterThisPin (the intentional gap
        // between the pinned source import and later net11.0 commits). This is deliberately NOT
        // an exact-equality check against requiredAncestor: the coordinator's package baseline is
        // expected to move forward past the source-import pin over time (e.g. to pick up published
        // API for commits the import intentionally does not include), while the source import
        // itself stays frozen. An arbitrary, undocumented commit here would still fail.
        using var doc = TestPaths.LoadJson("eng/baselines.json");
        var source = doc.RootElement.GetProperty("source");

        var requiredAncestor = source.GetProperty("requiredAncestor").GetProperty("commit").GetString();
        var devPackageCommit = source.GetProperty("developmentPackageBaseline").GetProperty("nuspecRepositoryCommit").GetString();

        var documentedRefs = new List<string?> { requiredAncestor };

        if (source.GetProperty("sourceBaseline").TryGetProperty("knownGapAfterThisPin", out var gap) &&
            gap.TryGetProperty("observedAgainst", out var observedAgainst))
        {
            documentedRefs.Add(observedAgainst.GetString());
        }

        Assert.Contains(devPackageCommit, documentedRefs);
    }
}
