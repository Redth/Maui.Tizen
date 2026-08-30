namespace Maui.Tizen.SourceTests;

public class PresentationSourceIntegrityTests
{
	[Fact]
	public void NuiGestureAdaptersUnsubscribeDetachAndDisposeIndependently()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src",
			"Maui.Tizen.Controls",
			"Core",
			"Platform",
			"Nui",
			"NuiGestureDetectorFactory.cs"));

		Assert.Contains(
			"UnsubscribeNativeEvents,\n\t\t\t\tDetach,\n\t\t\t\tNativeDetector.Dispose",
			source,
			StringComparison.Ordinal);
		Assert.Equal(4, source.Split("protected override void UnsubscribeNativeEvents()", StringSplitOptions.None).Length - 1);
		Assert.Contains(
			"view.TouchEvent -= OnTouch",
			source,
			StringComparison.Ordinal);
		Assert.Contains(
			"view.HoverEvent -= OnHover",
			source,
			StringComparison.Ordinal);
		Assert.Contains(
			"leaveRequiredLease?.Dispose()",
			source,
			StringComparison.Ordinal);
		Assert.Contains("SubscribeNativeEvents(Action subscribe)", source, StringComparison.Ordinal);
		Assert.Contains(
			"catch\n\t\t\t{\n\t\t\t\tNativeDetector.Dispose();\n\t\t\t\tthrow;",
			source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ControlsPackageApiGateUsesPinnedAssemblyMetadata()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"eng",
			"targets",
			"MauiControlsApiGate.targets"));

		Assert.Contains("PEReader", source, StringComparison.Ordinal);
		Assert.Contains("Microsoft.Maui.Controls.dll", source, StringComparison.Ordinal);
		Assert.Contains("BeforeTargets=\"GenerateNuspec\"", source, StringComparison.Ordinal);
		Assert.Contains(
			"Condition=\"'$(MSBuildProjectName)' == 'Maui.Tizen.Controls'\"",
			source,
			StringComparison.Ordinal);
		Assert.Contains("MAUITIZEN0104", source, StringComparison.Ordinal);
		Assert.Contains("MAUITIZEN0105", source, StringComparison.Ordinal);
		Assert.Contains("MauiTizenInspectLocalControlsAdoption", source, StringComparison.Ordinal);
		Assert.Contains("_MauiTizenModalLocalAdoption", source, StringComparison.Ordinal);
		Assert.Contains("_MauiTizenLongPressLocalAdoption", source, StringComparison.Ordinal);
		Assert.DoesNotContain("LocalAdoptionVerified", source, StringComparison.Ordinal);
		Assert.DoesNotContain("LocalAdoptionComplete)'", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Assembly.Load", source, StringComparison.Ordinal);
		Assert.DoesNotContain("GetMethod(", source, StringComparison.Ordinal);
	}
}
