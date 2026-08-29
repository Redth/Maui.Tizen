using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Migration.Tooling.Tests;

/// <summary>
/// Schema and self-consistency checks for eng/manifests/source-disposition.json against the
/// foundation-owned eng/manifests/source-disposition.schema.json contract. Offline-only (no
/// network, no Tizen workload): they catch a manifest that was hand-edited without being
/// regenerated via eng/scripts/generate-source-inventory.ps1, or that has drifted from its own
/// schema or from eng/baselines.json.
/// </summary>
public class SourceDispositionManifestTests
{
    private static readonly string ManifestPath = TestPaths.Path_("eng", "manifests", "source-disposition.json");
    private static readonly string SchemaPath = TestPaths.Path_("eng", "manifests", "source-disposition.schema.json");

    [Fact]
    public void Manifest_and_schema_files_exist()
    {
        Assert.True(File.Exists(ManifestPath), $"Missing {ManifestPath}");
        Assert.True(File.Exists(SchemaPath), $"Missing {SchemaPath}");
    }

    [Fact]
    public void Schema_file_is_a_valid_json_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(SchemaPath));
        Assert.NotNull(schema);
    }

    [Fact]
    public void Manifest_validates_against_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(SchemaPath));
        var instance = JsonNode.Parse(File.ReadAllText(ManifestPath));

        var result = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, DescribeFailures(result));
    }

    [Fact]
    public void No_duplicate_source_paths()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        var paths = doc.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!)
            .ToList();

        var duplicates = paths.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate 'path' values: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void No_two_source_files_map_to_the_same_target_path()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        var targetPaths = doc.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(e => e.TryGetProperty("targetPath", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)
            .Where(t => t is not null)
            .ToList();

        var duplicates = targetPaths.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate 'targetPath' values: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Entries_are_sorted_by_path()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        var paths = doc.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!)
            .ToList();

        var sorted = paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, paths);
    }

    [Fact]
    public void Manifest_source_refs_match_baselines_json()
    {
        using var manifest = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");

        var generatedFrom = manifest.RootElement.GetProperty("generatedFrom");
        var baselineSource = baselines.RootElement.GetProperty("source");

        Assert.Equal(
            baselineSource.GetProperty("sourceBaseline").GetProperty("commit").GetString(),
            generatedFrom.GetProperty("sourceBaseline").GetString());
        Assert.Equal(
            baselineSource.GetProperty("behaviorBaseline").GetProperty("commit").GetString(),
            generatedFrom.GetProperty("behaviorBaseline").GetString());
    }

    [Fact]
    public void Every_entry_present_only_at_behaviorBaseline_has_notes()
    {
        // Schema requires this for "exclude", but the intent (per docs/manifests/README.md:
        // "we forgot about it" must never be a possible outcome) is that every
        // behaviorBaseline-only entry is explained, which this asserts directly rather than only
        // indirectly through the exclude-implies-notes schema rule.
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (entry.GetProperty("sourceRef").GetString() == "behaviorBaseline")
            {
                Assert.True(entry.TryGetProperty("notes", out var notes) && notes.GetString()!.Length >= 10,
                    $"{entry.GetProperty("path").GetString()}: behaviorBaseline-only entries must carry explanatory notes");
            }
        }
    }

    private static string DescribeFailures(EvaluationResults result)
    {
        var details = result.Details
            .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} - {e.Value}"))
            .Take(20);
        return string.Join("\n", details);
    }
}
