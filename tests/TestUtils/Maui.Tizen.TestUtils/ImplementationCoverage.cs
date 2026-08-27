using System.Reflection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Checks that a set of service contracts has exactly one concrete implementation in a backend
/// assembly.
/// </summary>
/// <remarks>
/// Used for the Essentials surface, where each interface is expected to have a Tizen
/// implementation. A missing implementation compiles cleanly and fails only when an app calls the
/// API, so it needs to be a build-time assertion.
/// </remarks>
public static class ImplementationCoverageAnalyzer
{
    /// <summary>
    /// Maps each contract in <paramref name="contracts"/> to the concrete types in
    /// <paramref name="implementationAssembly"/> that implement it.
    /// </summary>
    public static ImplementationCoverageReport Analyze(
        string subject,
        IEnumerable<Type> contracts,
        Assembly implementationAssembly,
        IEnumerable<Type>? knownUnimplemented = null)
    {
        ArgumentNullException.ThrowIfNull(implementationAssembly);

        var candidates = SafeGetTypes(implementationAssembly)
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .ToList();

        var allowed = new HashSet<Type>(knownUnimplemented ?? []);

        var missing = new List<Type>();
        var ambiguous = new List<(Type Contract, IReadOnlyList<Type> Implementations)>();
        var resolved = new Dictionary<Type, Type>();

        foreach (var contract in contracts)
        {
            var implementations = candidates
                .Where(t => contract.IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            switch (implementations.Count)
            {
                case 0 when !allowed.Contains(contract):
                    missing.Add(contract);
                    break;
                case 0:
                    break;
                case 1:
                    resolved[contract] = implementations[0];
                    break;
                default:
                    ambiguous.Add((contract, implementations));
                    break;
            }
        }

        return new ImplementationCoverageReport(subject, missing, ambiguous, resolved);
    }

    /// <summary>Returns loadable types, tolerating partially resolvable assemblies.</summary>
    public static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(t => t is not null).Cast<Type>()];
        }
    }
}

public sealed record ImplementationCoverageReport(
    string Subject,
    IReadOnlyList<Type> MissingImplementations,
    IReadOnlyList<(Type Contract, IReadOnlyList<Type> Implementations)> AmbiguousImplementations,
    IReadOnlyDictionary<Type, Type> ResolvedImplementations)
{
    public bool Passed => MissingImplementations.Count == 0 && AmbiguousImplementations.Count == 0;

    public string Describe()
    {
        if (Passed)
            return $"'{Subject}': {ResolvedImplementations.Count} contract(s) each have exactly one implementation.";

        var lines = new List<string> { $"'{Subject}' implementation coverage failed:" };

        if (MissingImplementations.Count > 0)
        {
            lines.Add($"  {MissingImplementations.Count} contract(s) with no implementation:");
            lines.AddRange(MissingImplementations
                .Select(t => $"      {t.FullName}")
                .OrderBy(l => l, StringComparer.Ordinal));
        }

        foreach (var (contract, implementations) in AmbiguousImplementations)
        {
            lines.Add($"  {contract.FullName} has {implementations.Count} implementations:");
            lines.AddRange(implementations.Select(t => $"      {t.FullName}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
