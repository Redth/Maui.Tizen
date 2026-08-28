namespace Maui.Tizen.Conventions.Tests;

/// <summary>
/// Conventions for the Roslyn public-API tracking files.
/// </summary>
/// <remarks>
/// <para>
/// The API gate for Tizen-targeted assemblies is <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c>,
/// which runs during compilation of the Tizen TFM. That gate cannot run anywhere until the Samsung
/// workload ships.
/// </para>
/// <para>
/// What can be checked now is the health of the tracking files themselves. A malformed, unsorted or
/// duplicated <c>PublicAPI.*.txt</c> makes the analyzer's diff unreadable exactly when it matters -
/// during the API review that the file exists to enable.
/// </para>
/// </remarks>
public class PublicApiFileConventionTests
{
    const string NullableHeader = "#nullable enable";

    static IReadOnlyList<string> ShippedFiles() =>
        Directory.Exists(RepoLayout.Src)
            ? [.. Directory.GetFiles(RepoLayout.Src, "PublicAPI.Shipped.txt", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)]
            : [];

    static IReadOnlyList<string> AllApiFiles() =>
        Directory.Exists(RepoLayout.Src)
            ? [.. Directory.GetFiles(RepoLayout.Src, "PublicAPI.*.txt", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)]
            : [];

    /// <summary>Entries excluding the header and blank lines.</summary>
    static IReadOnlyList<string> Entries(string path) =>
        [.. File.ReadAllLines(path)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))];

    [Fact]
    public void EveryShippedFileHasAnUnshippedSibling()
    {
        var shipped = ShippedFiles();
        ValidationSkip.When(shipped.Count == 0, "No PublicAPI tracking files are present.");

        foreach (var file in shipped)
        {
            var unshipped = Path.Combine(Path.GetDirectoryName(file)!, "PublicAPI.Unshipped.txt");

            Assert.True(
                File.Exists(unshipped),
                $"'{RepoLayout.Relative(file)}' has no PublicAPI.Unshipped.txt sibling. The analyzer " +
                "requires both, and without the unshipped file every new API is reported as an error " +
                "with nowhere to record it.");
        }
    }

    [Fact]
    public void ApiFilesDeclareNullableAnnotations()
    {
        var files = AllApiFiles();
        ValidationSkip.When(files.Count == 0, "No PublicAPI tracking files are present.");

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            // An empty Unshipped file is legitimate and needs no header.
            if (lines.Length == 0 || lines.All(string.IsNullOrWhiteSpace))
                continue;

            Assert.True(
                lines.Any(l => l.Trim() == NullableHeader),
                $"'{RepoLayout.Relative(file)}' does not declare '{NullableHeader}'. Without it the " +
                "analyzer records every signature without nullability, and the first annotated build " +
                "reports the entire surface as changed.");
        }
    }

    [Fact]
    public void ApiFilesContainNoDuplicateEntries()
    {
        var files = AllApiFiles();
        ValidationSkip.When(files.Count == 0, "No PublicAPI tracking files are present.");

        foreach (var file in files)
        {
            var duplicates = Entries(file)
                .GroupBy(e => e, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(
                duplicates.Count == 0,
                $"'{RepoLayout.Relative(file)}' contains duplicate entries:{Environment.NewLine}" +
                string.Join(Environment.NewLine, duplicates.Select(d => "    " + d)));
        }
    }

    [Fact]
    public void ApiEntriesHaveNoStrayWhitespace()
    {
        var files = AllApiFiles();
        ValidationSkip.When(files.Count == 0, "No PublicAPI tracking files are present.");

        // Deliberately NOT an ordinal-sort assertion. PublicApiAnalyzers orders entries with its
        // own comparer, and the imported dotnet/maui files follow that ordering; asserting ordinal
        // sorting here would flag the entire inherited surface as broken for no benefit.
        // Stray whitespace, by contrast, silently prevents an entry from matching a real signature.
        foreach (var file in files)
        {
            foreach (var entry in Entries(file))
            {
                Assert.True(
                    entry == entry.Trim(),
                    $"'{RepoLayout.Relative(file)}' has an entry with leading or trailing whitespace: " +
                    $"'{entry}'. The analyzer compares entries literally, so it will not match.");
            }
        }
    }

    [Fact]
    public void UnshippedApiIsEmptyBeforeARelease()
    {
        // Release-only gate. Pending unshipped API is entirely normal during development - the
        // imported baseline starts with hundreds of entries - so failing pull requests on it would
        // be pure noise. It matters at exactly one moment: shipping.
        ValidationSkip.When(
            Environment.GetEnvironmentVariable("MAUI_TIZEN_RELEASE_VALIDATION") != "1",
            "Release-only gate. Set MAUI_TIZEN_RELEASE_VALIDATION=1 to run it; the release workflow " +
            "does this automatically. See docs/validation/ci.md.");

        var files = AllApiFiles().Where(f => f.EndsWith("PublicAPI.Unshipped.txt", StringComparison.Ordinal)).ToList();
        ValidationSkip.When(files.Count == 0, "No PublicAPI tracking files are present.");
        var pending = files
            .Select(f => (File: RepoLayout.Relative(f), Count: Entries(f).Count))
            .Where(x => x.Count > 0)
            .ToList();

        Assert.True(
            pending.Count == 0,
            $"""
             The following files declare unshipped public API awaiting review:
             {string.Join(Environment.NewLine, pending.Select(p => $"    {p.File}: {p.Count} entr(y/ies)"))}

             Move them to PublicAPI.Shipped.txt as part of the release that ships them.
             """);
    }

    [Fact]
    public void ApiFilesLiveUnderATargetFrameworkSpecificFolder()
    {
        var files = AllApiFiles();
        ValidationSkip.When(files.Count == 0, "No PublicAPI tracking files are present.");

        foreach (var file in files)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(file)!);

            Assert.False(
                string.Equals(parent, "PublicAPI", StringComparison.Ordinal),
                $"'{RepoLayout.Relative(file)}' sits directly under PublicAPI/. Tracking files must be " +
                "in a TFM-specific subfolder (for example PublicAPI/net-tizen/) so a future additional " +
                "target framework does not overwrite this one's recorded surface.");
        }
    }
}
