using System.Text.Json;

namespace Migration.Tooling.Tests;

internal sealed record ImportedPublicApiFile(string TargetPath, string Sha256);

internal static class ImportedPublicApiBaselineVerifier
{
    private const string BaselineDirectory = "eng/api-baselines/net11.0-publicapi";
    private const int MaximumLinkDepth = 40;

    private static readonly (string FileName, string HashProperty)[] BaselineFiles =
    [
        ("PublicAPI.Shipped.txt", "shippedSha256"),
        ("PublicAPI.Unshipped.txt", "unshippedSha256"),
    ];

    private static readonly EnumerationOptions DirectoryEnumerationOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private static StringComparer FileSystemPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static IReadOnlyList<ImportedPublicApiFile> LoadTrustedInventory(string repoRoot)
    {
        var canonicalRepoRoot = ResolvePhysicalExistingPath(repoRoot);
        using var baselines = LoadJson(canonicalRepoRoot, "eng/baselines.json");
        using var manifest = LoadJson(canonicalRepoRoot, $"{BaselineDirectory}/manifest.json");
        using var disposition = LoadJson(canonicalRepoRoot, "eng/manifests/source-disposition.json");

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
                var trustedArtifactPath = $"{BaselineDirectory}/{projectName}/{fileName}";

                Require(IsSha256(sha256), $"Invalid SHA-256 for {sourcePath}: {sha256}");
                var trustedArtifact = ResolveContainedRegularFile(
                    canonicalRepoRoot,
                    BaselineDirectory,
                    trustedArtifactPath);
                Require(
                    TestPaths.Sha256Hex(trustedArtifact) == sha256,
                    $"Trusted PublicAPI artifact does not match its pinned hash: {trustedArtifactPath}");
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
        string canonicalRepoRoot;
        try
        {
            canonicalRepoRoot = ResolvePhysicalExistingPath(repoRoot);
        }
        catch (InvalidDataException exception)
        {
            return [$"Repository root could not be resolved safely: {exception.Message}"];
        }

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

        var scan = ScanSourceTree(canonicalRepoRoot);
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
            var reparsePrefix = reparsePoint.Path.TrimEnd('/') + "/";
            var affectedExpectedPath = expectedPaths.FirstOrDefault(
                expectedPath => expectedPath.Equals(reparsePoint.Path, StringComparison.OrdinalIgnoreCase)
                    || expectedPath.StartsWith(reparsePrefix, StringComparison.OrdinalIgnoreCase));

            if (reparsePoint.ResolutionError is not null)
            {
                errors.Add(
                    $"Source tree reparse point could not be resolved: {reparsePoint.Path} "
                    + $"({reparsePoint.ResolutionError})");
                continue;
            }

            if (reparsePoint.EscapesTrustedRoot)
            {
                errors.Add(
                    $"Source tree symbolic link escapes the trusted src root: "
                    + $"{reparsePoint.Path} -> {reparsePoint.ResolvedPath}");
                continue;
            }

            errors.Add(affectedExpectedPath is null
                ? $"Source tree symbolic links are not allowed because they can alias imported baselines: {reparsePoint.Path}"
                : $"Imported baseline path contains a symbolic link: {reparsePoint.Path} (affects {affectedExpectedPath})");
        }

