namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// How the agent reports its platform in <c>GET /api/v1/agent/status</c>.
/// </summary>
/// <remarks>
/// The DevFlow <c>agent-status.json</c> schema constrains <c>platform</c> to
/// <c>ios | android | maccatalyst | windows | linux | macos</c>. <c>tizen</c> is not a member, so an
/// accurate value is out of spec until maui-labs adds it. Both behaviours are therefore explicit and
/// selectable rather than silently hard-coded.
/// </remarks>
public enum TizenPlatformReporting
{
    /// <summary>
    /// Report <c>tizen</c>. Accurate, and what a Tizen-aware driver should consume. Strict
    /// schema-validating clients reject it until the upstream spec is updated.
    /// </summary>
    Accurate,

    /// <summary>
    /// Report <c>linux</c> so stock schema-validating clients keep working, carrying the real
    /// platform in <see cref="TizenPlatformIdentity.PlatformExtensionKey"/>.
    /// </summary>
    SchemaCompatible,
}

/// <summary>Platform identity reported by the Tizen DevFlow agent.</summary>
public sealed class TizenPlatformIdentity
{
    /// <summary>The accurate platform name for Tizen.</summary>
    public const string TizenPlatformName = "tizen";

    /// <summary>
    /// Value used in <see cref="TizenPlatformReporting.SchemaCompatible"/> mode. Tizen is
    /// Linux-based, so this is the least wrong member of the current spec enum.
    /// </summary>
    public const string SchemaCompatiblePlatformName = "linux";

    /// <summary>
    /// Vendor extension key carrying the real platform when the reported value was downgraded for
    /// schema compatibility.
    /// </summary>
    public const string PlatformExtensionKey = "x-platform";

    /// <summary>Framework name reported alongside the platform.</summary>
    public const string FrameworkName = "maui";

    public TizenPlatformIdentity(
        TizenPlatformReporting reporting = TizenPlatformReporting.Accurate,
        string profile = TizenDeviceProfiles.Mobile)
    {
        Reporting = reporting;
        Profile = profile;
    }

    public TizenPlatformReporting Reporting { get; }

    /// <summary>Tizen device profile, e.g. <c>mobile</c> or <c>tv</c>.</summary>
    public string Profile { get; }

    /// <summary>The value written to the <c>platform</c> field.</summary>
    public string ReportedPlatform => Reporting == TizenPlatformReporting.SchemaCompatible
        ? SchemaCompatiblePlatformName
        : TizenPlatformName;

    /// <summary>True when <see cref="ReportedPlatform"/> is a member of the published spec enum.</summary>
    public bool ReportedPlatformIsSchemaValid =>
        SpecPlatformValues.Contains(ReportedPlatform, StringComparer.Ordinal);

    /// <summary>
    /// DevFlow's <c>idiom</c> for this profile. TV maps to <c>tv</c>, wearable to <c>watch</c>, and
    /// everything else to a phone-shaped idiom.
    /// </summary>
    public string Idiom => Profile switch
    {
        TizenDeviceProfiles.Tv => "tv",
        TizenDeviceProfiles.Wearable => "watch",
        _ => "phone",
    };

    /// <summary>Extension fields merged into the status payload, if any.</summary>
    public IReadOnlyDictionary<string, string> StatusExtensions =>
        Reporting == TizenPlatformReporting.SchemaCompatible
            ? new Dictionary<string, string>(StringComparer.Ordinal) { [PlatformExtensionKey] = TizenPlatformName }
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The <c>platform</c> enum as published by maui-labs at the pinned DevFlow version.</summary>
    public static IReadOnlyList<string> SpecPlatformValues { get; } =
        ["ios", "android", "maccatalyst", "windows", "linux", "macos"];
}

/// <summary>Tizen device profile identifiers.</summary>
public static class TizenDeviceProfiles
{
    public const string Mobile = "mobile";

    public const string Tv = "tv";

    public const string Wearable = "wearable";

    public static IReadOnlyList<string> All { get; } = [Mobile, Tv, Wearable];
}
