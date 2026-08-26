using System.Text.Json;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Typed access to <c>eng/baselines.json</c>, the frozen contract for the
/// dotnet/maui to Maui.Tizen extraction.
/// </summary>
/// <remarks>
/// The repository-root <c>Directory.Build.props</c> mirrors several of these values into MSBuild
/// properties because MSBuild cannot read JSON at evaluation time. That duplication is the reason
/// this type exists: <c>RepositoryContractTests</c> asserts the two stay in sync so a hand edit to
/// one side cannot silently diverge.
/// </remarks>
public static class RepositoryBaselines
{
    static readonly Lazy<BaselineDocument> DocumentLazy = new(Load);

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>True when <c>eng/baselines.json</c> is present in the working tree.</summary>
    public static bool Exists => File.Exists(RepoLayout.BaselinesFile);

    public static BaselineDocument Document => DocumentLazy.Value;

    public static BaselineTarget Target => Document.Target;

    public static BaselinePolicy Policy => Document.Policy;

    static BaselineDocument Load()
    {
        var path = RepoLayout.BaselinesFile;

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Missing {RepoLayout.Relative(path)}. It is introduced by the foundation import; " +
                "suites that depend on it skip until then.",
                path);
        }

        return JsonSerializer.Deserialize<BaselineDocument>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
    }
}

public sealed class BaselineDocument
{
    public int SchemaVersion { get; init; }

    public BaselineTarget Target { get; init; } = new();

    public BaselinePolicy Policy { get; init; } = new();
}

/// <summary>The workload contract this repository builds against.</summary>
public sealed class BaselineTarget
{
    /// <summary>e.g. <c>11.0</c>.</summary>
    public string DotNetVersion { get; init; } = string.Empty;

    /// <summary>e.g. <c>net11.0-tizen11.0</c>.</summary>
    public string TargetFramework { get; init; } = string.Empty;

    /// <summary>e.g. <c>11.0</c>.</summary>
    public string TizenPlatformVersion { get; init; } = string.Empty;

    /// <summary>e.g. <c>11.0.100-preview.7</c>.</summary>
    public string SdkBand { get; init; } = string.Empty;

    /// <summary><c>api-version</c> written into <c>tizen-manifest.xml</c>, e.g. <c>11</c>.</summary>
    public string TizenManifestApiVersion { get; init; } = string.Empty;

    /// <summary>e.g. <c>API15</c>.</summary>
    public string TizenFxApiLevel { get; init; } = string.Empty;

    public BaselineReferencePack ReferencePack { get; init; } = new();

    public BaselineWorkloadManifest WorkloadManifest { get; init; } = new();
}

public sealed class BaselineReferencePack
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}

public sealed class BaselineWorkloadManifest
{
    /// <summary>e.g. <c>samsung.net.sdk.tizen.manifest-11.0.100</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary><c>available</c> or <c>unavailable</c>.</summary>
    public string Status { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// True when the baseline itself declares the Samsung workload manifest as unpublished. This is
    /// the repository's own record of the external blocker, independent of what happens to be
    /// installed on the current runner.
    /// </summary>
    public bool IsUnavailable =>
        string.Equals(Status, "unavailable", StringComparison.OrdinalIgnoreCase);
}

public sealed class BaselinePolicy
{
    public string MinimumDotNet { get; init; } = string.Empty;

    public bool SupportsDotNet10 { get; init; }

    public string PreservedNamespacePrefix { get; init; } = string.Empty;

    public string NewImplementationNamespacePrefix { get; init; } = string.Empty;

    /// <summary>e.g. <c>Maui.Tizen</c>. Shipping package ids must use this prefix.</summary>
    public string PackageIdPrefix { get; init; } = string.Empty;
}
