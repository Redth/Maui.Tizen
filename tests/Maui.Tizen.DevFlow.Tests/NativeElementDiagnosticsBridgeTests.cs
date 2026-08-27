namespace Maui.Tizen.DevFlow.Tests;

/// <summary>
/// Behaviour of the registry that makes platform-owned Tizen chrome visible to a driver.
/// </summary>
/// <remarks>
/// Shell chrome, toolbar items and native dialogs never appear in the MAUI visual tree. Without this
/// registry a driver simply cannot see or tap them, so its correctness decides whether whole
/// categories of catalog cases are testable at all.
/// </remarks>
public class NativeElementDiagnosticsBridgeTests
{
    static NativeElementDescriptor Descriptor(
        object? target = null,
        string typeName = "MauiToolbarButton",
        string role = "button",
        double x = 0,
        double y = 0,
        double width = 100,
        double height = 40,
        string? automationId = null,
        string? text = null,
        bool canInvoke = true,
        bool canFocus = true,
        bool canSetValue = false) =>
        new(
            target ?? new object(),
            typeName,
            role,
            new NativeElementBounds(x, y, width, height),
            automationId,
            text,
            OwnerId: null,
            canInvoke,
            canFocus,
            canSetValue);

    [Fact]
    public void Register_ReturnsDistinctPrefixedIds()
    {
        var bridge = new NativeElementDiagnosticsBridge();

        var first = bridge.Register(Descriptor());
        var second = bridge.Register(Descriptor());

        Assert.NotEqual(first, second);
        Assert.StartsWith(NativeElementDiagnosticsBridge.IdPrefix, first, StringComparison.Ordinal);
        Assert.Equal(2, bridge.Count);
    }

    [Fact]
    public void Generation_AdvancesOnEveryMutation()
    {
        // DevFlow requests carry the registryGeneration they were computed against so a driver
        // acting on a stale snapshot can be rejected instead of hitting the wrong element.
        var bridge = new NativeElementDiagnosticsBridge();
        var initial = bridge.Generation;

        var id = bridge.Register(Descriptor());
        Assert.True(bridge.Generation > initial);

        var afterRegister = bridge.Generation;
        bridge.Unregister(id);
        Assert.True(bridge.Generation > afterRegister);
    }

    [Fact]
    public void Generation_DoesNotAdvanceOnANoOpClear()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        var generation = bridge.Generation;

        bridge.Clear();

        Assert.Equal(generation, bridge.Generation);
    }

    [Fact]
    public void Unregister_ReturnsFalseForUnknownIds()
    {
        var bridge = new NativeElementDiagnosticsBridge();

        Assert.False(bridge.Unregister("native:tizen:999"));
    }

    [Fact]
    public void TryGet_ResolvesTheRegisteredTarget()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        var target = new object();
        var id = bridge.Register(Descriptor(target));

        Assert.True(bridge.TryGet(id, out var registration));
        Assert.Same(target, registration!.Descriptor.Target);
    }

    [Fact]
    public void HitTest_ReturnsInnermostFirst()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor(typeName: "Container", width: 500, height: 500));
        bridge.Register(Descriptor(typeName: "Button", x: 10, y: 10, width: 50, height: 50));

        var hits = bridge.HitTest(20, 20);

        Assert.Equal(2, hits.Count);
        Assert.Equal("Button", hits[0].Descriptor.TypeName);
    }

    [Fact]
    public void HitTest_ExcludesPointsOutsideBounds()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor(x: 0, y: 0, width: 10, height: 10));

        Assert.Empty(bridge.HitTest(50, 50));
    }

    [Fact]
    public void Bounds_TreatRightAndBottomEdgesAsExclusive()
    {
        // Half-open bounds keep adjacent elements from both claiming the shared edge pixel.
        var bounds = new NativeElementBounds(0, 0, 10, 10);

        Assert.True(bounds.Contains(0, 0));
        Assert.True(bounds.Contains(9.99, 9.99));
        Assert.False(bounds.Contains(10, 5));
        Assert.False(bounds.Contains(5, 10));
    }

    [Fact]
    public void Bounds_WithZeroSizeContainNothing()
    {
        Assert.False(new NativeElementBounds(0, 0, 0, 0).Contains(0, 0));
    }

    [Fact]
    public void Query_FiltersByTypeAutomationIdAndText()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor(typeName: "Tab", automationId: "home", text: "Home"));
        bridge.Register(Descriptor(typeName: "Tab", automationId: "settings", text: "Settings"));
        bridge.Register(Descriptor(typeName: "Dialog", text: "Are you sure?"));

        Assert.Equal(2, bridge.Query("Tab", null, null).Count);
        Assert.Single(bridge.Query(null, "settings", null));
        Assert.Single(bridge.Query(null, null, "sure"));
        Assert.Empty(bridge.Query("Tab", "settings", "Home"));
    }

    [Fact]
    public void Query_WithNoFiltersReturnsEverythingInRegistrationOrder()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor(typeName: "First"));
        bridge.Register(Descriptor(typeName: "Second"));

        var all = bridge.Query(null, null, null);

        Assert.Equal(["First", "Second"], all.Select(r => r.Descriptor.TypeName));
    }

    [Fact]
    public void Capabilities_ReflectTheDeclaredInteractionFlags()
    {
        var descriptor = Descriptor(canInvoke: true, canFocus: false, canSetValue: true);

        Assert.Equal(["invoke", "set-value"], descriptor.Capabilities);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor());
        bridge.Register(Descriptor());

        bridge.Clear();

        Assert.Equal(0, bridge.Count);
        Assert.Empty(bridge.Snapshot());
    }

    [Fact]
    public void Snapshot_IsIsolatedFromLaterMutations()
    {
        // The walker projects a snapshot into ElementInfo objects; if the snapshot aliased live
        // state, a concurrent page teardown could mutate it mid-enumeration.
        var bridge = new NativeElementDiagnosticsBridge();
        bridge.Register(Descriptor());

        var snapshot = bridge.Snapshot();
        bridge.Clear();

        Assert.Single(snapshot);
    }
}
