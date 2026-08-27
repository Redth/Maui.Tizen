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

/// <summary>
/// Activation semantics: a tap must never report success for something it merely focused.
/// </summary>
public class NativeActivationPolicyTests
{
    static NativeActivationDecision Decide(
        bool hasElement = true,
        bool isButton = false,
        bool isFocusable = false,
        string typeName = "MauiView",
        bool syntheticInputAvailable = false) =>
        NativeActivationPolicy.Decide(hasElement, isButton, isFocusable, typeName, syntheticInputAvailable);

    [Fact]
    public void NativeButton_CanBeActivated()
    {
        var decision = Decide(isButton: true, typeName: "MauiButton");

        Assert.True(decision.CanActivate);
        Assert.Equal(NativeActivationOutcome.Activate, decision.Outcome);
        Assert.Null(decision.Reason);
    }

    public class AgentLifecycleStartupTests
    {
        sealed class TestApplication;

        [Fact]
        public void FirstActiveLifecycleEventStartsTheAgent()
        {
            var application = new TestApplication();
            var running = false;
            var bound = false;
            var starts = 0;
            var binds = 0;
            var startup = new AgentLifecycleStartup<TestApplication>(
                () => application,
                () => running,
                () => bound,
                _ =>
                {
                    starts++;
                    running = true;
                    bound = true;
                },
                _ =>
                {
                    binds++;
                    bound = true;
                });

            Assert.True(startup.OnApplicationActive());
            Assert.Equal(1, starts);
            Assert.Equal(0, binds);
        }

        public class NativeTapResultTests
        {
            [Fact]
            public void SuccessfulNativeActivationReturnsTheDevFlowHandledSentinel() =>
                Assert.Equal("ok", NativeTapResult.FromError(null));

            [Fact]
            public void NativeActivationErrorsRemainErrors() =>
                Assert.Equal("not activatable", NativeTapResult.FromError("not activatable"));
        }

        [Fact]
        public void ResumeRebindsARunningAppLessAgent()
        {
            var application = new TestApplication();
            var bound = false;
            var binds = 0;
            var startup = new AgentLifecycleStartup<TestApplication>(
                () => application,
                () => true,
                () => bound,
                _ => Assert.Fail("A running agent must not be started twice."),
                _ =>
                {
                    binds++;
                    bound = true;
                });

            Assert.True(startup.OnApplicationActive());
            Assert.Equal(1, binds);
        }

        [Fact]
        public void LifecycleWaitsUntilTheApplicationExists()
        {
            var startup = new AgentLifecycleStartup<TestApplication>(
                () => null,
                () => false,
                () => false,
                _ => Assert.Fail("No application is available."),
                _ => Assert.Fail("No application is available."));

            Assert.False(startup.OnApplicationActive());
        }
    }

