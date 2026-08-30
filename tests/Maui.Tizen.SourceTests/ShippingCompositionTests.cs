namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards that Wave B actually ships, rather than only being type-checked.
/// </summary>
/// <remarks>
/// The Wave B sources were, for a period, compiled *only* by
/// <c>tests/Maui.Tizen.Core.RefPackCompile</c>. That lane proves the code type-checks against real
/// TizenFX, but it produces a test assembly: nothing in it reaches a consumer. A backend that
/// verifies perfectly and ships nothing is indistinguishable, at runtime, from one that was never
/// written — MAUI's neutral handlers still resolve, so an app runs and simply renders nothing
/// Tizen. These tests pin the product projects to the same source groups.
/// </remarks>
public class ShippingCompositionTests
{
	static string ReadProject(params string[] parts) => File.ReadAllText(RepoPaths.Combine(parts));

	[Fact]
	public void CoreProductProjectCompilesTheWaveBSources()
	{
		var project = ReadProject("src", "Maui.Tizen.Core", "Maui.Tizen.Core.csproj");

		Assert.Contains("@(MauiTizenWaveBCompile)", project, StringComparison.Ordinal);
	}

	[Fact]
	public void ControlsProductProjectCompilesTheWaveBShapeHandlers()
	{
		var project = ReadProject("src", "Maui.Tizen.Controls", "Maui.Tizen.Controls.csproj");

		// Without the import the item group is empty and the Compile element silently contributes
		// nothing, which is exactly the failure mode being guarded against.
		Assert.Contains("Maui.Tizen.Core.Sources.props", project, StringComparison.Ordinal);
		Assert.Contains("@(MauiTizenWaveBControlsCompile)", project, StringComparison.Ordinal);
	}

	/// <summary>
	/// Every Wave B source listed in the manifest must exist on disk.
	/// </summary>
	/// <remarks>
	/// A path typo makes MSBuild's <c>Include</c> resolve to nothing. There is no error: the file is
	/// simply not compiled, and the type it declared goes missing from the product.
	/// </remarks>
	[Fact]
	public void EveryListedWaveBSourceExists()
	{
		var manifest = ReadProject("eng", "Maui.Tizen.Core.Sources.props");

		var missing = new List<string>();

		foreach (var line in manifest.Split('\n'))
		{
			if (!line.Contains("MauiTizenWaveB", StringComparison.Ordinal))
				continue;

			var marker = "Include=\"";
			var start = line.IndexOf(marker, StringComparison.Ordinal);
			if (start < 0)
				continue;

			start += marker.Length;
			var end = line.IndexOf('"', start);
			var include = line[start..end];

			// Group references such as @(MauiTizenWaveBPortableCompile) are not paths.
			if (include.StartsWith('@'))
				continue;

			var relative = include
				.Replace("$(MauiTizenCoreDir)", "src/Maui.Tizen.Core/", StringComparison.Ordinal)
				.Replace("$(MauiTizenControlsDir)", "src/Maui.Tizen.Controls/", StringComparison.Ordinal);

			if (!File.Exists(RepoPaths.Combine(relative.Split('/'))))
				missing.Add(relative);
		}

		Assert.Empty(missing);
	}
}
