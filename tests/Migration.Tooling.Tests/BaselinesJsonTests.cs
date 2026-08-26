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
    public void Development_package_baseline_shares_the_required_ancestor_commit()
    {
        // The dnceng dotnet11 preview package used as the "coherent package baseline" must be
        // built from the same commit as the minimum API floor (PR #36657), otherwise the two
        // baselines silently drift apart.
        using var doc = TestPaths.LoadJson("eng/baselines.json");
        var source = doc.RootElement.GetProperty("source");

        var requiredAncestor = source.GetProperty("requiredAncestor").GetProperty("commit").GetString();
        var devPackageCommit = source.GetProperty("developmentPackageBaseline").GetProperty("nuspecRepositoryCommit").GetString();

        Assert.Equal(requiredAncestor, devPackageCommit);
    }
}
