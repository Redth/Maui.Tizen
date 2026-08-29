using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Consistency checks for eng/api-baselines/net11.0-publicapi (the net11 net-tizen
/// PublicAPI.Shipped/Unshipped.txt inputs collected from the pinned dotnet/maui commit). The
/// SHA-256 comparisons are the key "stale generated artifact" detector: if a PublicAPI.txt file is
/// hand-edited without re-running eng/scripts/fetch-net11-publicapi-inputs.ps1 (which regenerates
/// manifest.json's recorded hashes), this test fails offline, no network required.
/// </summary>
public class Net11PublicApiInputsTests
{
    private const string OutDir = "eng/api-baselines/net11.0-publicapi";

    private static readonly string[] ExpectedProjects =
    [
        "BlazorWebView", "Controls.Maps", "Controls.Core", "Controls.Xaml",
        "Core.Maps", "Core", "Essentials", "Graphics.Skia", "Graphics",
    ];

    [Fact]
    public void Manifest_pins_the_net11_baseline_commit()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        using var baselines = TestPaths.LoadJson("eng/baselines.json");

        var expectedCommit = baselines.RootElement.GetProperty("source").GetProperty("sourceBaseline").GetProperty("commit").GetString();
        Assert.Equal(expectedCommit, manifest.RootElement.GetProperty("sourceRef").GetString());
    }

    [Fact]
    public void All_expected_projects_are_present()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");
        var actualProjects = manifest.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(p => p.GetProperty("project").GetString())
            .ToHashSet();

        foreach (var expected in ExpectedProjects)
        {
            Assert.Contains(expected, actualProjects);
        }
        Assert.Equal(ExpectedProjects.Length, actualProjects.Count);
    }

    [Fact]
    public void Committed_files_match_their_recorded_hashes()
    {
        using var manifest = TestPaths.LoadJson($"{OutDir}/manifest.json");

        foreach (var project in manifest.RootElement.GetProperty("projects").EnumerateArray())
        {
            var name = project.GetProperty("project").GetString()!;
            var shippedPath = TestPaths.Path_(OutDir, name, "PublicAPI.Shipped.txt");
            var unshippedPath = TestPaths.Path_(OutDir, name, "PublicAPI.Unshipped.txt");

            Assert.True(File.Exists(shippedPath), $"Missing {shippedPath}");
            Assert.True(File.Exists(unshippedPath), $"Missing {unshippedPath}");

            Assert.Equal(project.GetProperty("shippedSha256").GetString(), TestPaths.Sha256Hex(shippedPath));
            Assert.Equal(project.GetProperty("unshippedSha256").GetString(), TestPaths.Sha256Hex(unshippedPath));
        }
    }
}
