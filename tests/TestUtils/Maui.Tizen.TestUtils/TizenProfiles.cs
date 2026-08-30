using System.Text.Json;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Typed access to <c>eng/validation/profiles/tizen-profiles.json</c>.
/// </summary>
/// <remarks>
/// Holds only validation-specific facts: device profiles, baseline conventions and the dependency
/// policy. The target-framework contract is not restated here; read it from
/// <see cref="RepositoryBaselines"/>.
/// </remarks>
public static class TizenProfiles
{
    static readonly Lazy<TizenValidationMatrix> MatrixLazy = new(Load);

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TizenValidationMatrix Matrix => MatrixLazy.Value;

    public static IReadOnlyList<TizenProfile> Profiles => Matrix.Profiles;

    /// <summary>Profiles that must pass before a release can ship.</summary>
    public static IReadOnlyList<TizenProfile> ReleaseGatingProfiles =>
        [.. Matrix.Profiles.Where(p => p.GatesRelease)];

    public static TizenProfile Profile(string id) =>
        Matrix.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException(
            $"No profile '{id}' in {RepoLayout.Relative(RepoLayout.ProfileMatrixFile)}. " +
            $"Known profiles: {string.Join(", ", Matrix.Profiles.Select(p => p.Id))}.");

    /// <summary>
    /// Every (profile, theme, density) combination that a baseline may exist for. This is the
    /// authoritative expansion used by both the baseline inventory test and the device lane.
    /// </summary>
    public static IEnumerable<BaselineVariant> EnumerateBaselineVariants()
    {
        foreach (var profile in Matrix.Profiles)
        {
            foreach (var theme in profile.Themes)
            {
                foreach (var density in profile.Densities)
                    yield return new BaselineVariant(profile.Id, theme, density);
            }
        }
    }

    static TizenValidationMatrix Load()
    {
        var path = RepoLayout.ProfileMatrixFile;

        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing Tizen validation matrix at {path}.", path);

        return JsonSerializer.Deserialize<TizenValidationMatrix>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
    }
}

public sealed class TizenValidationMatrix
{
    public int SchemaVersion { get; init; }

    /// <summary>Additional platform versions the workload may accept beyond the primary target.</summary>
    public IReadOnlyList<AlsoValidTarget> AlsoValidTargets { get; init; } = [];

    public IReadOnlyList<TizenProfile> Profiles { get; init; } = [];

    public DependencyPolicy DependencyPolicy { get; init; } = new();

    public VisualBaselineSettings VisualBaselines { get; init; } = new();
}

public sealed class AlsoValidTarget
{
    public string TargetFramework { get; init; } = string.Empty;

    public string TizenPlatformVersion { get; init; } = string.Empty;

    /// <summary>False until verified against real Samsung tooling; unconfirmed targets never gate a release.</summary>
    public bool Confirmed { get; init; }
}

public sealed class TizenProfile
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool GatesRelease { get; init; }

    /// <summary>Primary input modality, e.g. <c>touch</c> or <c>remote</c>.</summary>
    public string PrimaryInput { get; init; } = string.Empty;

    public IReadOnlyList<string> InputMethods { get; init; } = [];

    /// <summary>True for profiles where D-pad/remote focus traversal must be validated.</summary>
    public bool RequiresFocusNavigation { get; init; }

    public IReadOnlyList<string> Themes { get; init; } = [];

    public IReadOnlyList<string> Densities { get; init; } = [];

    public IReadOnlyDictionary<string, VisualTarget> VisualTargets { get; init; } =
        new Dictionary<string, VisualTarget>(StringComparer.Ordinal);

    public ScreenSize Screen { get; init; } = new();

    public EmulatorSettings Emulator { get; init; } = new();
}

public sealed class VisualTarget
{
    public int Width { get; init; }

    public int Height { get; init; }

    public double DisplayDensity { get; init; }
}

public sealed class ScreenSize
{
    public int Width { get; init; }

    public int Height { get; init; }
}

public sealed class EmulatorSettings
{
    public string Kind { get; init; } = string.Empty;

    public string ImageFamily { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;
}

public sealed class VisualBaselineSettings
{
    public string Layout { get; init; } = string.Empty;

    public BaselineTolerance DefaultTolerance { get; init; } = new();
}

public sealed class BaselineTolerance
{
    /// <summary>Maximum allowed absolute difference on any single channel.</summary>
    public int MaxChannelDelta { get; init; }

    /// <summary>Maximum fraction of pixels permitted to differ at all.</summary>
    public double MaxDifferingPixelRatio { get; init; }
}

public sealed class ProbePackage
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}

public sealed class DependencyPolicy
{
    public IReadOnlyList<BannedResolution> BannedResolutions { get; init; } = [];
}

/// <summary>A package resolution that must never appear in a restored graph.</summary>
public sealed class BannedResolution
{
    public string Id { get; init; } = string.Empty;

    public string PackageId { get; init; } = string.Empty;

    /// <summary>Version prefixes that are rejected, e.g. <c>6.</c> for the whole 6.x line.</summary>
    public IReadOnlyList<string> BannedVersionPrefixes { get; init; } = [];

    public string Reason { get; init; } = string.Empty;

    /// <summary>Packages most likely to introduce the banned resolution, surfaced in failures.</summary>
    public IReadOnlyList<string> CommonSources { get; init; } = [];

    /// <summary>
    /// Whether the banned resolution is currently expected to be present.
    /// </summary>
    /// <remarks>
    /// <c>known-violation</c> records a tracked external prerequisite; <c>clean</c> means the rule
    /// is enforcing. The tripwire test compares reality against this in both directions so a fixed
    /// upstream package cannot leave a stale exemption behind.
    /// </remarks>
    public string ExpectedStatus { get; init; } = "clean";

    /// <summary>Package to probe when verifying this rule against a published graph.</summary>
    public ProbePackage? ProbePackage { get; init; }

    public bool IsKnownViolation =>
        string.Equals(ExpectedStatus, "known-violation", StringComparison.OrdinalIgnoreCase);

    public string Description => $"{PackageId} {string.Join("/", BannedVersionPrefixes)}x";

    public bool IsViolatedBy(string packageId, string version) =>
        string.Equals(packageId, PackageId, StringComparison.OrdinalIgnoreCase) &&
        BannedVersionPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
