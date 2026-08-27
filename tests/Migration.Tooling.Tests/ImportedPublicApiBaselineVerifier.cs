using System.Text.Json;

namespace Migration.Tooling.Tests;

internal sealed record ImportedPublicApiFile(string TargetPath, string Sha256);

internal static class ImportedPublicApiBaselineVerifier
{
    private const string BaselineDirectory = "eng/api-baselines/net11.0-publicapi";

    private static readonly (string FileName, string HashProperty)[] BaselineFiles =
    [
        ("PublicAPI.Shipped.txt", "shippedSha256"),
        ("PublicAPI.Unshipped.txt", "unshippedSha256"),
    ];

    public static IReadOnlyList<ImportedPublicApiFile> LoadTrustedInventory(string repoRoot)
    {
        using var baselines = LoadJson(repoRoot, "eng/baselines.json");
        using var manifest = LoadJson(repoRoot, $"{BaselineDirectory}/manifest.json");
        using var disposition = LoadJson(repoRoot, "eng/manifests/source-disposition.json");

        var baselineSource = baselines.RootElement.GetProperty("source");
        var expectedRepository = baselineSource.GetProperty("repository").GetString()!;
        var expectedRef = baselineSource.GetProperty("sourceBaseline").GetProperty("commit").GetString()!;

        Require(
            manifest.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
            "The net11 PublicAPI manifest schema version is not supported.");
        Require(
            manifest.RootElement.GetProperty("repository").GetString() == expectedRepository,
            "The net11 PublicAPI manifest repository does not match eng/baselines.json.");
        Require(
            manifest.RootElement.GetProperty("sourceRef").GetString() == expectedRef,
            "The net11 PublicAPI manifest sourceRef does not match the pinned source baseline.");
        Require(
            disposition.RootElement.GetProperty("generatedFrom").GetProperty("sourceBaseline").GetString() == expectedRef,
            "The source-disposition manifest was not generated from the pinned source baseline.");

        var trustedSources = new Dictionary<string, TrustedSourceFile>(StringComparer.Ordinal);
        foreach (var project in manifest.RootElement.GetProperty("projects").EnumerateArray())
        {
            var projectName = project.GetProperty("project").GetString()!;
            var sourceDirectory = project.GetProperty("sourcePath").GetString()!;

            Require(
                projectName.Length > 0 && projectName.IndexOfAny(['/', '\\']) < 0,
                $"The trusted PublicAPI project name is not a single path segment: {projectName}");
            Require(
                IsCanonicalImportedDirectory(sourceDirectory),
                $"The trusted source directory is not canonical: {sourceDirectory}");

            foreach (var (fileName, hashProperty) in BaselineFiles)
            {
                var sourcePath = $"{sourceDirectory}/{fileName}";
                var sha256 = project.GetProperty(hashProperty).GetString()!;
                var trustedArtifact = Path.Combine(repoRoot, BaselineDirectory, projectName, fileName);

                Require(IsSha256(sha256), $"Invalid SHA-256 for {sourcePath}: {sha256}");
                Require(File.Exists(trustedArtifact), $"Missing trusted PublicAPI artifact: {trustedArtifact}");
                Require(
                    !TryFindReparsePoint(repoRoot, trustedArtifact, out var trustedLink),
                    $"Trusted PublicAPI artifact path contains a symbolic link: {trustedLink}");
                Require(
                    TestPaths.Sha256Hex(trustedArtifact) == sha256,
                    $"Trusted PublicAPI artifact does not match its pinned hash: {trustedArtifact}");
                Require(
                    trustedSources.TryAdd(sourcePath, new TrustedSourceFile(sourcePath, fileName, sha256)),
                    $"Duplicate trusted PublicAPI source path: {sourcePath}");
            }
        }

        var importedEntries = disposition.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Where(entry => IsImportedPublicApiPath(entry.GetProperty("path").GetString()!))
            .ToList();

        var unexpectedSources = importedEntries
            .Select(entry => entry.GetProperty("path").GetString()!)
            .Where(path => !trustedSources.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        Require(
            unexpectedSources.Count == 0,
            "The source-disposition manifest contains imported PublicAPI files missing from the trusted hash manifest: "
                + string.Join(", ", unexpectedSources));

        var result = new List<ImportedPublicApiFile>(trustedSources.Count);
        foreach (var trusted in trustedSources.Values.OrderBy(file => file.SourcePath, StringComparer.Ordinal))
        {
            var matches = importedEntries
                .Where(entry => entry.GetProperty("path").GetString() == trusted.SourcePath)
                .ToList();
            Require(
                matches.Count == 1,
                $"Expected exactly one source-disposition entry for {trusted.SourcePath}, found {matches.Count}.");

            var entry = matches[0];
            var sourceRef = entry.GetProperty("sourceRef").GetString();
            Require(
                sourceRef is "sourceBaseline" or "both",
                $"{trusted.SourcePath} is not attributed to the pinned source baseline.");
            Require(
                entry.GetProperty("disposition").GetString() == "move",
                $"{trusted.SourcePath} must remain a byte-preserving move.");

            var targetPath = entry.GetProperty("targetPath").GetString()!;
            Require(
                IsCanonicalImportedTarget(targetPath, trusted.FileName),
                $"Imported PublicAPI target path is not canonical: {targetPath}");

            result.Add(new ImportedPublicApiFile(targetPath, trusted.Sha256));
        }

        var duplicateTargets = result
            .GroupBy(file => file.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" / ", group.Select(file => file.TargetPath)))
            .ToList();
        Require(
            duplicateTargets.Count == 0,
            "Imported PublicAPI target paths collide by case: " + string.Join(", ", duplicateTargets));

        return result;
    }

