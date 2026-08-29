using System.Xml.Linq;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Works out which <c>.cs</c> files a project actually compiles.
/// </summary>
/// <remarks>
/// <para>
/// This distinction is the whole point of the API15 source guards. The raw imports share
/// directories with adopted code. Core, Controls and Essentials keep default globbing disabled and
/// include explicit shared source manifests, while unported projects still compile nothing.
/// Scanning every file would report upstream history rather than the shipping closure.
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
        var model = ProjectModel.Load(projectPath);
        var defaultItemsEnabled = model.GetBooleanProperty("EnableDefaultCompileItems", defaultValue: true);

        var includes = model.GetItems("Compile", "Include")
            .Concat(model.GetItems("Compile", "Update"))
            .ToList();
        var removes = model.GetItems("Compile", "Remove").ToList();

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

    sealed class ProjectModel
    {
        readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<XElement>> _items = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

        ProjectModel(string projectPath)
        {
            _properties["RepositoryRoot"] = RepoLayout.Root + Path.DirectorySeparatorChar;
            Visit(Path.GetFullPath(projectPath));
        }

        public static ProjectModel Load(string projectPath) => new(projectPath);

        public bool GetBooleanProperty(string name, bool defaultValue) =>
            _properties.TryGetValue(name, out var value)
                ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                : defaultValue;

        public IEnumerable<string> GetItems(string itemName, string attributeName)
        {
            if (!_items.TryGetValue(itemName, out var items))
                yield break;

            foreach (var item in items)
            {
                if (item.Attribute(attributeName)?.Value is not { Length: > 0 } value)
                    continue;

                foreach (var expanded in ExpandItemExpression(value))
                    yield return expanded;
            }
        }

        void Visit(string path)
        {
            if (!File.Exists(path) || !_visited.Add(path))
                return;

            var directory = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
            var document = XDocument.Load(path);

            foreach (var element in document.Root?.Elements() ?? [])
            {
                switch (element.Name.LocalName)
                {
                    case "PropertyGroup":
                        foreach (var property in element.Elements())
                        {
                            // The inventory cannot evaluate arbitrary MSBuild conditions. These
                            // source manifests only use "set when empty", for which retaining the
                            // first value is the faithful result.
                            if (property.Attribute("Condition") is not null &&
                                _properties.ContainsKey(property.Name.LocalName))
                            {
                                continue;
                            }

                            _properties[property.Name.LocalName] =
                                ExpandProperties(property.Value.Trim(), directory);
                        }
                        break;

                    case "Import":
                        if (element.Attribute("Project")?.Value is { Length: > 0 } import)
                        {
                            var importPath = ExpandProperties(import, directory);
                            if (!Path.IsPathRooted(importPath))
                                importPath = Path.Combine(directory, importPath);
                            Visit(Path.GetFullPath(importPath));
                        }
                        break;

                    case "ItemGroup":
                        foreach (var item in element.Elements())
                        {
                            var clone = new XElement(item);
                            foreach (var attribute in clone.Attributes().ToList())
                                attribute.Value = ExpandProperties(attribute.Value, directory);

                            if (!_items.TryGetValue(item.Name.LocalName, out var values))
                                _items[item.Name.LocalName] = values = [];
                            values.Add(clone);
                        }
                        break;
                }
            }
        }

        IEnumerable<string> ExpandItemExpression(string value)
        {
            foreach (var component in value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (component.StartsWith("@(", StringComparison.Ordinal) &&
                    component.EndsWith(')'))
                {
                    var itemName = component[2..^1];
                    foreach (var item in GetItems(itemName, "Include"))
                        yield return item;
                }
                else
                {
                    yield return component;
                }
            }
        }

        string ExpandProperties(string value, string currentDirectory)
        {
            var expanded = value.Replace(
                "$(MSBuildThisFileDirectory)",
                currentDirectory,
                StringComparison.OrdinalIgnoreCase);

            for (var pass = 0; pass < 8; pass++)
            {
                var before = expanded;
                foreach (var (name, propertyValue) in _properties)
                {
                    expanded = expanded.Replace(
                        $"$({name})",
                        propertyValue,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (expanded == before)
                    break;
            }

            return expanded;
        }
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