    [Fact]
    public void FocusableNonButton_IsNotActivatable()
    {
        // The regression: focusing used to be treated as a successful tap. A driver would see
        // "tap succeeded" while the control's command never ran.
        var decision = Decide(isFocusable: true, typeName: "MauiEntry");

        Assert.False(decision.CanActivate);
        Assert.Equal(NativeActivationOutcome.NotActivatable, decision.Outcome);
        Assert.Contains("focusing is not activation", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFocusableNonButton_IsNotActivatable()
    {
        var decision = Decide(typeName: "MauiLabel");

        Assert.False(decision.CanActivate);
        Assert.Contains("MauiLabel", decision.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("focusing is not activation", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusableButton_ActivatesRatherThanFocusing()
    {
        // Being focusable must never downgrade a real activation path.
        Assert.True(Decide(isButton: true, isFocusable: true).CanActivate);
    }

    [Fact]
    public void MissingElement_IsReportedDistinctly()
    {
        var decision = Decide(hasElement: false);

        Assert.Equal(NativeActivationOutcome.NoElement, decision.Outcome);
        Assert.False(decision.CanActivate);
    }

    [Fact]
    public void AdviceReflectsWhetherSynthesisedInputIsAvailable()
    {
        // The verdict must not change with privilege - only the suggested way forward.
        var withPrivilege = Decide(isFocusable: true, syntheticInputAvailable: true);
        var withoutPrivilege = Decide(isFocusable: true, syntheticInputAvailable: false);

        Assert.False(withPrivilege.CanActivate);
        Assert.False(withoutPrivilege.CanActivate);

        Assert.Contains("real hit-testing", withPrivilege.Reason!, StringComparison.Ordinal);
        Assert.Contains(TizenPrivileges.InputGenerator, withoutPrivilege.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNonActivatableOutcomeCarriesAReason()
    {
        foreach (var decision in new[]
                 {
                     Decide(hasElement: false),
                     Decide(isFocusable: true),
                     Decide(),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        }
    }
}

/// <summary>Key injection is privileged, exactly like touch.</summary>
public class KeyCapabilityTests
{
    static TizenAgentEnvironment Environment(bool inputGenerator) =>
        new()
        {
            GrantedPrivileges = inputGenerator
                ? [TizenPrivileges.Internet, TizenPrivileges.InputGenerator]
                : [TizenPrivileges.Internet],
        };

    [Fact]
    public void KeyIsUnsupportedWithoutTheInputGeneratorPrivilege()
    {
        // Previously advertised on window presence alone, so the endpoint reported success and did
        // nothing - which reaches a driver as "pressed a key, nothing happened".
        var key = TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: false))[
            TizenAgentCapabilityPolicy.Keys.Key];

        Assert.False(key.Supported);
        Assert.Contains(TizenPrivileges.InputGenerator, key.UnsupportedReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyIsSupportedWithThePrivilege() =>
        Assert.True(
            TizenAgentCapabilityPolicy.Compute(Environment(inputGenerator: true))[
                TizenAgentCapabilityPolicy.Keys.Key].Supported);

    [Fact]
    public void KeyAndNativeInputAgree()
    {
        // The TV focus harness drives ui/actions/key; if these two ever disagreed, the capability
        // map would advertise one thing and the endpoint do another.
        foreach (var granted in new[] { true, false })
        {
            var capabilities = TizenAgentCapabilityPolicy.Compute(Environment(granted));

            Assert.Equal(
                capabilities[TizenAgentCapabilityPolicy.Keys.NativeInput].Supported,
                capabilities[TizenAgentCapabilityPolicy.Keys.Key].Supported);
        }
    }
}

/// <summary>The on-device convention protocol.</summary>
public class ConventionProtocolTests : IDisposable
{
    public void Dispose() => ConventionAssertionProviderRegistry.Clear();

    sealed class FakeProvider(ConventionAssertionReport report) : IConventionAssertionProvider
    {
        public Task<ConventionAssertionReport> RunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    [Fact]
    public void NoProviderIsRegisteredByDefault()
    {
        // The agent assembly must not supply assertions of its own; the app under test does. With a
        // default provider, a device lane pointed at a non-self-asserting app would look clean.
        Assert.False(ConventionAssertionProviderRegistry.HasProvider);
        Assert.Null(ConventionAssertionProviderRegistry.Current);
    }

    [Fact]
    public async Task ARegisteredProviderIsUsed()
    {
        ConventionAssertionProviderRegistry.Register(new FakeProvider(new ConventionAssertionReport(3, [], [])));

        Assert.True(ConventionAssertionProviderRegistry.HasProvider);

        var report = await ConventionAssertionProviderRegistry.Current!
            .RunAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(3, report.Total);
        Assert.True(report.Passed);
    }

    [Fact]
    public void AnEmptyRunIsNotAPass()
    {
        // Zero assertions is indistinguishable from a run that never happened.
        Assert.False(ConventionAssertionReport.Empty.Passed);
        Assert.False(new ConventionAssertionReport(0, [], ["everything skipped"]).Passed);
    }

    [Fact]
    public void AnyFailureFailsTheReport() =>
        Assert.False(new ConventionAssertionReport(5, ["ButtonHandler.Mapper missing TextColor"], []).Passed);

    [Fact]
    public void SkipsAloneDoNotFailARunThatAlsoAsserted() =>
        Assert.True(new ConventionAssertionReport(4, [], ["Geocoding unsupported on API15"]).Passed);

    [Fact]
    public void PayloadCarriesEverythingTheHarnessReads()
    {
        var payload = new ConventionAssertionReport(2, ["a"], ["b"]).ToPayload();

        Assert.Equal(2, payload["total"]);
        Assert.Equal(false, payload["passed"]);
        Assert.Equal(["a"], (IReadOnlyList<string>)payload["failed"]);
        Assert.Equal(["b"], (IReadOnlyList<string>)payload["skipped"]);
    }

    [Fact]
    public void ExtensionIdentityIsStable()
    {
        // The agent registers with these values and the harness discovers the route by namespace;
        // drift between the two would produce a 404 that looks like a missing app.
        Assert.Equal("org.dotnet.maui.tizen", TizenDevFlowConventions.Namespace);
        Assert.Equal("/conventions/run", TizenDevFlowConventions.RunRoute);
        Assert.Contains(TizenDevFlowConventions.ConventionsFeature, TizenDevFlowConventions.Features);
    }
}
