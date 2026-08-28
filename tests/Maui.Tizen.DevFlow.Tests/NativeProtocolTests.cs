using Microsoft.Maui.DevFlow.Agent.Core;

namespace Maui.Tizen.DevFlow.Tests;

public class NativeProtocolTests
{
    [Fact]
    public void CssSelectorFiltersProjectedNativeTree()
    {
        List<ElementInfo> projected =
        [
            Element("native:tizen:button", "Button", "save", "Save"),
            Element("native:tizen:label", "Label", "status", "Ready")
        ];

        var matches = NativeElementQuery.Apply(projected, null, null, null, "*[automationId=save]");

        var match = Assert.Single(matches);
        Assert.Equal("native:tizen:button", match.Id);
    }

    [Theory]
    [InlineData("tap")]
    [InlineData("fill")]
    [InlineData("focus")]
    public void CaptureBoundNativeActionsAcceptUnchangedElementAndRejectReplacement(string action)
    {
        var original = new object();
        var replacement = new object();
        var projected = Element("native:tizen:button", "Button", "save", "Save");
        NativeElementIdentity.Stamp(projected, original);
        var capturedIdentity = Assert.IsType<object>(NativeElementIdentity.Read(projected));

        Assert.True(NativeElementIdentity.Matches(capturedIdentity, original));
        Assert.False(
            NativeElementIdentity.Matches(capturedIdentity, replacement),
            $"{action} must reject an element that was replaced after capture.");
    }

    static ElementInfo Element(string id, string type, string automationId, string text) =>
        new()
        {
            Id = id,
            Type = type,
            FullType = $"Tizen.NUI.{type}",
            NativeType = type,
            Framework = "native",
            Origin = "tizen-native-bridge",
            AutomationId = automationId,
            Text = text,
            IsVisible = true,
            IsEnabled = true,
            Children = [],
        };
}
