namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// The contract for running convention assertions inside the application under test.
/// </summary>
/// <remarks>
/// <para>
/// Mapper parity, DI registration and Essentials coverage all read runtime state that only exists
/// while the Tizen backend is executing, so they cannot run on a controller or a hosted runner.
/// They run in-app and report back over DevFlow.
/// </para>
/// <para>
/// DevFlow hosts this through its extension mechanism: <c>AgentOptions.RegisterExtension</c> returns
/// an <c>AgentExtension</c> exposing <c>MapGet</c>/<c>MapPost</c>, and the agent registers those
/// routes at startup (<c>RegisterExtensionRoutes</c>). This is a supported extension point, not an
/// invented endpoint - <c>DevFlowContractTests</c> pins the exact signatures used.
/// </para>
/// <para>
/// The agent registers the route; the <em>application</em> supplies the assertions by registering an
/// <see cref="IConventionAssertionProvider"/>. Until one exists the route answers 501 rather than
/// pretending to pass, so a device lane wired to an app that cannot self-assert fails loudly.
/// </para>
/// </remarks>
public interface IConventionAssertionProvider
{
    /// <summary>Runs the in-app convention assertions.</summary>
    Task<ConventionAssertionReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <param name="Total">Assertions executed. Zero is treated as a failure by the device lane.</param>
/// <param name="Failed">Human-readable descriptions of failures.</param>
/// <param name="Skipped">Assertions that could not run, with reasons.</param>
public sealed record ConventionAssertionReport(
    int Total,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Skipped)
{
    public static ConventionAssertionReport Empty { get; } = new(0, [], []);

    /// <summary>
    /// True only when assertions actually ran and none failed.
    /// </summary>
    /// <remarks>
    /// A report of zero assertions is not a pass. It is indistinguishable from a run that never
    /// happened, which is exactly the failure mode the device lane exists to catch.
    /// </remarks>
    public bool Passed => Total > 0 && Failed.Count == 0;

    /// <summary>Shape returned by the extension route and consumed by the device lane.</summary>
    public Dictionary<string, object> ToPayload() => new(StringComparer.Ordinal)
    {
        ["total"] = Total,
        ["failed"] = Failed,
        ["skipped"] = Skipped,
        ["passed"] = Passed,
    };
}

/// <summary>
/// Identity of the Tizen convention extension.
/// </summary>
/// <remarks>
/// The namespace, route and feature names live here as constants so the agent that registers the
/// route and the harness that calls it cannot drift apart. The harness still discovers the URL from
/// the agent's advertised capabilities rather than composing it from these values, because the
/// prefix DevFlow uses for extension routes is its own concern.
/// </remarks>
public static class TizenDevFlowConventions
{
    /// <summary>Extension namespace registered with DevFlow.</summary>
    public const string Namespace = "maui-tizen";

    public const string Description = "Maui.Tizen on-device convention assertions.";

    public const int Version = 1;

    /// <summary>Route mapped on the extension, relative to DevFlow's extension prefix.</summary>
    public const string RunRoute = "/conventions/run";

    /// <summary>Feature advertised so a driver can detect support before calling.</summary>
    public const string ConventionsFeature = "conventions";

    public static IReadOnlyList<string> Features { get; } = [ConventionsFeature];

    /// <summary>Capability key reporting whether an application-supplied provider is present.</summary>
    public const string ProviderCapabilityKey = "maui-tizen.conventions";
}
