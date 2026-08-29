using System.Text.Json;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Typed access to <c>eng/validation/api15-contract.json</c>.
/// </summary>
public static class Api15Contract
{
    static readonly Lazy<Api15Document> DocumentLazy = new(Load);

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Path => RepoLayout.Combine("eng", "validation", "api15-contract.json");

    public static bool Exists => File.Exists(Path);

    public static Api15Document Document => DocumentLazy.Value;

    static Api15Document Load()
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException($"Missing API15 contract at {Path}.", Path);

        return JsonSerializer.Deserialize<Api15Document>(File.ReadAllText(Path), SerializerOptions)
               ?? throw new InvalidOperationException($"'{Path}' deserialized to null.");
    }
}

public sealed class Api15Document
{
    public int SchemaVersion { get; init; }

    public string ApiLevel { get; init; } = string.Empty;

    public IReadOnlyList<BannedSymbol> BannedSymbols { get; init; } = [];

    public IReadOnlyList<UnsupportedService> UnsupportedServices { get; init; } = [];

    public IReadOnlyList<CompatibilityShim> CompatibilityShims { get; init; } = [];
}

/// <summary>A symbol that compiled source must not reference on API15.</summary>
public sealed class BannedSymbol
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Dotted names or type members, e.g. <c>Tizen.Maps</c>, <c>Window.Instance</c>.</summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>
    /// Identifiers that must not be flagged even though they begin with a banned symbol, e.g.
    /// <c>MapServiceToken</c> against a <c>MapService</c> ban.
    /// </summary>
    public IReadOnlyList<string> AllowedIdentifiers { get; init; } = [];

    public string Reason { get; init; } = string.Empty;

    /// <summary>Suggested replacement, when one exists.</summary>
    public string? Replacement { get; init; }

    /// <summary>Reference-pack assembly this rule is derived from, when applicable.</summary>
    public string? ReferencePackAssembly { get; init; }

    /// <summary>Whether <see cref="ReferencePackAssembly"/> should be present in the pack.</summary>
    public bool? ExpectedInReferencePack { get; init; }

    /// <summary>Type carrying the obsolete member, when the rule is a deprecation.</summary>
    public string? ReferencePackType { get; init; }

    public string? ObsoleteMember { get; init; }

    public string? ReplacementMember { get; init; }
}

public sealed class UnsupportedService
{
    public string Contract { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    /// <summary>What the implementation does when called.</summary>
    public string Behaviour { get; init; } = string.Empty;

    /// <summary>True when the service must not be registered in the container at all.</summary>
    public bool DoNotRegisterInDi { get; init; }
}

/// <summary>A member kept for source/startup compatibility that no longer does anything.</summary>
public sealed class CompatibilityShim
{
    public string Member { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public bool IsAcceptedNoOp =>
        string.Equals(Status, "accepted-no-op", StringComparison.OrdinalIgnoreCase);
}
