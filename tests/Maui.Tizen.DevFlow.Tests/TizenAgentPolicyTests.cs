namespace Maui.Tizen.DevFlow.Tests;

/// <summary>
/// Behaviour of the platform-neutral half of the Tizen agent.
/// </summary>
/// <remarks>
/// These are the agent decisions that must be right regardless of hardware: what gets advertised as
/// supported, how privileges gate native input, and how the driver reaches the device. Keeping them
/// out of the Tizen-targeted assembly is what makes them testable at all today.
/// </remarks>
public class TizenAgentPolicyTests
{
    static TizenAgentEnvironment Environment(
        string profile = TizenDeviceProfiles.Mobile,
        bool inputGenerator = false,
        bool hasWindow = true,
        bool supportsCapture = true,
        bool supportsResize = true) =>
        new()
        {
            Profile = profile,
            HasWindow = hasWindow,
            SupportsCapture = supportsCapture,
            SupportsWindowResize = supportsResize,
            GrantedPrivileges = inputGenerator
                ? [TizenPrivileges.Internet, TizenPrivileges.Display, TizenPrivileges.InputGenerator]
                : [TizenPrivileges.Internet, TizenPrivileges.Display],
        };

    [Fact]
    public void NativeInput_IsNotAdvertisedWithoutTheInputGeneratorPrivilege()
    {
        var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: false));

        var nativeInput = capabilities[TizenAgentCapabilityPolicy.Keys.NativeInput];

        Assert.False(nativeInput.Supported);
        Assert.Contains(TizenPrivileges.InputGenerator, nativeInput.UnsupportedReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeInput_IsAdvertisedWhenThePrivilegeIsGranted()
    {
        var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: true));

        Assert.True(capabilities[TizenAgentCapabilityPolicy.Keys.NativeInput].Supported);
    }

    [Fact]
    public void FrameworkLevelInteraction_RemainsAvailableWithoutThePrivilege()
    {
        // Losing synthesised input must not disable ordinary tap/fill; otherwise a device without
        // the privilege looks completely broken rather than merely less capable.
        var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: false));

        Assert.True(capabilities[TizenAgentCapabilityPolicy.Keys.Tap].Supported);
        Assert.True(capabilities[TizenAgentCapabilityPolicy.Keys.Fill].Supported);
        Assert.True(capabilities[TizenAgentCapabilityPolicy.Keys.Focus].Supported);
    }

    [Fact]
    public void WithoutAWindow_UiCapabilitiesAreUnsupported()
    {
        var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(hasWindow: false));

        Assert.False(capabilities[TizenAgentCapabilityPolicy.Keys.UiTree].Supported);
        Assert.False(capabilities[TizenAgentCapabilityPolicy.Keys.Screenshot].Supported);
        Assert.False(capabilities[TizenAgentCapabilityPolicy.Keys.Tap].Supported);
    }

    [Fact]
    public void WhenCaptureIsUnavailable_ScreenshotIsUnsupportedButTheTreeStillWorks()
    {
        var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(supportsCapture: false));

        Assert.False(capabilities[TizenAgentCapabilityPolicy.Keys.Screenshot].Supported);
        Assert.True(capabilities[TizenAgentCapabilityPolicy.Keys.UiTree].Supported);
    }

    [Fact]
    public void ThemeSwitchingIsAlwaysUnsupportedWithAReason()
    {
        var theme = TizenAgentCapabilityPolicy.Compute(Environment())[TizenAgentCapabilityPolicy.Keys.Theme];

        Assert.False(theme.Supported);
        Assert.False(string.IsNullOrWhiteSpace(theme.UnsupportedReason));
    }

    [Fact]
    public void EveryUnsupportedCapabilityCarriesAReason()
    {
        // DevFlow answers unsupported capabilities with HTTP 501 and a reason. A blank reason
        // reaches the driver as an unexplained failure.
        foreach (var environment in new[]
                 {
                     Environment(),
                     Environment(hasWindow: false),
                     Environment(supportsCapture: false),
                     Environment(profile: TizenDeviceProfiles.Tv, supportsResize: false),
                 })
        {
            foreach (var capability in TizenAgentCapabilityPolicy.Compute(environment).Values.Where(c => !c.Supported))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(capability.UnsupportedReason),
                    $"Capability '{capability.Key}' is unsupported without a reason.");
            }
        }
    }

    [Fact]
    public void Payload_ShapeMatchesWhatDevFlowExpects()
    {
        var payload = TizenAgentCapabilityPolicy.ToPayload(
            TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: false)));

        var supported = Assert.IsType<Dictionary<string, object>>(payload[TizenAgentCapabilityPolicy.Keys.Tap]);
        Assert.Equal(true, supported["supported"]);
        Assert.False(supported.ContainsKey("reason"));

        var unsupported = Assert.IsType<Dictionary<string, object>>(payload[TizenAgentCapabilityPolicy.Keys.NativeInput]);
        Assert.Equal(false, unsupported["supported"]);
        Assert.False(string.IsNullOrWhiteSpace((string)unsupported["reason"]));
    }

    [Theory]
    [InlineData(TizenDeviceProfiles.Mobile, "phone")]
    [InlineData(TizenDeviceProfiles.Tv, "tv")]
    [InlineData(TizenDeviceProfiles.Wearable, "watch")]
    public void Idiom_FollowsTheDeviceProfile(string profile, string expectedIdiom) =>
        Assert.Equal(expectedIdiom, new TizenPlatformIdentity(profile: profile).Idiom);

    [Fact]
    public void AccurateReporting_SaysTizenAndIsKnowinglyOutOfSpec()
    {
        var identity = new TizenPlatformIdentity(TizenPlatformReporting.Accurate);

        Assert.Equal("tizen", identity.ReportedPlatform);
        Assert.False(identity.ReportedPlatformIsSchemaValid);
        Assert.Empty(identity.StatusExtensions);
    }

    [Fact]
    public void SchemaCompatibleReporting_DowngradesToLinuxAndKeepsTheRealPlatform()
    {
        var identity = new TizenPlatformIdentity(TizenPlatformReporting.SchemaCompatible);

        Assert.Equal("linux", identity.ReportedPlatform);
        Assert.True(identity.ReportedPlatformIsSchemaValid);
        Assert.Equal("tizen", identity.StatusExtensions[TizenPlatformIdentity.PlatformExtensionKey]);
    }
}

