using System.Collections;
using System.Reflection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Reads MAUI property and command mappers by shape rather than by type reference.
/// </summary>
/// <remarks>
/// <para>
/// Duck typing is deliberate. Binding directly to <c>IPropertyMapper</c> would force this shared
/// library to reference a specific <c>Microsoft.Maui.Core</c> build, which is exactly the assembly
/// under test. Reading <c>GetKeys()</c> by shape lets the same analyzer run against the Tizen
/// backend on-device and against fakes on a hosted runner.
/// </para>
/// <para>
/// Mapper contents are runtime state built by static initialisers, so they cannot be read from
/// metadata alone. Parity therefore executes wherever the backend can execute; see
/// <see cref="ProductAssemblies"/>.
/// </para>
/// </remarks>
public static class MapperInspector
{
    public const string PropertyMapperMemberName = "Mapper";

    public const string CommandMapperMemberName = "CommandMapper";

    /// <summary>Reads the value of a public static field or property on <paramref name="type"/>.</summary>
    public static object? GetStaticMember(Type type, string memberName)
    {
        ArgumentNullException.ThrowIfNull(type);

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        var field = type.GetField(memberName, Flags);
        if (field is not null)
            return field.GetValue(null);

        var property = type.GetProperty(memberName, Flags);
        return property?.GetValue(null);
    }

    /// <summary>
    /// Extracts mapper keys by invoking a parameterless <c>GetKeys()</c> that returns strings.
    /// </summary>
    public static bool TryGetKeys(object? mapper, out IReadOnlyList<string> keys)
    {
        keys = [];

        if (mapper is null)
            return false;

        var getKeys = mapper.GetType().GetMethod(
            "GetKeys",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (getKeys is null)
            return false;

        if (getKeys.Invoke(mapper, null) is not IEnumerable enumerable)
            return false;

        keys = [.. enumerable.OfType<string>().OrderBy(k => k, StringComparer.Ordinal)];
        return true;
    }

    /// <summary>Reads property mapper keys declared by <paramref name="handlerType"/>.</summary>
    public static bool TryGetPropertyMapperKeys(Type handlerType, out IReadOnlyList<string> keys) =>
        TryGetKeys(GetStaticMember(handlerType, PropertyMapperMemberName), out keys);

    /// <summary>Reads command mapper keys declared by <paramref name="handlerType"/>.</summary>
    public static bool TryGetCommandMapperKeys(Type handlerType, out IReadOnlyList<string> keys) =>
        TryGetKeys(GetStaticMember(handlerType, CommandMapperMemberName), out keys);
}

/// <summary>
/// Compares the mapper surface a platform handler exposes against the expected cross-platform set.
/// </summary>
/// <remarks>
/// A handler that silently omits a mapper key does not fail to build and does not throw. The
/// property simply never reaches the platform view, and the control looks subtly wrong. Parity
/// checks turn that class of bug into a build failure.
/// </remarks>
public static class HandlerParityAnalyzer
{
    /// <summary>Compares two key sets.</summary>
    /// <param name="subject">Label used in failure text, typically the handler name.</param>
    /// <param name="expected">Keys the cross-platform handler declares.</param>
    /// <param name="actual">Keys the platform handler declares.</param>
    /// <param name="knownPlatformOnlyKeys">
    /// Keys the platform legitimately adds. These are allowed as extras but still reported so the
    /// list cannot quietly grow.
    /// </param>
    public static HandlerParityReport Compare(
        string subject,
        IEnumerable<string> expected,
        IEnumerable<string> actual,
        IEnumerable<string>? knownPlatformOnlyKeys = null)
    {
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        var allowed = new HashSet<string>(knownPlatformOnlyKeys ?? [], StringComparer.Ordinal);

        var missing = expectedSet.Except(actualSet).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var extra = actualSet.Except(expectedSet).OrderBy(k => k, StringComparer.Ordinal).ToList();

        var unexpectedExtra = extra.Where(k => !allowed.Contains(k)).ToList();
        var declaredExtra = extra.Where(allowed.Contains).ToList();

        return new HandlerParityReport(subject, missing, unexpectedExtra, declaredExtra);
    }
}

public sealed record HandlerParityReport(
    string Subject,
    IReadOnlyList<string> MissingKeys,
    IReadOnlyList<string> UnexpectedExtraKeys,
    IReadOnlyList<string> DeclaredPlatformOnlyKeys)
{
    public bool Passed => MissingKeys.Count == 0 && UnexpectedExtraKeys.Count == 0;

    public string Describe()
    {
        if (Passed)
        {
            var extras = DeclaredPlatformOnlyKeys.Count == 0
                ? string.Empty
                : $" ({DeclaredPlatformOnlyKeys.Count} declared platform-only key(s))";

            return $"'{Subject}' mapper parity holds{extras}.";
        }

        var lines = new List<string> { $"'{Subject}' mapper parity failed:" };

        if (MissingKeys.Count > 0)
        {
            lines.Add($"  Missing {MissingKeys.Count} key(s) the cross-platform handler declares:");
            lines.AddRange(MissingKeys.Select(k => $"      {k}"));
        }

        if (UnexpectedExtraKeys.Count > 0)
        {
            lines.Add($"  {UnexpectedExtraKeys.Count} undeclared platform-only key(s):");
            lines.AddRange(UnexpectedExtraKeys.Select(k => $"      {k}"));
            lines.Add("  Add them to the handler's known platform-only list if intentional.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
