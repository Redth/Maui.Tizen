using System.IO.Compression;
using System.Xml.Linq;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Minimal read-only view over a <c>.nupkg</c>.
/// </summary>
/// <remarks>
/// Deliberately implemented over <see cref="ZipArchive"/> and <see cref="XDocument"/> instead of
/// taking a NuGet.Packaging dependency. Package-content assertions must describe the bytes that are
/// actually shipped; going through NuGet's own object model risks asserting NuGet's interpretation
/// of a package rather than its literal contents.
/// </remarks>
public sealed class NuPkg : IDisposable
{
    static readonly XNamespace NuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    readonly ZipArchive _archive;

    NuPkg(string path, ZipArchive archive)
    {
        Path = path;
        _archive = archive;
        Entries = [.. archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(e => !e.EndsWith('/'))
            .OrderBy(e => e, StringComparer.Ordinal)];
    }

    /// <summary>Absolute path of the package file.</summary>
    public string Path { get; }

    /// <summary>All entry paths in the package, sorted ordinally and using forward slashes.</summary>
    public IReadOnlyList<string> Entries { get; }

    public static NuPkg Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Package not found: {path}", path);

        return new NuPkg(path, ZipFile.OpenRead(path));
    }

    /// <summary>Finds the single <c>.nupkg</c> for <paramref name="packageId"/> in a directory.</summary>
    public static NuPkg OpenFromDirectory(string directory, string packageId)
    {
        var matches = Directory
            .EnumerateFiles(directory, $"{packageId}.*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return matches.Count switch
        {
            1 => Open(matches[0]),
            0 => throw new FileNotFoundException(
                $"No package matching '{packageId}.*.nupkg' was produced in '{directory}'."),
            _ => throw new InvalidOperationException(
                $"Expected exactly one '{packageId}' package in '{directory}' but found {matches.Count}: " +
                string.Join(", ", matches.Select(System.IO.Path.GetFileName))),
        };
    }

    /// <summary>Reads the embedded <c>.nuspec</c>.</summary>
    public XDocument ReadNuspec()
    {
        var entry = _archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains('/', StringComparison.Ordinal));

        if (entry is null)
            throw new InvalidOperationException($"'{Path}' does not contain a root .nuspec entry.");

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>Package dependencies as (targetFramework, id, versionRange) triples.</summary>
    public IReadOnlyList<PackageDependency> ReadDependencies()
    {
        var nuspec = ReadNuspec();
        var metadata = nuspec.Root?.Element(NuspecNamespace + "metadata")
                       ?? nuspec.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");

        var dependencies = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null)
            return [];

        var results = new List<PackageDependency>();

        foreach (var group in dependencies.Elements().Where(e => e.Name.LocalName == "group"))
        {
            var tfm = group.Attribute("targetFramework")?.Value ?? string.Empty;
            foreach (var dep in group.Elements().Where(e => e.Name.LocalName == "dependency"))
                results.Add(Create(tfm, dep));
        }

        // Ungrouped dependencies apply to every target framework.
        foreach (var dep in dependencies.Elements().Where(e => e.Name.LocalName == "dependency"))
            results.Add(Create(string.Empty, dep));

        return results;

        static PackageDependency Create(string tfm, XElement dep) => new(
            tfm,
            dep.Attribute("id")?.Value ?? string.Empty,
            dep.Attribute("version")?.Value ?? string.Empty);
    }

    /// <summary>Reads a text entry, or <see langword="null"/> when it is absent.</summary>
    public string? ReadText(string entryPath)
    {
        var entry = _archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), entryPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return null;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => _archive.Dispose();
}

/// <summary>A single nuspec dependency entry.</summary>
/// <param name="TargetFramework">Group TFM, or empty when ungrouped.</param>
/// <param name="Id">Package id.</param>
/// <param name="VersionRange">Raw version range string as written in the nuspec.</param>
public sealed record PackageDependency(string TargetFramework, string Id, string VersionRange);
