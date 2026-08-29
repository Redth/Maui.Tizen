using System.IO.Compression;
using System.Xml.Linq;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Reads the declared dependencies of a published NuGet package without restoring it.
/// </summary>
/// <remarks>
/// <para>
/// Several packages this repository depends on target Tizen framework monikers that cannot be
/// restored anywhere until the Samsung workload ships. Their dependency graphs are still worth
/// asserting on now, because a bad transitive dependency is the kind of thing that only surfaces
/// as a runtime failure on a device.
/// </para>
/// <para>
/// Resolution order is deliberately cache-first: the NuGet global packages folder if the package is
/// already there, then nuget.org's flat container. That keeps the check fast and makes it work
/// offline once a restore has happened, while still functioning on a clean CI agent.
/// </para>
/// </remarks>
public static class PackageDependencyProbe
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Reads the dependency groups declared by a package.</summary>
    /// <returns><see langword="null"/> when the package could not be located.</returns>
    public static async Task<IReadOnlyList<PackageDependency>?> TryReadDependenciesAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(version);

        var nuspec = TryReadCachedNuspec(packageId, version)
                     ?? await TryDownloadNuspecAsync(packageId, version, cancellationToken).ConfigureAwait(false);

        return nuspec is null ? null : ParseDependencies(nuspec);
    }

    /// <summary>Global packages folder, honouring <c>NUGET_PACKAGES</c>.</summary>
    public static string GlobalPackagesFolder =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");

    static XDocument? TryReadCachedNuspec(string packageId, string version)
    {
        var path = Path.Combine(
            GlobalPackagesFolder,
            packageId.ToLowerInvariant(),
            version.ToLowerInvariant(),
            $"{packageId.ToLowerInvariant()}.nuspec");

        if (!File.Exists(path))
            return null;

        try
        {
            return XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    static async Task<XDocument?> TryDownloadNuspecAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var lowerId = packageId.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();
        var url = $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.nuspec";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            // Network access is not guaranteed on every runner. Callers treat null as "unknown"
            // and skip rather than failing, so a offline agent cannot produce a false positive.
            return null;
        }
    }

    static IReadOnlyList<PackageDependency> ParseDependencies(XDocument nuspec)
    {
        var metadata = nuspec.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        var dependencies = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");

        if (dependencies is null)
            return [];

        var results = new List<PackageDependency>();

        foreach (var group in dependencies.Elements().Where(e => e.Name.LocalName == "group"))
        {
            var tfm = group.Attribute("targetFramework")?.Value ?? string.Empty;

            foreach (var dependency in group.Elements().Where(e => e.Name.LocalName == "dependency"))
                results.Add(Create(tfm, dependency));
        }

        foreach (var dependency in dependencies.Elements().Where(e => e.Name.LocalName == "dependency"))
            results.Add(Create(string.Empty, dependency));

        return results;

        static PackageDependency Create(string tfm, XElement dependency) => new(
            tfm,
            dependency.Attribute("id")?.Value ?? string.Empty,
            dependency.Attribute("version")?.Value ?? string.Empty);
    }

    /// <summary>
    /// Lowest version a NuGet range resolves to, which is what a restore actually picks.
    /// </summary>
    /// <remarks>
    /// A dependency declared as <c>[6.0.0, )</c> or <c>6.0.0</c> both resolve to 6.0.0 unless
    /// something else in the graph forces higher, so the lower bound is the right thing to test a
    /// banned-version rule against.
    /// </remarks>
    public static string LowerBound(string versionRange)
    {
        if (string.IsNullOrWhiteSpace(versionRange))
            return string.Empty;

        var trimmed = versionRange.Trim();

        if (trimmed.Length == 0 || (trimmed[0] != '[' && trimmed[0] != '('))
            return trimmed;

        var inner = trimmed[1..].TrimEnd(']', ')');
        var comma = inner.IndexOf(',');
        var lower = comma < 0 ? inner : inner[..comma];

        return lower.Trim();
    }
}
