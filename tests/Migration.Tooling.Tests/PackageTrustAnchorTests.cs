using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Structural checks for eng/api-baselines/net9.0-tizen7.0-package-trust.json, the repository
/// trust anchor generate-api-baseline.ps1 checks every downloaded/cached package against. This is
/// deliberately NOT trust-on-first-use: every package the generator downloads must already have an
/// entry here, pinned ahead of time, or generation refuses to proceed.
/// </summary>
public class PackageTrustAnchorTests
{
    private const string TrustAnchorPath = "eng/api-baselines/net9.0-tizen7.0-package-trust.json";

    private static readonly string[] ExpectedPackageIds =
    [
        "Microsoft.Maui.Core", "Microsoft.Maui.Essentials", "Microsoft.Maui.Graphics",
        "Microsoft.Maui.Controls.Core", "Microsoft.Maui.Controls.Xaml",
        "Microsoft.Maui.Controls.Compatibility", "Microsoft.Maui.Controls.Maps",
    ];

    [Fact]
    public void Trust_anchor_pins_the_behaviorBaseline_version()
    {
        using var trustAnchor = TestPaths.LoadJson(TrustAnchorPath);
        using var baselines = TestPaths.LoadJson("eng/baselines.json");

        var expectedTag = baselines.RootElement.GetProperty("source").GetProperty("behaviorBaseline").GetProperty("tag").GetString();
        Assert.Equal(expectedTag, trustAnchor.RootElement.GetProperty("packageVersion").GetString());
    }

    [Fact]
    public void Trust_anchor_has_exactly_the_expected_packages_with_valid_hashes()
    {
        using var trustAnchor = TestPaths.LoadJson(TrustAnchorPath);
        var packages = trustAnchor.RootElement.GetProperty("packages").EnumerateArray().ToList();

        var actualIds = packages.Select(p => p.GetProperty("packageId").GetString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedPackageIds.OrderBy(s => s, StringComparer.Ordinal), actualIds);

        foreach (var pkg in packages)
        {
            Assert.Matches("^[0-9a-f]{64}$", pkg.GetProperty("nupkgSha256").GetString()!);
        }
    }

    [Fact]
    public void Trust_anchor_has_no_duplicate_package_ids()
    {
        using var trustAnchor = TestPaths.LoadJson(TrustAnchorPath);
        var ids = trustAnchor.RootElement.GetProperty("packages").EnumerateArray()
            .Select(p => p.GetProperty("packageId").GetString())
            .ToList();

        var duplicates = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate packageId values in trust anchor: " + string.Join(", ", duplicates));
    }
}
