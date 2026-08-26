using System.Xml.Linq;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Reads literal property values out of an MSBuild file without evaluating it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a literal text read, not an MSBuild evaluation. The point of these assertions is to
/// check that a human-maintained mirror of <c>eng/baselines.json</c> still says what the baseline
/// says. Evaluating the project would resolve <c>$(DotNetTfm)</c> style references and happily
/// report a correct-looking value even when the literal text had drifted, which is precisely the
/// failure being guarded against.
/// </para>
/// <para>
/// Simple <c>$(Name)</c> references are expanded against properties already seen, so composed values
/// such as <c>net$(DotNetVersion)-tizen$(TizenPlatformVersion)</c> can still be compared.
/// </para>
/// </remarks>
public static class MSBuildPropertyReader
{
    /// <summary>Reads all <c>PropertyGroup</c> properties from an MSBuild file.</summary>
    public static IReadOnlyDictionary<string, string> ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"MSBuild file not found: {path}", path);

        return Read(File.ReadAllText(path));
    }

    /// <summary>Reads all <c>PropertyGroup</c> properties from MSBuild XML.</summary>
    public static IReadOnlyDictionary<string, string> Read(string xml)
    {
        var document = XDocument.Parse(xml);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var groups = document.Root?
            .Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup") ?? [];

        foreach (var property in groups.SelectMany(g => g.Elements()))
        {
            // Later definitions win, mirroring MSBuild's last-one-wins semantics.
            properties[property.Name.LocalName] = Expand(property.Value.Trim(), properties);
        }

        return properties;
    }

    /// <summary>Gets a property value, or null when absent.</summary>
    public static string? Value(this IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value : null;

    static string Expand(string value, IReadOnlyDictionary<string, string> known)
    {
        if (!value.Contains("$(", StringComparison.Ordinal))
            return value;

        var result = value;

        foreach (var (name, resolved) in known)
            result = result.Replace($"$({name})", resolved, StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