/// <summary>How an external driver reaches an agent on a device.</summary>
public class TizenAgentConnectionTests
{
    [Fact]
    public void DefaultsToDevFlowsPublishedPortOnBothSides()
    {
        var connection = new TizenAgentConnection();

        Assert.Equal(9223, connection.DevicePort);
        Assert.Equal(9223, connection.HostPort);
        Assert.Equal(new Uri("http://127.0.0.1:9223/"), connection.BaseAddress);
    }

    [Fact]
    public void ForwardArguments_TunnelHostPortToDevicePort()
    {
        var connection = new TizenAgentConnection(devicePort: 9223, hostPort: 9300);

        Assert.Equal(["forward", "tcp:9300", "tcp:9223"], connection.BuildForwardArguments());
    }

    [Fact]
    public void ForwardArguments_TargetASpecificDeviceWhenSerialIsKnown()
    {
        var connection = new TizenAgentConnection("emulator-26101", hostPort: 9300);

        Assert.Equal(
            ["-s", "emulator-26101", "forward", "tcp:9300", "tcp:9223"],
            connection.BuildForwardArguments());
    }

    [Fact]
    public void RemoveArguments_TearTheTunnelDownByHostPort()
    {
        // Teardown must be deterministic; a leaked forward silently captures the next job's traffic.
        var connection = new TizenAgentConnection("emulator-26101", hostPort: 9300);

        Assert.Equal(
            ["-s", "emulator-26101", "forward", "--remove", "tcp:9300"],
            connection.BuildForwardRemoveArguments());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void RejectsPortsOutsideTheValidRange(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TizenAgentConnection(devicePort: port));
}