    public static IReadOnlyList<string> VerifyTree(
        string repoRoot,
        IReadOnlyCollection<ImportedPublicApiFile> expectedFiles)
    {
        var errors = new SortedSet<string>(StringComparer.Ordinal);
        var expectedByPath = new Dictionary<string, ImportedPublicApiFile>(StringComparer.Ordinal);

        foreach (var expected in expectedFiles.OrderBy(file => file.TargetPath, StringComparer.Ordinal))
        {
            if (!expectedByPath.TryAdd(expected.TargetPath, expected))
            {
                errors.Add($"Duplicate trusted imported baseline path: {expected.TargetPath}");
            }
        }

        var caseCollisions = expectedFiles
            .GroupBy(file => file.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(file => file.TargetPath).Distinct(StringComparer.Ordinal).Count() > 1);
        foreach (var collision in caseCollisions)
        {
            errors.Add(
                "Trusted imported baseline paths collide by case: "
                + string.Join(", ", collision.Select(file => file.TargetPath).OrderBy(path => path, StringComparer.Ordinal)));
        }

        var scan = ScanSourceTree(repoRoot);
        var actualPaths = scan.ImportedFiles;

        foreach (var path in scan.NonPortablePaths)
        {
            errors.Add(
                $"Repository path uses a literal backslash that can alias the src tree on Windows: {path}");
        }

        var actualSet = actualPaths.ToHashSet(StringComparer.Ordinal);
        var expectedPaths = expectedByPath.Keys.OrderBy(path => path, StringComparer.Ordinal).ToList();

        foreach (var duplicate in actualPaths
            .GroupBy(path => path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            errors.Add($"Multiple imported baseline files normalize to the same path: {duplicate.Key}");
        }

        foreach (var reparsePoint in scan.ReparsePoints)
        {
            var reparsePrefix = reparsePoint.TrimEnd('/') + "/";
            var affectedExpectedPath = expectedPaths.FirstOrDefault(
                expectedPath => expectedPath.Equals(reparsePoint, StringComparison.OrdinalIgnoreCase)
                    || expectedPath.StartsWith(reparsePrefix, StringComparison.OrdinalIgnoreCase));

            if (affectedExpectedPath is not null)
            {
                errors.Add($"Imported baseline path contains a symbolic link: {reparsePoint} (affects {affectedExpectedPath})");
                continue;
            }

            errors.Add(
                $"Source tree symbolic links are not allowed because they can alias imported baselines: {reparsePoint}");
        }

        foreach (var expected in expectedByPath.Values.OrderBy(file => file.TargetPath, StringComparer.Ordinal))
        {
            if (!actualSet.Contains(expected.TargetPath))
            {
                errors.Add($"Missing imported baseline: {expected.TargetPath}");
                continue;
            }

            var actualPath = Path.Combine(repoRoot, expected.TargetPath.Replace('/', Path.DirectorySeparatorChar));
            if ((File.GetAttributes(actualPath) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add($"Imported baseline must be a regular file, not a symbolic link: {expected.TargetPath}");
                continue;
            }

            var actualHash = TestPaths.Sha256Hex(actualPath);
            if (actualHash != expected.Sha256)
            {
                errors.Add(
                    $"Imported baseline content changed: {expected.TargetPath} "
                    + $"(expected SHA-256 {expected.Sha256}, actual {actualHash})");
            }
        }

        foreach (var actualPath in actualPaths.Where(path => !expectedByPath.ContainsKey(path)))
        {
            var caseMatch = expectedPaths.FirstOrDefault(
                expectedPath => string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase));

            errors.Add(caseMatch is null
                ? $"Unexpected imported baseline file: {actualPath}"
                : $"Imported baseline path/case drift: expected {caseMatch}, found {actualPath}");
        }

        return errors.ToList();
    }

    private static SourceTreeScan ScanSourceTree(string repoRoot)
    {
        var importedFiles = new List<string>();
        var reparsePoints = new List<string>();
        var nonPortablePaths = new List<string>();
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var pending = new Stack<DirectoryInfo>();
        var rootEntries = new DirectoryInfo(repoRoot)
            .EnumerateFileSystemInfos("*", options)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        if (Path.DirectorySeparatorChar != '\\')
        {
            nonPortablePaths.AddRange(rootEntries
                .Where(entry => IsPortableSourcePathAlias(entry.Name))
                .Select(entry => entry.Name));
        }

        var sourceRoots = rootEntries
            .Where(entry => entry.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var sourceRoot in sourceRoots)
        {
            if ((sourceRoot.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                reparsePoints.Add(sourceRoot.Name);
            }
            else if (sourceRoot is DirectoryInfo sourceDirectory)
            {
                pending.Push(sourceDirectory);
            }
        }

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", options)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                var rawRelativePath = Path.GetRelativePath(repoRoot, entry.FullName);
                var relativePath = NormalizePath(rawRelativePath);
                if (Path.DirectorySeparatorChar != '\\' && rawRelativePath.Contains('\\'))
                {
                    nonPortablePaths.Add(relativePath);
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reparsePoints.Add(relativePath);
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                }
                else if (IsImportedPublicApiPath(relativePath))
                {
                    importedFiles.Add(relativePath);
                }
            }
        }

        importedFiles.Sort(StringComparer.Ordinal);
        reparsePoints.Sort(StringComparer.Ordinal);
        nonPortablePaths.Sort(StringComparer.Ordinal);
        return new SourceTreeScan(importedFiles, reparsePoints, nonPortablePaths);
    }

