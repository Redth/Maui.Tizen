using Microsoft.Maui.Hosting;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>
/// Registers the Tizen DevFlow agent with a MAUI app host.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>AddMauiDevFlowAgent</c> to match every other platform backend, so
/// <c>MauiProgram.cs</c> is identical across platforms:
/// </para>
/// <code>
/// #if DEBUG
///     builder.AddMauiDevFlowAgent();
/// #endif
/// </code>
/// <para>
/// Registration goes through <see cref="DevFlowAgentHost.Configure"/> and
/// <c>DevFlowAgentHostContext.AttachTo</c>, which is the current path used by the shipped backends.
/// It deliberately does not re-implement broker registration: <see cref="DevFlowAgentService"/>
/// already owns that, and duplicating it produces two registrations that race to bind the port.
/// </para>
/// </remarks>
public static class TizenAgentServiceExtensions
{
    /// <summary>Adds the Tizen DevFlow agent.</summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder) =>
        builder.AddMauiDevFlowAgent(static _ => { });

    /// <summary>Adds the Tizen DevFlow agent with configuration.</summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AgentOptions();
        configure(options);

        // Register the on-device convention route through DevFlow's supported extension mechanism.
        // AgentExtension.MapPost is a real API (DevFlowContractTests pins it) and the agent wires
        // these routes at startup, so this is not an endpoint invented by the harness.
        var extension = options.RegisterExtension(
            TizenDevFlowConventions.Namespace,
            TizenDevFlowConventions.Description,
            TizenDevFlowConventions.Version,
            TizenDevFlowConventions.Features);

        extension.MapPost(TizenDevFlowConventions.RunRoute, async _ =>
        {
            var provider = ConventionAssertionProviderRegistry.Current;

            if (provider is null)
            {
                // 501 rather than an empty pass: an app that cannot self-assert must fail the
                // device lane, not look like a clean run.
                return HttpResponse.Error(
                    "No convention assertion provider is registered. The application under test must "
                    + "register one via ConventionAssertionProviderRegistry.Register(...); see "
                    + "samples/Maui.Tizen.Catalog/README.md.",
                    501,
                    DevFlowAgentService.PlatformErrorReasonNotSupported,
                    null);
            }

            var report = await provider.RunAsync().ConfigureAwait(false);
            return HttpResponse.Json(report.ToPayload());
        });

        if (!options.Enabled)
            return builder;

        var environment = TizenDeviceEnvironment.Detect();
        var identity = new TizenPlatformIdentity(TizenPlatformReporting.Accurate, environment.Profile);

        var context = DevFlowAgentHost.Configure(
            options,
            () => (identity.ReportedPlatform, identity.Profile),
            message => global::Tizen.Log.Info(LogTag, message));

        var service = new TizenAgentService(options, environment);
        context.AttachTo(service, options);

        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<DevFlowAgentService>(service);

        return builder;
    }

    const string LogTag = "MauiTizenDevFlow";
}
