using System.Xml.Linq;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Works out which <c>.cs</c> files a project actually compiles.
/// </summary>
/// <remarks>
/// <para>
/// This distinction is the whole point of the API15 source guards. The <c>src/Maui.Tizen.*</c>
/// sources were imported verbatim from dotnet/maui and are currently listed as <c>&lt;None&gt;</c>
/// with <c>EnableDefaultCompileItems=false</c>, so nothing compiles them. Scanning those files for
/// banned APIs would produce hundreds of failures describing upstream history rather than anything
/// this repository has adopted, and the only way to get green would be to disable the guard.
/// </para>
/// <para>
/// So the guard follows compilation: a file is in scope exactly when a project compiles it, and a
/// project comes into scope automatically the moment it opts in.
/// </para>
/// <para>
/// Compile sets are computed by reading project XML rather than by evaluating MSBuild, because the
/// Tizen-targeted projects cannot be evaluated at all without the Samsung workload - the target
/// platform identifier is unrecognised, so evaluation fails long before item lists exist.
/// </para>
/// </remarks>
public static class CompiledSourceInventory
{
    static readonly string[] ExcludedDirectorySegments = ["/bin/", "/obj/", "/artifacts/"];

    /// <summary>Every project under <c>src/</c>, including diagnostics.</summary>
    public static IReadOnlyList<string> EnumerateProductProjects()
    {
        if (!Directory.Exists(RepoLayout.Src))
            return [];

        return [.. Directory
            .EnumerateFiles(RepoLayout.Src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsExcluded(p))
            .OrderBy(p => p, StringComparer.Ordinal)];
    }

    /// <summary>Computes the compiled source set for a single project.</summary>
    public static CompiledSourceSet ForProject(string projectPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var document = XDocument.Load(projectPath);

        var defaultItemsEnabled = ResolveDefaultCompileItems(projectPath, document);

        var includes = new List<string>();
        var removes = new List<string>();

        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "Compile"))
        {
            if (item.Attribute("Include")?.Value is { Length: > 0 } include)
                includes.AddRange(SplitItemList(include));

            if (item.Attribute("Update")?.Value is { Length: > 0 } update)
                includes.AddRange(SplitItemList(update));

            if (item.Attribute("Remove")?.Value is { Length: > 0 } remove)
                removes.AddRange(SplitItemList(remove));
        }

        var files = new SortedSet<string>(StringComparer.Ordinal);

        if (defaultItemsEnabled)
        {
            foreach (var file in EnumerateSourceFiles(projectDirectory))
                files.Add(file);
        }

        foreach (var include in includes)
        {
            foreach (var file in ExpandGlob(projectDirectory, include))
                files.Add(file);
        }

        foreach (var remove in removes)
        {
            foreach (var file in ExpandGlob(projectDirectory, remove))
                files.Remove(file);
        }

        return new CompiledSourceSet(projectPath, defaultItemsEnabled, [.. files]);
    }

    /// <summary>Compiled source sets for every product project.</summary>
    public static IReadOnlyList<CompiledSourceSet> ForAllProductProjects() =>
        [.. EnumerateProductProjects().Select(ForProject)];

    /// <summary>
    /// Resolves the effective <c>EnableDefaultCompileItems</c>.
    /// </summary>
    /// <remarks>
    /// The project's own value wins. Otherwise any props file it imports is consulted, which is how
    /// <c>eng/targets/TizenPackage.props</c> turns default items off for the imported packages.
    /// Absent both, the SDK default of <see langword="true"/> applies.
    /// </remarks>
    static bool ResolveDefaultCompileItems(string projectPath, XDocument document)
    {
        var own = ReadProperty(document, "EnableDefaultCompileItems");
        if (own is not null)
            return IsTrue(own);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

        foreach (var import in document.Descendants().Where(e => e.Name.LocalName == "Import"))
        {
            var raw = import.Attribute("Project")?.Value;
            if (string.IsNullOrEmpty(raw))
                continue;

            // Only $(MSBuildThisFileDirectory)-relative imports are resolvable without evaluation.
            var relative = raw.Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal);
            var importPath = Path.GetFullPath(Path.Combine(projectDirectory, relative));

            if (!File.Exists(importPath))
                continue;

            var imported = ReadProperty(XDocument.Load(importPath), "EnableDefaultCompileItems");
            if (imported is not null)
                return IsTrue(imported);
        }

        return true;
    }

    static string? ReadProperty(XDocument document, string name) =>
        document.Descendants()
            .Where(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup")
            .Select(e => e.Value.Trim())
            .LastOrDefault();

    static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> SplitItemList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static IEnumerable<string> EnumerateSourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsExcluded(p));

    static IEnumerable<string> ExpandGlob(string projectDirectory, string pattern)
    {
        var normalized = pattern.Replace('\\', '/');

        // Literal path.
        if (!normalized.Contains('*', StringComparison.Ordinal) && !normalized.Contains('?', StringComparison.Ordinal))
        {
            var literal = Path.GetFullPath(Path.Combine(projectDirectory, normalized));
            if (File.Exists(literal))
                yield return literal;

            yield break;
        }

        foreach (var candidate in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsExcluded(candidate))
                continue;

            var relative = Path.GetRelativePath(projectDirectory, candidate).Replace('\\', '/');
            if (GlobMatcher.IsMatch(normalized, relative))
                yield return Path.GetFullPath(candidate);
        }
    }

    static bool IsExcluded(string path)
    {
        var normalized = "/" + path.Replace('\\', '/').Trim('/') + "/";
        return ExcludedDirectorySegments.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}

/// <param name="DefaultCompileItemsEnabled">
/// False for the imported package projects that have not been adopted yet.
/// </param>
/// <param name="Files">Absolute paths of the <c>.cs</c> files this project compiles.</param>
public sealed record CompiledSourceSet(string ProjectPath, bool DefaultCompileItemsEnabled, IReadOnlyList<string> Files)
{
    public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>True when the project compiles nothing, i.e. it is a raw historical import.</summary>
    public bool CompilesNothing => Files.Count == 0;

    public override string ToString() => $"{ProjectName} ({Files.Count} compiled file(s))";
}
