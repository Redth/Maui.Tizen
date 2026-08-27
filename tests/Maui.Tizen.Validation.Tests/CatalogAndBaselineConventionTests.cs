using System.Text.RegularExpressions;

namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Integrity checks for the validation matrix, the control catalog and the visual-baseline tree.
/// </summary>
/// <remarks>
/// These three files describe work that mostly happens on hardware this repository does not own.
/// If they are allowed to become inconsistent, the device lane fails in ways that look like product
/// regressions. Validating them on every hosted build keeps that class of noise out of the device
/// lane entirely.
/// </remarks>
public partial class CatalogAndBaselineConventionTests
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern { get; }

    [Fact]
    public void ProfileMatrix_IsWellFormed()
    {
        var profiles = TizenProfiles.Profiles;

        Assert.NotEmpty(profiles);

        foreach (var profile in profiles)
        {
            Assert.Matches(SlugPattern, profile.Id);
            Assert.False(string.IsNullOrWhiteSpace(profile.DisplayName), $"Profile '{profile.Id}' has no display name.");
            Assert.NotEmpty(profile.Themes);
            Assert.NotEmpty(profile.Densities);
            Assert.Equal(
                profile.Densities.OrderBy(d => d, StringComparer.Ordinal),
                profile.VisualTargets.Keys.OrderBy(d => d, StringComparer.Ordinal));
            Assert.All(
                profile.VisualTargets,
                target =>
                {
                    Assert.True(target.Value.Width > 0 && target.Value.Height > 0);
                    Assert.True(target.Value.DisplayDensity > 0);
                });
            Assert.NotEmpty(profile.InputMethods);

            Assert.Contains(
                profile.PrimaryInput,
                profile.InputMethods,
                StringComparer.Ordinal);

            Assert.True(profile.Screen.Width > 0 && profile.Screen.Height > 0,
                $"Profile '{profile.Id}' has a degenerate screen size.");
        }

        Assert.Distinct(profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileMatrix_EmulatorNotesCarryNoPersonalInfrastructure()
    {
        // Device infrastructure must never be pinned to a person, host or account in the repository.
        foreach (var profile in TizenProfiles.Profiles)
        {
            var notes = profile.Emulator.Notes ?? string.Empty;

            Assert.DoesNotContain("@", notes, StringComparison.Ordinal);
            Assert.DoesNotContain("http://", notes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", notes, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TvProfile_RequiresFocusNavigation()
    {
        // A TV lane that does not exercise remote focus traversal is not testing the thing that
        // most commonly breaks on TV.
        var tv = TizenProfiles.Profile("tv");

        Assert.True(tv.RequiresFocusNavigation);
        Assert.Equal("remote", tv.PrimaryInput);
    }

    [Fact]
    public void UnconfirmedAlsoValidTargets_DoNotGateRelease()
    {
        foreach (var target in TizenProfiles.Matrix.AlsoValidTargets)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(target.TargetFramework),
                "An also-valid target must declare a target framework.");

            Assert.StartsWith("net", target.TargetFramework, StringComparison.Ordinal);
            Assert.Contains("-tizen", target.TargetFramework, StringComparison.Ordinal);
        }

        // Release gating is defined by eng/baselines.json > target. The also-valid list is
        // opportunistic coverage only, so nothing in it may claim to be confirmed without a note.
        Assert.All(
            TizenProfiles.Matrix.AlsoValidTargets,
            t => Assert.False(
                t.Confirmed,
                $"'{t.TargetFramework}' is marked confirmed. Confirmed targets must be promoted into " +
                "eng/baselines.json rather than tracked here."));
    }

    [Fact]
    public void CatalogManifest_IsWellFormed()
    {
        ValidationSkip.When(!ControlCatalog.Exists, "The control catalog manifest is not present.");

        var manifest = ControlCatalog.Manifest;
        var knownProfiles = TizenProfiles.Profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedInteractions = manifest.Interactions.Allowed.ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(manifest.Cases);
        Assert.NotEmpty(allowedInteractions);
        Assert.Distinct(manifest.Cases.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var catalogCase in manifest.Cases)
        {
            Assert.Matches(SlugPattern, catalogCase.Id);

            Assert.False(
                string.IsNullOrWhiteSpace(catalogCase.Description),
                $"Case '{catalogCase.Id}' has no description; a catalog entry nobody can interpret is dead weight.");

            Assert.NotEmpty(catalogCase.Profiles);

            foreach (var profile in catalogCase.Profiles)
            {
                Assert.True(
                    knownProfiles.Contains(profile),
                    $"Case '{catalogCase.Id}' targets unknown profile '{profile}'.");
            }

            foreach (var interaction in catalogCase.Interactions)
            {
                Assert.True(
                    allowedInteractions.Contains(interaction),
                    $"Case '{catalogCase.Id}' uses interaction '{interaction}', which is not in the allowed vocabulary.");
            }
        }
    }

    [Fact]
    public void CatalogCases_RequiringRemoteNavigation_TargetTheTvProfile()
    {
        ValidationSkip.When(!ControlCatalog.Exists, "The control catalog manifest is not present.");

        foreach (var catalogCase in ControlCatalog.Cases.Where(c => c.Interactions.Contains("remote-navigate", StringComparer.Ordinal)))
        {
            Assert.True(
                catalogCase.AppliesTo("tv"),
                $"Case '{catalogCase.Id}' declares remote-navigate but does not target the tv profile.");
        }
    }

    [Fact]
    public void BaselineVariants_ExpandFromTheProfileMatrix()
    {
        var variants = TizenProfiles.EnumerateBaselineVariants().ToList();

        Assert.NotEmpty(variants);
        Assert.Distinct(variants.Select(v => v.ToString()), StringComparer.Ordinal);

        // mobile: 2 themes x 3 densities, tv: 1 theme x 2 densities.
        Assert.Equal(8, variants.Count);
    }

    [Fact]
    public void BaselinePaths_RoundTrip()
    {
        var variant = new BaselineVariant("mobile", "dark", "hdpi");
        var path = VisualBaselines.ImagePath(variant, "API15", "button-default");

        Assert.True(VisualBaselines.TryParsePath(path, out var address));
        Assert.Equal(variant, address.Variant);
        Assert.Equal("API15", address.ApiLevel);
        Assert.Equal("button-default", address.CaseId);
    }

    [Fact]
    public void BaselinePaths_RejectNonConformingLayout()
    {
        var flat = Path.Combine(RepoLayout.VisualBaselineRoot, "button-default.png");
        Assert.False(VisualBaselines.TryParsePath(flat, out _));
    }

    [Fact]
    public void CheckedInBaselines_MapToCatalogCasesAndKnownVariants()
    {
        var images = VisualBaselines.EnumerateImages();

        ValidationSkip.When(
            images.Count == 0,
            "No visual baselines are checked in yet. They are produced by the device lane once the " +
            "Samsung workload is available; see docs/validation/visual-baselines.md.");

        var knownVariants = TizenProfiles.EnumerateBaselineVariants().Select(v => v.ToString()).ToHashSet(StringComparer.Ordinal);
        var knownCases = ControlCatalog.Cases.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            Assert.True(
                VisualBaselines.TryParsePath(image, out var address),
                $"'{RepoLayout.Relative(image)}' does not follow the baseline layout convention.");

            Assert.True(
                knownVariants.Contains(address.Variant.ToString()),
                $"'{RepoLayout.Relative(image)}' uses variant '{address.Variant}', which the profile matrix does not define.");

            Assert.True(
                knownCases.Contains(address.CaseId),
                $"'{RepoLayout.Relative(image)}' has no matching catalog case '{address.CaseId}'. " +
                "Orphaned baselines must be deleted with the case that produced them.");

            var metadataPath = Path.ChangeExtension(image, ".json");
            Assert.True(
                File.Exists(metadataPath),
                $"'{RepoLayout.Relative(image)}' has no metadata sidecar. Without provenance a stale " +
                "baseline is indistinguishable from a correct one.");

            var metadata = VisualBaselines.ReadMetadata(metadataPath);
            var decoded = PngImage.Load(image);

            Assert.Equal(address.CaseId, metadata.CaseId);
            Assert.Equal(address.Variant.Profile, metadata.Profile);
            Assert.Equal(address.Variant.Theme, metadata.Theme);
            Assert.Equal(address.Variant.Density, metadata.Density);
            Assert.Equal(address.ApiLevel, metadata.ApiLevel);

            Assert.Equal(decoded.Width, metadata.Width);
            Assert.Equal(decoded.Height, metadata.Height);

            Assert.False(
                string.IsNullOrWhiteSpace(metadata.Commit),
                $"'{RepoLayout.Relative(metadataPath)}' does not record the commit it was captured at.");

            if (metadata.Tolerance is not null)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(metadata.ToleranceJustification),
                    $"'{RepoLayout.Relative(metadataPath)}' widens the default tolerance without justification. " +
                    "An unexplained tolerance bump is how a real regression gets absorbed.");
            }
        }
    }
}
