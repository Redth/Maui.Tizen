using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Guards against re-introducing a locally-mutated copy of eng/manifests/source-disposition.schema.json.
/// That file is authored and owned by the foundation/import workstream; this generator treats it
/// as immutable input and must emit data conforming to it, never a parallel or edited shape.
/// </summary>
public class SchemaImmutabilityTests
{
    [Fact]
    public void Disposition_enum_is_exactly_the_five_foundation_defined_values()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.schema.json");
        var disposition = doc.RootElement
            .GetProperty("$defs").GetProperty("entry").GetProperty("properties").GetProperty("disposition");

        var values = disposition.GetProperty("oneOf")
            .EnumerateArray()
            .Select(o => o.GetProperty("const").GetString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { "exclude", "keep-upstream", "move", "rebuild", "rename" }.OrderBy(s => s, StringComparer.Ordinal),
            values);
    }

    [Fact]
    public void Kind_enum_is_exactly_the_four_foundation_defined_values()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.schema.json");
        var kind = doc.RootElement
            .GetProperty("$defs").GetProperty("entry").GetProperty("properties").GetProperty("kind");

        var values = kind.GetProperty("oneOf")
            .EnumerateArray()
            .Select(o => o.GetProperty("const").GetString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { "asset", "project", "shared-conditional", "tizen-specific" }.OrderBy(s => s, StringComparer.Ordinal),
            values);
    }

    [Fact]
    public void ExcludeDisposition_requires_shared_conditional_files_never_move()
    {
        using var doc = TestPaths.LoadJson("eng/manifests/source-disposition.schema.json");
        var allOf = doc.RootElement.GetProperty("$defs").GetProperty("entry").GetProperty("allOf");

        var sharedConditionalRule = allOf.EnumerateArray().FirstOrDefault(rule =>
            rule.TryGetProperty("if", out var cond)
            && cond.TryGetProperty("properties", out var props)
            && props.TryGetProperty("kind", out var kind)
            && kind.TryGetProperty("const", out var c)
            && c.GetString() == "shared-conditional");

        Assert.True(sharedConditionalRule.ValueKind != JsonValueKind.Undefined,
            "Schema must still constrain shared-conditional file dispositions.");

        var allowed = sharedConditionalRule
            .GetProperty("then").GetProperty("properties").GetProperty("disposition").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToHashSet();

        Assert.DoesNotContain("move", allowed);
        Assert.DoesNotContain("rename", allowed);
    }

    [Fact]
    public void No_manifest_entry_uses_a_disposition_outside_the_schema_enum()
    {
        using var schema = TestPaths.LoadJson("eng/manifests/source-disposition.schema.json");
        var allowed = schema.RootElement
            .GetProperty("$defs").GetProperty("entry").GetProperty("properties").GetProperty("disposition")
            .GetProperty("oneOf").EnumerateArray().Select(o => o.GetProperty("const").GetString()).ToHashSet();

        using var manifest = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
        {
            var disposition = entry.GetProperty("disposition").GetString();
            Assert.Contains(disposition, allowed);
        }
    }

    [Fact]
    public void LegacyTopLevelCompatibilityStack_is_excluded_not_rebuilt()
    {
        // Scope arbitration: src/Compatibility/** (absent from net11.0) stays "exclude" -- it is
        // not the same thing as src/Controls/src/Core/Compatibility/** (still present on net11.0,
        // which IS a "rebuild" candidate). Do not collapse this distinction.
        using var manifest = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!;
            if (path.StartsWith("src/Compatibility/", StringComparison.Ordinal))
            {
                Assert.Equal("exclude", entry.GetProperty("disposition").GetString());
            }
        }
    }

    [Fact]
    public void ControlsEmbeddedCompatibilityShim_is_still_present_and_a_rebuild_candidate()
    {
        using var manifest = TestPaths.LoadJson("eng/manifests/source-disposition.json");
        var shimEntries = manifest.RootElement.GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("path").GetString()!.Contains("src/Controls/src/Core/Compatibility/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(shimEntries);

        // Only the Tizen-specific-path files in this folder are the "rebuild" candidates (the
        // legacy renderer shim itself). A shared file that merely lives alongside them (e.g. a
        // shared handler with a small #if TIZEN branch) is correctly kind=shared-conditional and
        // may legitimately be keep-upstream instead -- see DetermineDisposition's shared-conditional
        // branch, which is independent of the Compatibility-path check.
        var tizenSpecificShimEntries = shimEntries.Where(e => e.GetProperty("kind").GetString() == "tizen-specific").ToList();
        Assert.NotEmpty(tizenSpecificShimEntries);
        Assert.All(tizenSpecificShimEntries, e => Assert.Equal("rebuild", e.GetProperty("disposition").GetString()));
    }
}
