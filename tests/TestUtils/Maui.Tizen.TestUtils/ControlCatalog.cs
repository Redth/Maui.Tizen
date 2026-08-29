using System.Text.Json;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Typed access to <c>samples/Maui.Tizen.Catalog/catalog-manifest.json</c>.
/// </summary>
/// <remarks>
/// The catalog manifest is the join point between the sample app, the visual-baseline inventory and
/// the input/focus test plan. Keeping it machine-readable is what allows a test to assert that a
/// checked-in baseline still corresponds to a real catalog case, instead of accumulating orphaned
/// images nobody dares delete.
/// </remarks>
public static class ControlCatalog
{
    static readonly Lazy<CatalogManifest> ManifestLazy = new(Load);

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool Exists => File.Exists(RepoLayout.CatalogManifestFile);

    public static CatalogManifest Manifest => ManifestLazy.Value;

    public static IReadOnlyList<CatalogCase> Cases => Manifest.Cases;

    /// <summary>Cases that should produce a visual baseline for <paramref name="profileId"/>.</summary>
    public static IReadOnlyList<CatalogCase> BaselineCasesFor(string profileId) =>
        [.. Cases.Where(c => c.CapturesBaseline && c.AppliesTo(profileId))];

    /// <summary>Cases requiring remote/D-pad focus traversal coverage.</summary>
    public static IReadOnlyList<CatalogCase> FocusNavigationCases() =>
        [.. Cases.Where(c => c.Focusable && c.Interactions.Contains("remote-navigate", StringComparer.Ordinal))];

    static CatalogManifest Load()
    {
        var path = RepoLayout.CatalogManifestFile;

        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing catalog manifest at {path}.", path);

        return JsonSerializer.Deserialize<CatalogManifest>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
    }
}

public sealed class CatalogManifest
{
    public int SchemaVersion { get; init; }

    public CatalogInteractions Interactions { get; init; } = new();

    public IReadOnlyList<CatalogCase> Cases { get; init; } = [];
}

public sealed class CatalogInteractions
{
    /// <summary>Closed vocabulary of interaction verbs.</summary>
    public IReadOnlyList<string> Allowed { get; init; } = [];
}

public sealed class CatalogCase
{
    /// <summary>Stable slug; also the baseline image file name.</summary>
    public string Id { get; init; } = string.Empty;

    public string Control { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Device profiles this case is exercised on.</summary>
    public IReadOnlyList<string> Profiles { get; init; } = [];

    public bool CapturesBaseline { get; init; }

    public IReadOnlyList<string> Interactions { get; init; } = [];

    /// <summary>True when the case participates in focus traversal.</summary>
    public bool Focusable { get; init; }

    public bool AppliesTo(string profileId) =>
        Profiles.Contains(profileId, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => Id;
}
