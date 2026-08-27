using System.Security.Cryptography;
using System.Text;

namespace Migration.Tooling.Tests;

public class ImportedPublicApiBaselineTests
{
    private const string ImportedPath = "src/Example/PublicAPI/net-tizen/PublicAPI.Shipped.txt";
    private static readonly byte[] ImportedContent = Encoding.UTF8.GetBytes("#nullable enable\nExample.Api\n");

    [Fact]
    public void Repository_imported_baselines_match_the_pinned_source_snapshot()
    {
        var expected = ImportedPublicApiBaselineVerifier.LoadTrustedInventory(TestPaths.RepoRoot);
        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(TestPaths.RepoRoot, expected);

        Assert.Equal(18, expected.Count);
        Assert.True(
            errors.Count == 0,
            "Imported PublicAPI provenance check failed:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Overwrite_is_rejected()
    {
        using var fixture = CreateValidFixture();
        File.AppendAllText(fixture.Path_(ImportedPath), "Example.Overwritten\n");

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(errors, error => error.StartsWith("Imported baseline content changed:", StringComparison.Ordinal));
    }

    [Fact]
    public void Deletion_is_rejected()
    {
        using var fixture = CreateValidFixture();
        File.Delete(fixture.Path_(ImportedPath));

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains($"Missing imported baseline: {ImportedPath}", errors);
    }

    [Fact]
    public void Extra_file_is_rejected()
    {
        using var fixture = CreateValidFixture();
        var extraPath = "src/Example/PublicAPI/net-tizen/Generated.txt";
        fixture.Write(extraPath, "generated");

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains($"Unexpected imported baseline file: {extraPath}", errors);
    }

    [Fact]
    public void Path_or_case_drift_is_rejected()
    {
        using var fixture = CreateValidFixture();
        var canonicalDirectory = fixture.Path_("src/Example/PublicAPI/net-tizen");
        var temporaryDirectory = fixture.Path_("src/Example/PublicAPI/net-tizen.rename");
        var driftedDirectory = fixture.Path_("src/Example/PublicAPI/NET-TIZEN");
        Directory.Move(canonicalDirectory, temporaryDirectory);
        Directory.Move(temporaryDirectory, driftedDirectory);

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains("Imported baseline path/case drift:", StringComparison.Ordinal));
    }

    [Fact]
    public void Top_level_src_case_drift_is_rejected()
    {
        using var fixture = CreateValidFixture();
        var canonicalDirectory = fixture.Path_("src");
        var temporaryDirectory = fixture.Path_("src.rename");
        var driftedDirectory = fixture.Path_("SRC");
        Directory.Move(canonicalDirectory, temporaryDirectory);
        Directory.Move(temporaryDirectory, driftedDirectory);

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains("Imported baseline path/case drift:", StringComparison.Ordinal)
                && error.Contains("SRC/", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_slice_baselines_are_outside_the_imported_provenance_guard()
    {
        using var fixture = CreateValidFixture();
        fixture.Write("src/Example/PublicAPI/slice/PublicAPI.Shipped.txt", "#nullable enable\n");
        fixture.Write("src/Example/PublicAPI/slice/PublicAPI.Unshipped.txt", "Example.GeneratedSlice\n");

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Empty(errors);
    }

    [Fact]
    public void Directory_symlink_cannot_substitute_for_imported_files()
    {
        using var fixture = CreateValidFixture();
        var canonicalDirectory = fixture.Path_("src/Example/PublicAPI/net-tizen");
        var backingDirectory = fixture.Path_("backing/net-tizen");
        Directory.CreateDirectory(Path.GetDirectoryName(backingDirectory)!);
        Directory.Move(canonicalDirectory, backingDirectory);

        try
        {
            Directory.CreateSymbolicLink(canonicalDirectory, backingDirectory);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains("Repository path component resolves outside its trusted root:", StringComparison.Ordinal)
                && error.Contains("src/Example/PublicAPI/net-tizen", StringComparison.Ordinal));
    }

    [Fact]
    public void Unresolved_imported_directory_symlink_fails_closed()
    {
        using var fixture = CreateValidFixture();
        var canonicalDirectory = fixture.Path_("src/Example/PublicAPI/net-tizen");
        var missingDirectory = fixture.Path_("missing/net-tizen");
        Directory.Delete(canonicalDirectory, recursive: true);

        try
        {
            Directory.CreateSymbolicLink(canonicalDirectory, missingDirectory);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains("could not be resolved:", StringComparison.Ordinal)
                && error.Contains("src/Example/PublicAPI/net-tizen", StringComparison.Ordinal));
    }

    [Fact]
    public void Directory_symlink_cannot_alias_extra_imported_files()
    {
        using var fixture = CreateValidFixture();
        var aliasPath = fixture.Path_("src/Alias");
        var importedProjectPath = fixture.Path_("src/Example");

        try
        {
            Directory.CreateSymbolicLink(aliasPath, importedProjectPath);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains(
                "Source tree symbolic links are not allowed because they can alias imported baselines:",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Dangling_symlink_under_src_is_rejected()
    {
        using var fixture = CreateValidFixture();
        var aliasPath = fixture.Path_("src/DanglingAlias");
        var missingTarget = fixture.Path_("src/EXAMPLE");

        try
        {
            File.CreateSymbolicLink(aliasPath, missingTarget);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            errors,
            error => error.Contains(
                "Source tree symbolic links are not allowed because they can alias imported baselines:",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Posix_backslash_path_drift_is_rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = CreateValidFixture();
        var driftedPath = "src/Example/PublicAPI/net-tizen\\PublicAPI.Shipped.txt";
        fixture.Write(driftedPath, ImportedContent);

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            $"Repository path uses a literal backslash that can alias the src tree on Windows: {driftedPath}",
            errors);
    }

    [Fact]
    public void Posix_root_backslash_alias_is_rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = CreateValidFixture();
        var aliasPath = "src\\Alias\\PublicAPI\\net-tizen\\Generated.txt";
        fixture.Write(aliasPath, "generated");

        var errors = ImportedPublicApiBaselineVerifier.VerifyTree(fixture.Root, fixture.Expected);

        Assert.Contains(
            $"Repository path uses a literal backslash that can alias the src tree on Windows: {aliasPath}",
            errors);
    }

    private static BaselineFixture CreateValidFixture()
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(ImportedContent)).ToLowerInvariant();
        var expected = new[] { new ImportedPublicApiFile(ImportedPath, sha256) };
        var fixture = new BaselineFixture(expected);
        fixture.Write(ImportedPath, ImportedContent);
        return fixture;
    }

    private sealed class BaselineFixture : IDisposable
    {
        public BaselineFixture(IReadOnlyCollection<ImportedPublicApiFile> expected)
        {
            Root = Path.Combine(Path.GetTempPath(), $"maui-tizen-publicapi-{Guid.NewGuid():N}");
            Expected = expected;
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public IReadOnlyCollection<ImportedPublicApiFile> Expected { get; }

        public string Path_(string relativePath) =>
            Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relativePath, string content) =>
            Write(relativePath, Encoding.UTF8.GetBytes(content));

        public void Write(string relativePath, byte[] content)
        {
            var path = Path_(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
