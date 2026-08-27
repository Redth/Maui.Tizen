namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Compares device-captured screenshots against the checked-in baselines.
/// </summary>
/// <remarks>
/// <para>
/// The device lane used to only capture, so it could not fail on a rendering regression: it
/// produced images that nothing ever compared to anything. This is the comparison half.
/// </para>
/// <para>
/// It runs host-side, on whichever machine holds the captures - the self-hosted runner after
/// pulling them back from the device. Comparison needs no device, and keeping it here means it uses
/// the same deterministic comparer the rest of the suite is tested against.
/// </para>
/// <para>
/// Captures are laid out exactly like baselines
/// (<c>{profile}/{apiLevel}/{theme}/{density}/{caseId}.png</c>), so each one maps to precisely one
/// baseline. Any other arrangement requires guessing which baseline a capture belongs to, and a
/// wrong guess produces a failure that looks like a rendering change.
/// </para>
/// </remarks>
public class VisualBaselineComparisonTests
{
    /// <summary>Root the device lane captures into.</summary>
    public static string ScreenshotRoot => Path.Combine(RepoLayout.Root, "artifacts", "screenshots");

    static string DiffRoot => Path.Combine(RepoLayout.Root, "artifacts", "visual-diffs");

    /// <summary>Set by the device lane so a hosted run does not look for captures that cannot exist.</summary>
    static bool ComparisonRequested =>
        Environment.GetEnvironmentVariable("MAUI_TIZEN_COMPARE_BASELINES") == "1";

    static IReadOnlyList<string> Captures() =>
        Directory.Exists(ScreenshotRoot)
            ? [.. Directory.EnumerateFiles(ScreenshotRoot, "*.png", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)]
            : [];

    /// <summary>Parses <c>{profile}/{apiLevel}/{theme}/{density}/{caseId}.png</c>.</summary>
    static bool TryParseCapture(string path, out BaselineAddress address)
    {
        address = default!;

        var relative = Path.GetRelativePath(ScreenshotRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 5)
            return false;

        address = new BaselineAddress(
            new BaselineVariant(segments[0], segments[2], segments[3]),
            segments[1],
            Path.GetFileNameWithoutExtension(segments[4]));

        return true;
    }

    [Fact]
    public void CapturedScreenshotsMatchTheirBaselines()
    {
        var captures = Captures();

        ValidationSkip.When(
            !ComparisonRequested && captures.Count == 0,
            "No device captures are present. The device lane sets MAUI_TIZEN_COMPARE_BASELINES=1 " +
            "after capturing; see docs/validation/visual-baselines.md.");

        // Once the device lane has asked for a comparison, finding nothing to compare is a failure.
        // Otherwise a capture step that silently produced no files would read as a clean pass.
        Assert.True(
            captures.Count > 0,
            $"A baseline comparison was requested but '{RepoLayout.Relative(ScreenshotRoot)}' " +
            "contains no screenshots. The capture step produced nothing.");

        var tolerance = TizenProfiles.Matrix.VisualBaselines.DefaultTolerance;
        var failures = new List<string>();
        var compared = 0;

        foreach (var capture in captures)
        {
            Assert.True(
                TryParseCapture(capture, out var address),
                $"'{RepoLayout.Relative(capture)}' does not follow " +
                "{profile}/{apiLevel}/{theme}/{density}/{caseId}.png, so it cannot be matched to a baseline.");

            var baselinePath = VisualBaselines.ImagePath(address.Variant, address.ApiLevel, address.CaseId);

            if (!File.Exists(baselinePath))
            {
                failures.Add(
                    $"  {address}: no baseline at {RepoLayout.Relative(baselinePath)}. " +
                    "Review the capture and commit it with its metadata sidecar.");
                continue;
            }

            var expected = PngImage.Load(baselinePath);
            var actual = PngImage.Load(capture);

            // A per-baseline override is honoured, but only with a justification - enforced by
            // CatalogAndBaselineConventionTests.
            var metadataPath = Path.ChangeExtension(baselinePath, ".json");
            var effectiveTolerance = File.Exists(metadataPath)
                ? VisualBaselines.ReadMetadata(metadataPath).Tolerance ?? tolerance
                : tolerance;

            var result = ImageComparer.Compare(expected, actual, effectiveTolerance);
            compared++;

            if (result.Passed)
                continue;

            var artifacts = ImageComparer.WriteFailureArtifacts(
                DiffRoot, address.ToString(), expected, actual, result);

            failures.Add($"  {result.Describe(address.ToString())}{Environment.NewLine}      artifacts: {RepoLayout.Relative(artifacts)}");
        }

        Assert.True(
            failures.Count == 0,
            $"""
             {failures.Count} of {captures.Count} capture(s) did not match their baseline:

             {string.Join(Environment.NewLine, failures)}

             expected.png, actual.png and diff.png for each failure are under
             {RepoLayout.Relative(DiffRoot)} and are uploaded by the device workflow.
             """);

        Assert.True(compared > 0, "No capture was compared to a baseline.");
    }

    [Fact]
    public void EveryDeclaredCaseWasCapturedForTheProfileUnderTest()
    {
        ValidationSkip.When(!ComparisonRequested, "Only meaningful during a device-lane comparison.");

        var profile = Environment.GetEnvironmentVariable("MAUI_TIZEN_SCREENSHOT_PROFILE");
        ValidationSkip.When(string.IsNullOrEmpty(profile), "MAUI_TIZEN_SCREENSHOT_PROFILE is not set.");

        var captured = Captures()
            .Where(p => TryParseCapture(p, out _))
            .Select(p => { TryParseCapture(p, out var a); return a.CaseId; })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ControlCatalog
            .BaselineCasesFor(profile!)
            .Where(c => !captured.Contains(c.Id))
            .Select(c => c.Id)
            .ToList();

        // Guards the other direction: comparing only what happened to be captured would let a
        // case silently drop out of the run.
        Assert.True(
            missing.Count == 0,
            $"""
             These catalog cases declare capturesBaseline for profile '{profile}' but produced no
             screenshot:
             {string.Join(Environment.NewLine, missing.Select(m => "    " + m))}
             """);
    }
}
