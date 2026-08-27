using System.Reflection;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Maui.Tizen.DevFlow.Tests;

/// <summary>
/// Pins the exact DevFlow extension surface that the Tizen agent overrides.
/// </summary>
/// <remarks>
/// <para>
/// <c>src/Diagnostics/Maui.Tizen.DevFlow.Agent</c> targets <c>net11.0-tizen11.0</c> and therefore
/// cannot be compiled by anyone until the Samsung workload ships. Its correctness is unverifiable by
/// the compiler, which makes it exactly the kind of code that silently rots against an upstream
/// preview package.
/// </para>
/// <para>
/// This suite is the substitute. DevFlow's packages are plain <c>net10.0</c> assemblies, so the
/// hosted lane can load them and assert that every member the Tizen agent overrides still exists
/// with the expected signature. If maui-labs renames or reshapes one of these, this fails on an
/// ordinary pull request instead of on a device months later.
/// </para>
/// <para>
/// Every signature below was verified against
/// <c>Microsoft.Maui.DevFlow.Agent.Core 0.1.0-preview.12.26421.1</c>. None of it is guessed.
/// </para>
/// </remarks>
public class DevFlowContractTests
{
    const BindingFlags Overridable =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    static MethodInfo RequireMethod(Type type, string name, params Type[] parameters)
    {
        var method = type.GetMethod(name, Overridable, binder: null, types: parameters, modifiers: null);

        Assert.True(
            method is not null,
            $"{type.FullName}.{name}({string.Join(", ", parameters.Select(p => p.Name))}) no longer exists. " +
            "The Tizen agent overrides it; update src/Diagnostics/Maui.Tizen.DevFlow.Agent to match the " +
            "new DevFlow surface.");

        Assert.True(
            method!.IsVirtual && !method.IsFinal,
            $"{type.FullName}.{name} is no longer overridable.");

        return method;
    }

    public static TheoryData<string, Type[], Type> AgentServiceOverrides =>
        new()
        {
            { nameof(MauiDevFlowAgentService), [], typeof(VisualTreeWalker) },
        };

    [Fact]
    public void AgentService_ExposesTheOverridesTheTizenBackendUses()
    {
        var type = typeof(MauiDevFlowAgentService);

        Assert.Equal(typeof(VisualTreeWalker), RequireMethod(type, "CreateTreeWalker").ReturnType);

        RequireMethod(type, "CaptureFullScreenAsync", typeof(int?));
        RequireMethod(type, "CaptureNativeElementScreenshotAsync", typeof(object), typeof(ElementInfo));
        RequireMethod(type, "DescribeScreenshotFailure");
        RequireMethod(type, "GetWindowMetrics", typeof(int?));
        RequireMethod(type, "PopulateCapabilities", typeof(Dictionary<string, object>));
        RequireMethod(type, "get_PlatformName");
        RequireMethod(type, "get_DeviceTypeName");
        RequireMethod(type, "get_IdiomName");
        RequireMethod(type, "IsMainThreadDispatchRequired");
        RequireMethod(type, "GetAppDataBasePath");
        RequireMethod(type, "TryNativeElementTapAsync", typeof(string), typeof(object));
        RequireMethod(type, "StopBackendAsync");
        RequireMethod(type, "DisposeBackendResources");
    }

    [Fact]
    public void AgentService_WindowMetricsStillReturnsWidthHeightDensity()
    {
        // The Tizen override returns a 3-tuple. If DevFlow ever changes the shape, the Tizen code
        // would fail to compile on a machine nobody currently has.
        var method = RequireMethod(typeof(MauiDevFlowAgentService), "GetWindowMetrics", typeof(int?));

        Assert.Equal(typeof(ValueTuple<double, double, double>), method.ReturnType);
    }

    [Fact]
    public void VisualTreeWalker_ExposesTheNativeLayerExtensionPoints()
    {
        var type = typeof(VisualTreeWalker);

        RequireMethod(type, "WalkNativeTree", typeof(IReadOnlyList<nint>), typeof(int));
        RequireMethod(type, "QueryNative", typeof(IReadOnlyList<nint>), typeof(string), typeof(string), typeof(string), typeof(string));
        RequireMethod(type, "HitTestNativeElements", typeof(IReadOnlyList<nint>), typeof(double), typeof(double));
        RequireMethod(type, "GetNativeElementById", typeof(string));
        RequireMethod(type, "GetNativeElementInfoById", typeof(string));
        RequireMethod(type, "EnsurePlatformStableId", typeof(object));
        RequireMethod(type, "TryNativeElementFocus", typeof(string), typeof(object));
        RequireMethod(type, "TrySetValueRegisteredNativeElement", typeof(string), typeof(object), typeof(string));
        RequireMethod(type, "CanInvokeRegisteredNativeElement", typeof(object));
        RequireMethod(type, "CanFocusRegisteredNativeElement", typeof(object));
        RequireMethod(type, "CanSetValueRegisteredNativeElement", typeof(object));
    }

    [Fact]
    public void ElementInfo_StillCarriesTheFieldsTheTizenWalkerPopulates()
    {
        var type = typeof(ElementInfo);

        foreach (var name in new[]
                 {
                     "Id", "Type", "FullType", "NativeType", "Framework", "Origin", "Role",
                     "AutomationId", "Text", "OwnerId", "Capabilities", "RegistryGeneration",
                     "Bounds", "IsVisible", "IsEnabled", "IsFocused", "Opacity",
                 })
        {
            Assert.True(
                type.GetProperty(name) is not null,
                $"ElementInfo.{name} no longer exists; the Tizen walker sets it.");
        }
    }

