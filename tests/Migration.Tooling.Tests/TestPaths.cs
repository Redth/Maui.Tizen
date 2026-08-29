using System.Text.Json;

namespace Migration.Tooling.Tests;

/// <summary>
/// Locates repo-relative paths from the test's output directory. Walks upward looking for
/// eng/baselines.json (a stable, always-present marker) rather than hardcoding a relative depth,
/// so the tests keep working if the test project itself is moved.
/// </summary>
internal static class TestPaths
{
    public static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "eng", "baselines.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (eng/baselines.json) above '{AppContext.BaseDirectory}'.");
    }

    public static string Path_(params string[] segments) =>
        Path.Combine([RepoRoot, .. segments]);

    public static JsonDocument LoadJson(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path_(relativePath)));

    public static string Sha256Hex(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
