using System.Reflection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Resolves repository-relative locations for the validation lane.
/// </summary>
/// <remarks>
/// The repository root is baked in at build time via <see cref="AssemblyMetadataAttribute"/>
/// (see tests/Directory.Build.targets). Walking up from the current working directory is not
/// reliable because <c>dotnet test</c>, VSTest and IDE runners all use different working
/// directories, and CI sometimes shadow-copies test binaries.
/// </remarks>
public static class RepoLayout
{
    static readonly Lazy<string> RootLazy = new(ResolveRoot);

    /// <summary>Absolute path of the repository root, with a trailing separator.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>The Tizen target framework the validation lane is currently pinned to.</summary>
    public static string TizenTargetFramework =>
        Metadata("Maui.Tizen.TizenTargetFramework") ?? "net10.0-tizen";

    /// <summary>The host target framework used by workload-free projects.</summary>
    public static string HostTargetFramework =>
        Metadata("Maui.Tizen.HostTargetFramework") ?? "net10.0";

    public static string Src => Combine("src");

    public static string Tests => Combine("tests");

    public static string Samples => Combine("samples");

    public static string Eng => Combine("eng");

    public static string ValidationConfig => Combine("eng", "validation");

    public static string PackageContentContracts => Combine("eng", "validation", "package-contents");

    public static string ApprovedApiDirectory => Combine("eng", "api");

    public static string ProfileMatrixFile => Combine("eng", "validation", "profiles", "tizen-profiles.json");

    /// <summary>The frozen extraction contract owned by the foundation import.</summary>
    public static string BaselinesFile => Combine("eng", "baselines.json");

    /// <summary>Repository-root MSBuild props that mirror <see cref="BaselinesFile"/>.</summary>
    public static string RootDirectoryBuildProps => Combine("Directory.Build.props");

    public static string GlobalJsonFile => Combine("global.json");

    public static string DiagnosticsSource => Combine("src", "Diagnostics");

    public static string VisualBaselineRoot => Combine("tests", "VisualBaselines");

    public static string CatalogManifestFile => Combine("samples", "Maui.Tizen.Catalog", "catalog-manifest.json");

    public static string MSBuildFixtures => Combine("tests", "fixtures", "msbuild");

    /// <summary>Combines <paramref name="segments"/> onto the repository root.</summary>
    public static string Combine(params string[] segments) =>
        Path.GetFullPath(Path.Combine(new[] { Root }.Concat(segments).ToArray()));

    /// <summary>Returns the path relative to the repository root, using forward slashes.</summary>
    public static string Relative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    static string ResolveRoot()
    {
        var fromMetadata = Metadata("Maui.Tizen.RepositoryRoot");
        if (!string.IsNullOrEmpty(fromMetadata) && Directory.Exists(fromMetadata))
            return Path.GetFullPath(fromMetadata);

        // Fallback for unusual hosts: walk up looking for a repository marker.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Unable to resolve the repository root. Expected the Maui.Tizen.RepositoryRoot assembly " +
            "metadata to be present (tests/Directory.Build.targets) or a .git marker above " +
            AppContext.BaseDirectory + ".");
    }

    static string? Metadata(string key) =>
        typeof(RepoLayout).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal))
            ?.Value;
}
