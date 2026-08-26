using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Consistency checks for eng/api-baselines/net9.0-tizen7.0 (the last-published MAUI Tizen
/// NuGet API surface, captured via eng/tools/ApiDump). Offline: only re-hashes already-committed
/// files, never re-downloads or re-runs ApiDump.
/// </summary>
public class PublishedApiBaselineTests
{
    private const string OutDir = "eng/api-baselines/net9.0-tizen7.0";

    [Fact]
    public void Manifest_exists_and_pins_the_baselines_json_version()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");

        var packageVersion = manifest.RootElement.GetProperty("packageVersion").GetString();
        var expectedTag = baselines.RootElement.GetProperty("source").GetProperty("behaviorBaseline").GetProperty("tag").GetString();

        Assert.Equal(expectedTag, packageVersion);
    }

    [Fact]
    public void Every_package_with_a_tizen_assembly_has_a_dump_file_with_matching_repository_commit()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");
        var expectedCommit = baselines.RootElement.GetProperty("source").GetProperty("behaviorBaseline").GetProperty("commit").GetString();

        var packages = manifest.RootElement.GetProperty("packages").EnumerateArray().ToList();
        Assert.NotEmpty(packages);

        foreach (var pkg in packages)
        {
            var hasAssembly = pkg.GetProperty("hasTizenAssembly").GetBoolean();
            if (!hasAssembly)
            {
                continue;
            }

            Assert.Equal(expectedCommit, pkg.GetProperty("nuspecRepositoryCommit").GetString());

            foreach (var asmFile in pkg.GetProperty("assemblies").EnumerateArray())
            {
                var assemblyName = Path.GetFileNameWithoutExtension(asmFile.GetString());
                var dumpPath = TestPaths.Path_(OutDir, assemblyName + ".json");
                Assert.True(File.Exists(dumpPath), $"Missing API dump for {assemblyName} at {dumpPath}");

                using var dump = JsonDocument.Parse(File.ReadAllText(dumpPath));
                var types = dump.RootElement.GetProperty("types");
                Assert.True(types.GetArrayLength() > 0, $"{assemblyName}.json has no public types recorded");

                var sha256 = dump.RootElement.GetProperty("sha256").GetString();
                Assert.Matches("^[0-9a-f]{64}$", sha256!);
            }
        }
    }

    [Fact]
    public void Type_entries_are_sorted_deterministically()
    {
        foreach (var file in Directory.GetFiles(TestPaths.Path_(OutDir), "*.json"))
        {
            if (Path.GetFileName(file) == "manifest.json")
            {
                continue;
            }

            using var dump = JsonDocument.Parse(File.ReadAllText(file));
            var names = dump.RootElement.GetProperty("types")
                .EnumerateArray()
                .Select(t =>
                {
                    var ns = t.GetProperty("namespace").GetString()!;
                    var name = t.GetProperty("name").GetString()!;
                    var fullName = ns.Length == 0 ? name : $"{ns}.{name}";
                    return (fullName, t.GetProperty("arity").GetInt32());
                })
                .ToList();

            var sorted = names
                .OrderBy(n => n.Item1, StringComparer.Ordinal)
                .ThenBy(n => n.Item2)
                .ToList();

            Assert.Equal(sorted, names);
        }
    }
}