        foreach (var expected in expectedByPath.Values.OrderBy(file => file.TargetPath, StringComparer.Ordinal))
        {
            string? resolvedFile = null;
            try
            {
                resolvedFile = ResolveContainedRegularFile(
                    canonicalRepoRoot,
                    GetParentPath(expected.TargetPath),
                    expected.TargetPath);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(exception.Message);
            }

            if (!actualSet.Contains(expected.TargetPath))
            {
                errors.Add($"Missing imported baseline: {expected.TargetPath}");
                continue;
            }

            if (resolvedFile is null)
            {
                continue;
            }

            var actualHash = TestPaths.Sha256Hex(resolvedFile);
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
        var reparsePoints = new List<SourceTreeReparsePoint>();
        var nonPortablePaths = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        var rootEntries = new DirectoryInfo(repoRoot)
            .EnumerateFileSystemInfos("*", DirectoryEnumerationOptions)
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
                reparsePoints.Add(InspectSourceTreeReparsePoint(
                    repoRoot,
                    Path.Combine(repoRoot, "src"),
                    sourceRoot,
                    sourceRoot.Name));
            }
            else if (sourceRoot is DirectoryInfo sourceDirectory)
            {
                pending.Push(sourceDirectory);
            }
        }

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", DirectoryEnumerationOptions)
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
                    reparsePoints.Add(InspectSourceTreeReparsePoint(
                        repoRoot,
                        Path.Combine(repoRoot, "src"),
                        entry,
                        relativePath));
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
        reparsePoints.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        nonPortablePaths.Sort(StringComparer.Ordinal);
        return new SourceTreeScan(importedFiles, reparsePoints, nonPortablePaths);
    }

    private static SourceTreeReparsePoint InspectSourceTreeReparsePoint(
        string repoRoot,
        string trustedSourceRoot,
        FileSystemInfo reparsePoint,
        string relativePath)
    {
        try
        {
            var resolvedPath = ResolveReparsePoint(reparsePoint, relativePath);
            var displayPath = IsPathContained(repoRoot, resolvedPath)
                ? NormalizePath(Path.GetRelativePath(repoRoot, resolvedPath))
                : resolvedPath;

            return new SourceTreeReparsePoint(
                relativePath,
                displayPath,
                EscapesTrustedRoot: !IsPathContained(trustedSourceRoot, resolvedPath),
                ResolutionError: null);
        }
        catch (InvalidDataException exception)
        {
            return new SourceTreeReparsePoint(
                relativePath,
                ResolvedPath: null,
                EscapesTrustedRoot: false,
                ResolutionError: exception.Message);
        }
    }

    private static string ResolveContainedRegularFile(
        string canonicalRepoRoot,
        string trustedRootRelativePath,
        string fileRelativePath)
    {
        ValidateRepositoryRelativePath(trustedRootRelativePath, "trusted root");
        ValidateRepositoryRelativePath(fileRelativePath, "file");

        var trustedRoot = Path.GetFullPath(
            Path.Combine(canonicalRepoRoot, ToPlatformPath(trustedRootRelativePath)));
        var intendedFile = Path.GetFullPath(
            Path.Combine(canonicalRepoRoot, ToPlatformPath(fileRelativePath)));

        Require(
            IsPathContained(trustedRoot, intendedFile),
            $"Repository file is outside its trusted root: {fileRelativePath} (root {trustedRootRelativePath})");

        var currentDirectory = new DirectoryInfo(canonicalRepoRoot);
        var segments = fileRelativePath.Split('/');

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var entry = FindExactEntry(canonicalRepoRoot, currentDirectory, segment, fileRelativePath);
            var entryRelativePath = NormalizePath(Path.GetRelativePath(canonicalRepoRoot, entry.FullName));

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var resolvedPath = ResolveReparsePoint(entry, entryRelativePath);
                Require(
                    IsPathContained(trustedRoot, resolvedPath),
                    $"Repository path component resolves outside its trusted root: "
                        + $"{entryRelativePath} -> {resolvedPath} (root {trustedRootRelativePath})");
                throw new InvalidDataException(
                    $"Repository path component is a symbolic link: {entryRelativePath} -> {resolvedPath}");
            }

            var isFinalSegment = index == segments.Length - 1;
            if (!isFinalSegment)
            {
                Require(
                    entry is DirectoryInfo,
                    $"Repository path component is not a directory: {entryRelativePath}");
                currentDirectory = (DirectoryInfo)entry;
                continue;
            }

            Require(entry is FileInfo, $"Expected a regular repository file: {fileRelativePath}");
            var resolvedFile = ResolvePhysicalExistingPath(entry.FullName);
            Require(
                IsPathContained(trustedRoot, resolvedFile),
                $"Repository file resolves outside its trusted root: "
                    + $"{fileRelativePath} -> {resolvedFile} (root {trustedRootRelativePath})");
            return resolvedFile;
        }

        throw new InvalidDataException($"Repository file has no path segments: {fileRelativePath}");
    }

    private static FileSystemInfo FindExactEntry(
        string canonicalRepoRoot,
        DirectoryInfo directory,
        string expectedName,
        string expectedPath)
    {
        var matches = directory
            .EnumerateFileSystemInfos("*", DirectoryEnumerationOptions)
            .Where(entry => entry.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        var exactMatches = matches
            .Where(entry => entry.Name.Equals(expectedName, StringComparison.Ordinal))
            .ToList();

        if (exactMatches.Count == 0)
        {
            if (matches.Count > 0)
            {
                throw new InvalidDataException(
                    $"Repository path/case drift: expected '{expectedName}' in "
                    + $"'{NormalizePath(Path.GetRelativePath(canonicalRepoRoot, directory.FullName))}', "
                    + $"found {string.Join(", ", matches.Select(entry => $"'{entry.Name}'"))} while resolving {expectedPath}");
            }

            throw new InvalidDataException($"Missing repository file or directory while resolving {expectedPath}: {expectedName}");
        }

        Require(
            matches.Count == 1,
            $"Repository path component collides by case while resolving {expectedPath}: "
                + string.Join(", ", matches.Select(entry => entry.Name)));
        return exactMatches[0];
    }

    private static string ResolvePhysicalExistingPath(string path)
    {
        var visitedLinks = new HashSet<string>(FileSystemPathComparer);
        return ResolvePhysicalExistingPath(path, visitedLinks, linkDepth: 0);
    }

    private static string ResolvePhysicalExistingPath(
        string path,
        HashSet<string> visitedLinks,
        int linkDepth)
    {
        Require(linkDepth <= MaximumLinkDepth, $"Too many symbolic links while resolving {path}");

        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath);
        Require(!string.IsNullOrEmpty(pathRoot), $"Path has no filesystem root: {path}");

        var currentPath = pathRoot!;
        var remainingPath = fullPath[pathRoot!.Length..];
        var segments = remainingPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var candidatePath = Path.Combine(currentPath, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(candidatePath);
            }
            catch (FileNotFoundException exception)
            {
                throw new InvalidDataException($"Path component does not exist: {candidatePath}", exception);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw new InvalidDataException($"Path component does not exist: {candidatePath}", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidDataException($"Path component cannot be inspected: {candidatePath}", exception);
            }
            catch (IOException exception)
            {
                throw new InvalidDataException($"Path component cannot be inspected: {candidatePath}", exception);
            }

            FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(candidatePath)
                : new FileInfo(candidatePath);

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                currentPath = Path.GetFullPath(entry.FullName);
                continue;
            }

            var linkPath = Path.GetFullPath(entry.FullName);
            Require(visitedLinks.Add(linkPath), $"Symbolic link cycle detected at {linkPath}");
            var resolvedTarget = ResolveLinkTarget(entry, linkPath);
            currentPath = ResolvePhysicalExistingPath(
                resolvedTarget.FullName,
                visitedLinks,
                linkDepth + 1);
        }

        return Path.TrimEndingDirectorySeparator(currentPath);
    }

    private static string ResolveReparsePoint(FileSystemInfo reparsePoint, string displayPath)
    {
        var resolvedTarget = ResolveLinkTarget(reparsePoint, displayPath);
        try
        {
            return ResolvePhysicalExistingPath(resolvedTarget.FullName);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Reparse point target could not be resolved: {displayPath} ({exception.Message})",
                exception);
        }
    }

    private static FileSystemInfo ResolveLinkTarget(FileSystemInfo reparsePoint, string displayPath)
    {
        try
        {
            return reparsePoint.ResolveLinkTarget(returnFinalTarget: true)
                ?? throw new InvalidDataException($"Unresolved reparse point: {displayPath}");
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException($"Unresolved reparse point: {displayPath}", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new InvalidDataException($"Unresolved reparse point: {displayPath}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidDataException($"Reparse point cannot be resolved: {displayPath}", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Reparse point cannot be resolved: {displayPath}", exception);
        }
    }

    private static bool IsPathContained(string trustedRoot, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidate.Equals(root, comparison)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static void ValidateRepositoryRelativePath(string path, string role)
    {
        Require(!Path.IsPathRooted(path), $"Repository {role} path must be relative: {path}");
        Require(!path.Contains('\\'), $"Repository {role} path must use '/' separators: {path}");

        var segments = path.Split('/');
        Require(
            segments.Length > 0
                && segments.All(segment => segment.Length > 0 && segment is not "." and not ".."),
            $"Repository {role} path is not canonical: {path}");
    }

    private static string GetParentPath(string path)
    {
        var separator = path.LastIndexOf('/');
        Require(separator > 0, $"Repository file path has no parent directory: {path}");
        return path[..separator];
    }

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

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
        var path = ResolveContainedRegularFile(
            repoRoot,
            GetParentPath(relativePath),
            relativePath);
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
        List<SourceTreeReparsePoint> ReparsePoints,
        List<string> NonPortablePaths);
    private sealed record SourceTreeReparsePoint(
        string Path,
        string? ResolvedPath,
        bool EscapesTrustedRoot,
        string? ResolutionError);
    private sealed record TrustedSourceFile(string SourcePath, string FileName, string Sha256);
}
