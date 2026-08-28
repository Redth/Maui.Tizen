using System.Reflection;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Detects whether the Samsung Tizen workload is installed on the current machine.
/// </summary>
/// <remarks>
/// <para>
/// The probe mirrors the one in the repository-root <c>Directory.Build.props</c>: it looks for a
/// <c>samsung.net.sdk.tizen</c> workload manifest under the SDK's <c>sdk-manifests</c> directory.
/// </para>
/// <para>
/// A common and costly misdiagnosis is to treat the <c>maui-tizen</c> workload as sufficient. It is
/// not: in <c>microsoft.net.sdk.maui</c>'s manifest, <c>maui-tizen</c> only declares
/// <c>"extends": ["maui-blazor"]</c> and carries no Tizen SDK packs at all. The actual platform
/// support comes from Samsung's separate <c>samsung.net.sdk.tizen</c> manifest. That is why
/// <c>dotnet workload list</c> can report <c>maui-tizen</c> as installed while any
/// <c>net*-tizen*</c> build still fails with NETSDK1139 ("the target platform identifier tizen was
/// not recognized").
/// </para>
/// </remarks>
public static class TizenWorkload
{
    static readonly Lazy<TizenWorkloadProbe> ProbeLazy = new(Probe);

    /// <summary>True when a Tizen target framework can actually be built here.</summary>
    public static bool IsAvailable => ProbeLazy.Value.Available;

    /// <summary>Human-readable explanation of the probe result, used in skip reasons.</summary>
    public static string Diagnosis => ProbeLazy.Value.Diagnosis;

    /// <summary>Manifest directories that were searched.</summary>
    public static IReadOnlyList<string> SearchedPaths => ProbeLazy.Value.SearchedPaths;

    static TizenWorkloadProbe Probe()
    {
        // MSBuild already computed this during the build; prefer it so tests and the build agree.
        var fromBuild = typeof(TizenWorkload).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Maui.Tizen.TizenWorkloadAvailable")
            ?.Value;

        if (bool.TryParse(fromBuild, out var declared) && declared)
            return new TizenWorkloadProbe(true, "Declared available by the build (TizenWorkloadAvailable=true).", []);

        var searched = new List<string>();

        foreach (var manifestRoot in EnumerateManifestRoots())
        {
            searched.Add(manifestRoot);

            if (!Directory.Exists(manifestRoot))
                continue;

            foreach (var band in Directory.EnumerateDirectories(manifestRoot))
            {
                var manifest = Path.Combine(band, "samsung.net.sdk.tizen", "WorkloadManifest.json");
                if (File.Exists(manifest))
                    return new TizenWorkloadProbe(true, $"Found Samsung workload manifest at {manifest}.", searched);
            }
        }

        return new TizenWorkloadProbe(
            false,
            "No 'samsung.net.sdk.tizen' workload manifest found. Note that the 'maui-tizen' workload " +
            "does NOT provide Tizen platform packs; only Samsung's manifest does. " +
            $"Searched: {(searched.Count == 0 ? "(no SDK root resolved)" : string.Join(", ", searched))}.",
            searched);
    }

    static IEnumerable<string> EnumerateManifestRoots()
    {
        var roots = new List<string>();

        // DOTNET_ROOT is the reliable signal on CI agents.
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            roots.Add(Path.Combine(dotnetRoot, "sdk-manifests"));

        // Fall back to the SDK hosting the current process.
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var candidate = new DirectoryInfo(runtimeDirectory);
        while (candidate is not null)
        {
            if (Directory.Exists(Path.Combine(candidate.FullName, "sdk-manifests")))
            {
                roots.Add(Path.Combine(candidate.FullName, "sdk-manifests"));
                break;
            }

            candidate = candidate.Parent;
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    sealed record TizenWorkloadProbe(bool Available, string Diagnosis, IReadOnlyList<string> SearchedPaths);
}
