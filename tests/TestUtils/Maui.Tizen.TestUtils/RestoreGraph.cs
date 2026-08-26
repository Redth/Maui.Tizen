using System.Text.Json;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Reads a restored dependency graph from <c>obj/project.assets.json</c>.
/// </summary>
/// <remarks>
/// Asserting on the restored graph rather than on declared <c>PackageReference</c> items is what
/// catches transitive problems: a banned package version is almost never referenced directly, it
/// arrives through something like <c>Tizen.UIExtensions.NUI</c> and only surfaces at runtime.
/// </remarks>
public sealed class RestoreGraph
{
    RestoreGraph(string assetsPath, IReadOnlyDictionary<string, IReadOnlyList<ResolvedPackage>> targets)
    {
        AssetsPath = assetsPath;
        Targets = targets;
    }

    public string AssetsPath { get; }

    /// <summary>Resolved packages keyed by target framework moniker.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ResolvedPackage>> Targets { get; }

    /// <summary>Every resolved package across all targets.</summary>
    public IEnumerable<ResolvedPackage> AllPackages => Targets.Values.SelectMany(v => v);

    /// <summary>Loads the assets file produced by restoring <paramref name="projectDirectory"/>.</summary>
    public static RestoreGraph LoadFromProjectDirectory(string projectDirectory)
    {
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        return Load(assetsPath);
    }

    public static RestoreGraph Load(string assetsPath)
    {
        if (!File.Exists(assetsPath))
        {
            throw new FileNotFoundException(
                $"No restore graph at '{assetsPath}'. Restore the project before asserting on its " +
                "dependency graph.",
                assetsPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));

        var targets = new Dictionary<string, IReadOnlyList<ResolvedPackage>>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.TryGetProperty("targets", out var targetsElement))
        {
            foreach (var target in targetsElement.EnumerateObject())
            {
                var packages = new List<ResolvedPackage>();

                foreach (var entry in target.Value.EnumerateObject())
                {
                    // Keys are "Id/Version".
                    var slash = entry.Name.LastIndexOf('/');
                    if (slash <= 0)
                        continue;

                    var id = entry.Name[..slash];
                    var version = entry.Name[(slash + 1)..];

                    var dependencies = new List<string>();
                    if (entry.Value.TryGetProperty("dependencies", out var deps))
                        dependencies.AddRange(deps.EnumerateObject().Select(d => d.Name));

                    var type = entry.Value.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString() ?? "package"
                        : "package";

                    packages.Add(new ResolvedPackage(id, version, type, dependencies));
                }

                targets[target.Name] = packages;
            }
        }

        return new RestoreGraph(assetsPath, targets);
    }

    /// <summary>Packages in <paramref name="targetFramework"/> that depend on <paramref name="packageId"/>.</summary>
    public IReadOnlyList<ResolvedPackage> FindDependents(string targetFramework, string packageId)
    {
        if (!Targets.TryGetValue(targetFramework, out var packages))
            return [];

        return [.. packages.Where(p =>
            p.Dependencies.Any(d => string.Equals(d, packageId, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>Evaluates the repository dependency policy against this graph.</summary>
    public IReadOnlyList<DependencyPolicyViolation> EvaluatePolicy(DependencyPolicy policy)
    {
        var violations = new List<DependencyPolicyViolation>();

        foreach (var (targetFramework, packages) in Targets)
        {
            foreach (var package in packages)
            {
                foreach (var rule in policy.BannedResolutions)
                {
                    if (!rule.IsViolatedBy(package.Id, package.Version))
                        continue;

                    var dependents = FindDependents(targetFramework, package.Id);
                    violations.Add(new DependencyPolicyViolation(rule, targetFramework, package, dependents));
                }
            }
        }

        return violations;
    }
}

/// <param name="Type">NuGet library type, typically <c>package</c> or <c>project</c>.</param>
/// <param name="Dependencies">Package ids this package depends on.</param>
public sealed record ResolvedPackage(string Id, string Version, string Type, IReadOnlyList<string> Dependencies)
{
    public override string ToString() => $"{Id}/{Version}";
}

public sealed record DependencyPolicyViolation(
    BannedResolution Rule,
    string TargetFramework,
    ResolvedPackage Package,
    IReadOnlyList<ResolvedPackage> Dependents)
{
    /// <summary>Failure text that names the rule, the offending resolution and who pulled it in.</summary>
    public string Describe()
    {
        var pulledInBy = Dependents.Count == 0
            ? "a direct PackageReference"
            : string.Join(", ", Dependents.Select(d => d.ToString()));

        var likely = Rule.CommonSources.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}  Usual sources: {string.Join(", ", Rule.CommonSources)}.";

        return
            $"Dependency policy '{Rule.Id}' violated for {TargetFramework}:{Environment.NewLine}" +
            $"  Resolved {Package} which is banned ({Rule.Description}).{Environment.NewLine}" +
            $"  Pulled in by: {pulledInBy}.{Environment.NewLine}" +
            $"  Reason: {Rule.Reason}{likely}";
    }
}
