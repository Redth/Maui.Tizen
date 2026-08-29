using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maui.Tizen.TestUtils;

/// <summary>One addressable rendering context for a baseline image.</summary>
/// <param name="Profile">Device profile id, e.g. <c>mobile</c> or <c>tv</c>.</param>
/// <param name="Theme">e.g. <c>light</c> or <c>dark</c>.</param>
/// <param name="Density">e.g. <c>hdpi</c>, <c>uhd</c>.</param>
public sealed record BaselineVariant(string Profile, string Theme, string Density)
{
    public override string ToString() => $"{Profile}/{Theme}/{Density}";
}

/// <summary>
/// Addresses visual baselines on disk and describes the metadata captured alongside them.
/// </summary>
/// <remarks>
/// <para>
/// The layout is
/// <c>tests/VisualBaselines/{profile}/{apiLevel}/{theme}/{density}/{caseId}.png</c> with a sibling
/// <c>{caseId}.json</c>.
/// </para>
/// <para>
/// Every path segment exists because it changes pixels: profile changes the toolkit's default
/// metrics, API level changes platform styling between Tizen releases, theme changes palette, and
/// density changes rasterisation. Flattening any of them forces a single image to represent several
/// legitimately different renderings, which is how baseline suites end up "temporarily" disabled.
/// </para>
/// </remarks>
public static class VisualBaselines
{
    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Directory holding baselines for a variant at a given API level.</summary>
    public static string DirectoryFor(BaselineVariant variant, string apiLevel) =>
        Path.Combine(RepoLayout.VisualBaselineRoot, variant.Profile, apiLevel, variant.Theme, variant.Density);

    /// <summary>Absolute path of a baseline image.</summary>
    public static string ImagePath(BaselineVariant variant, string apiLevel, string caseId) =>
        Path.Combine(DirectoryFor(variant, apiLevel), caseId + ".png");

    /// <summary>Absolute path of a baseline's metadata sidecar.</summary>
    public static string MetadataPath(BaselineVariant variant, string apiLevel, string caseId) =>
        Path.Combine(DirectoryFor(variant, apiLevel), caseId + ".json");

    /// <summary>Enumerates every baseline image currently checked in.</summary>
    public static IReadOnlyList<string> EnumerateImages()
    {
        if (!Directory.Exists(RepoLayout.VisualBaselineRoot))
            return [];

        return [.. Directory
            .EnumerateFiles(RepoLayout.VisualBaselineRoot, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Parses a baseline image path back into its addressing components.
    /// </summary>
    /// <returns><see langword="false"/> when the path does not follow the convention.</returns>
    public static bool TryParsePath(string imagePath, out BaselineAddress address)
    {
        address = default!;

        var relative = Path.GetRelativePath(RepoLayout.VisualBaselineRoot, imagePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // profile / apiLevel / theme / density / caseId.png
        if (segments.Length != 5 || !segments[4].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;

        address = new BaselineAddress(
            new BaselineVariant(segments[0], segments[2], segments[3]),
            segments[1],
            Path.GetFileNameWithoutExtension(segments[4]));

        return true;
    }

    public static BaselineMetadata ReadMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException($"Missing baseline metadata: {metadataPath}", metadataPath);

        return JsonSerializer.Deserialize<BaselineMetadata>(File.ReadAllText(metadataPath), SerializerOptions)
               ?? throw new InvalidOperationException($"'{metadataPath}' deserialized to null.");
    }

    public static void WriteMetadata(string metadataPath, BaselineMetadata metadata)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(metadataPath))!);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SerializerOptions) + "\n");
    }
}

/// <param name="ApiLevel">Tizen API level the capture came from, e.g. <c>API15</c>.</param>
public sealed record BaselineAddress(BaselineVariant Variant, string ApiLevel, string CaseId)
{
    public override string ToString() => $"{Variant.Profile}/{ApiLevel}/{Variant.Theme}/{Variant.Density}/{CaseId}";
}

/// <summary>
/// Provenance recorded next to every baseline image.
/// </summary>
/// <remarks>
/// Without this, a stale baseline is indistinguishable from a correct one, and the only way to
/// judge a diff is to re-capture and eyeball it. Recording what produced the pixels makes a
/// baseline reviewable and lets the suite reject captures taken under the wrong conditions.
/// </remarks>
public sealed class BaselineMetadata
{
    /// <summary>Catalog case this image belongs to.</summary>
    public string CaseId { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string ApiLevel { get; init; } = string.Empty;

    public string Theme { get; init; } = string.Empty;

    public string Density { get; init; } = string.Empty;

    /// <summary>TFM the app under test was built with, e.g. <c>net11.0-tizen11.0</c>.</summary>
    public string TargetFramework { get; init; } = string.Empty;

    /// <summary>Emulator or device image identifier; must never contain a hostname or account.</summary>
    public string DeviceImage { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Repository commit the capture was taken at.</summary>
    public string Commit { get; init; } = string.Empty;

    /// <summary>UTC capture timestamp, ISO-8601.</summary>
    public string CapturedUtc { get; init; } = string.Empty;

    /// <summary>Optional per-case tolerance override.</summary>
    public BaselineTolerance? Tolerance { get; init; }

    /// <summary>Why this baseline deviates from the default tolerance, when it does.</summary>
    public string? ToleranceJustification { get; init; }
}