    private static bool TryFindReparsePoint(string root, string path, out string? reparsePoint)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var currentPath = root;

        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            currentPath = Path.Combine(currentPath, segment);
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                reparsePoint = NormalizePath(Path.GetRelativePath(root, currentPath));
                return true;
            }
        }

        reparsePoint = null;
        return false;
    }

    private static bool IsCanonicalImportedDirectory(string path)
    {
        if (!path.StartsWith("src/", StringComparison.Ordinal)
            || path.Contains('\\')
            || path.EndsWith('/'))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.Length >= 3
            && segments[^2] == "PublicAPI"
            && segments[^1] == "net-tizen"
            && segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsCanonicalImportedTarget(string path, string fileName)
    {
        if (!path.StartsWith("src/", StringComparison.Ordinal) || path.Contains('\\'))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.Length >= 5
            && segments[^3] == "PublicAPI"
            && segments[^2] == "net-tizen"
            && segments[^1] == fileName
            && segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsImportedPublicApiPath(string path)
    {
        var segments = NormalizePath(path).Split('/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("PublicAPI", StringComparison.OrdinalIgnoreCase)
                && segments[i + 1].Equals("net-tizen", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPortableSourcePathAlias(string path)
    {
        if (!path.Contains('\\'))
        {
            return false;
        }

        var firstSegment = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment?.Equals("src", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizePath(string path) =>
        Path.DirectorySeparatorChar == '\\' ? path.Replace('\\', '/') : path;

    private static JsonDocument LoadJson(string repoRoot, string relativePath)
    {
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Require(File.Exists(path), $"Missing trusted provenance file: {relativePath}");
        Require(
            !TryFindReparsePoint(repoRoot, path, out var reparsePoint),
            $"Trusted provenance path contains a symbolic link: {reparsePoint}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed record SourceTreeScan(
        List<string> ImportedFiles,
        List<string> ReparsePoints,
        List<string> NonPortablePaths);
    private sealed record TrustedSourceFile(string SourcePath, string FileName, string Sha256);
}