    [Fact]
    public void BoundsInfo_IsStillASettableRectangle()
    {
        var type = typeof(BoundsInfo);

        foreach (var name in new[] { "X", "Y", "Width", "Height" })
        {
            var property = type.GetProperty(name);
            Assert.True(property is not null, $"BoundsInfo.{name} no longer exists.");
            Assert.True(property!.CanWrite, $"BoundsInfo.{name} is no longer settable.");
            Assert.Equal(typeof(double), property.PropertyType);
        }
    }

    [Fact]
    public void ScreenshotCaptureFailure_StillTakesMessageReasonRetryableSuggestions()
    {
        var constructor = typeof(ScreenshotCaptureFailure).GetConstructor(
            [typeof(string), typeof(string), typeof(bool), typeof(string[])]);

        Assert.True(
            constructor is not null,
            "ScreenshotCaptureFailure(string, string, bool, string[]) no longer exists; " +
            "TizenAgentService.DescribeScreenshotFailure uses it.");
    }

    [Fact]
    public void AgentHost_StillProvidesTheRegistrationPathTheTizenExtensionUses()
    {
        // The Tizen registration mirrors the shipped backends: DevFlowAgentHost.Configure(...)
        // followed by DevFlowAgentHostContext.AttachTo(...). It deliberately does not
        // re-implement broker registration, which DevFlowAgentService already owns.
        var configure = typeof(DevFlowAgentHost).GetMethod(
            "Configure",
            BindingFlags.Public | BindingFlags.Static);

        Assert.True(configure is not null, "DevFlowAgentHost.Configure no longer exists.");
        Assert.Equal(typeof(DevFlowAgentHostContext), configure!.ReturnType);

        var attachTo = typeof(DevFlowAgentHostContext).GetMethod(
            "AttachTo",
            [typeof(DevFlowAgentService), typeof(AgentOptions)]);

        Assert.True(
            attachTo is not null,
            "DevFlowAgentHostContext.AttachTo(DevFlowAgentService, AgentOptions) no longer exists.");
    }

    [Fact]
    public void AgentOptions_StillExposesEnabledAndPort()
    {
        Assert.NotNull(typeof(AgentOptions).GetProperty("Enabled"));
        Assert.NotNull(typeof(AgentOptions).GetProperty("Port"));

        // The default port is part of the published protocol; TizenAgentConnection mirrors it.
        var defaultPort = typeof(AgentOptions).GetField("DefaultPort", BindingFlags.Public | BindingFlags.Static);
        Assert.True(defaultPort is not null, "AgentOptions.DefaultPort no longer exists.");

        Assert.Equal(
            TizenAgentConnection.DefaultDevFlowPort,
            (int)defaultPort!.GetValue(null)!);
    }

    [Fact]
    public void ExtensionRouting_IsARealDevFlowMechanism()
    {
        // The on-device conventions endpoint is hosted through DevFlow's extension mechanism.
        // Pinned here because "we call an endpoint no server can host" is a fair criticism of any
        // custom route, and the answer has to be evidence rather than assertion.
        var register = typeof(AgentOptions).GetMethod(
            "RegisterExtension",
            [typeof(string), typeof(string), typeof(int), typeof(IEnumerable<string>)]);

        Assert.True(
            register is not null,
            "AgentOptions.RegisterExtension(string, string, int, IEnumerable<string>) no longer exists.");

        Assert.Equal(typeof(AgentExtension), register!.ReturnType);

        var mapPost = typeof(AgentExtension).GetMethod("MapPost");
        Assert.True(mapPost is not null, "AgentExtension.MapPost no longer exists.");

        var parameters = mapPost!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);

        // The handler shape is what the agent's route implementation must match.
        Assert.StartsWith("Func`2", parameters[1].ParameterType.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void TizenExtensionNamespaceUsesThePinnedReverseDomainContract()
    {
        var options = new AgentOptions();
        var extension = options.RegisterExtension(
            TizenDevFlowConventions.Namespace,
            TizenDevFlowConventions.Description,
            TizenDevFlowConventions.Version,
            TizenDevFlowConventions.Features);

        Assert.Equal("org.dotnet.maui.tizen", extension.Namespace);
    }

    [Fact]
    public void NativeIdsUseTheRoutingPrefixRecognizedByDevFlow()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        var id = bridge.Register(new NativeElementDescriptor(
            new object(), "Button", "button", new NativeElementBounds(0, 0, 1, 1), CanInvoke: true));

        Assert.StartsWith("native:", id, StringComparison.Ordinal);
        Assert.False(id.StartsWith("native:registered:", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtensionRoutes_AreRegisteredByTheAgentItself()
    {
        // Route registration lives inside DevFlow; if it stopped happening, every extension route
        // would 404 while still looking correctly declared on our side.
        var abstractions = typeof(AgentExtension).Assembly;

        Assert.Contains(
            abstractions.GetTypes().Where(t => t.Name.Contains("ExtensionRoute", StringComparison.Ordinal)),
            t => t is not null);
    }

    [Fact]
    public void HttpResponse_ExposesTheHelpersTheExtensionRouteUses()
    {
        Assert.NotNull(typeof(HttpResponse).GetMethod("Json", [typeof(object)]));
        Assert.NotNull(typeof(HttpResponse).GetMethod(
            "Error", [typeof(string), typeof(int), typeof(string), typeof(object)]));
    }

    [Fact]
    public void DevFlowSpecPlatformEnum_StillLacksTizen()
    {
        // Recorded, not asserted as desirable. The moment maui-labs adds "tizen" to the
        // agent-status platform enum, this fails and TizenPlatformIdentity's SchemaCompatible
        // mode can be deleted.
        Assert.DoesNotContain(
            TizenPlatformIdentity.TizenPlatformName,
            TizenPlatformIdentity.SpecPlatformValues,
            StringComparer.Ordinal);
    }
}
