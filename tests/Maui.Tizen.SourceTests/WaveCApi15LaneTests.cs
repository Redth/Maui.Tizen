using System.Text.RegularExpressions;

namespace Maui.Tizen.SourceTests;

/// <summary>Guards Wave C's finalized assembly and API15 source-lane ownership.</summary>
public class WaveCApi15LaneTests
{
	static string SourcesProps() => File.ReadAllText(RepoPaths.Combine("eng", "Maui.Tizen.WaveC.Sources.props"));

	[Fact]
	public void EveryWaveCSourceAndCatalogPageIsListed()
	{
		var listed = Regex.Matches(
				SourcesProps(),
				@"Include=""\$\((?:MauiTizenNavigationDir|MauiTizenCatalogDir)\)([^""]+)""")
			.Select(m => m.Groups[1].Value.Replace('\\', '/'))
			.ToHashSet(StringComparer.Ordinal);

		var onDisk = new[]
			{
				RepoPaths.Combine("src", "Maui.Tizen.Controls", "Navigation"),
				RepoPaths.Combine("samples", "Controls", "Catalog"),
			}
			.Where(Directory.Exists)
			.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
				.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')))
			.ToList();

		Assert.True(
			onDisk.All(listed.Contains),
			"Wave C sources missing from the deterministic manifest: "
				+ string.Join(", ", onDisk.Where(path => !listed.Contains(path))));
	}

	[Fact]
	public void Api15AcceptanceIsUnconditionalAndOwnedByControls()
	{
		var props = SourcesProps();
		var product = File.ReadAllText(
			RepoPaths.Combine("src", "Maui.Tizen.Controls", "Maui.Tizen.Controls.csproj"));
		var controlsLane = File.ReadAllText(
			RepoPaths.Combine("tests", "Maui.Tizen.Controls.RefPackCompile", "Maui.Tizen.Controls.RefPackCompile.csproj"));
		var coreLane = File.ReadAllText(
			RepoPaths.Combine("tests", "Maui.Tizen.Core.RefPackCompile", "Maui.Tizen.Core.RefPackCompile.csproj"));

		Assert.DoesNotContain("MauiTizenWaveCAcceptance", props, StringComparison.Ordinal);
		Assert.Contains("Maui.Tizen.WaveC.Sources.props", product, StringComparison.Ordinal);
		Assert.Contains("Maui.Tizen.WaveC.Sources.props", controlsLane, StringComparison.Ordinal);
		Assert.DoesNotContain("Maui.Tizen.WaveC.Sources.props", coreLane, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenToolbarView")]
	[InlineData("TizenStackNavigationManager")]
	[InlineData("TizenFlyoutView")]
	public void WaveCUsesCoreOwnedPrimitives(string typeName)
	{
		var coreSources = string.Concat(Directory
			.EnumerateFiles(
				RepoPaths.Combine("src", "Maui.Tizen.Core"),
				"*.cs",
				SearchOption.AllDirectories)
			.Select(File.ReadAllText));

		Assert.Contains(typeName, coreSources, StringComparison.Ordinal);
		Assert.Contains(typeName, string.Concat(WaveCSource.Files.Select(File.ReadAllText)), StringComparison.Ordinal);
	}

	[Fact]
	public void LegacyNet9WaveCValidationLaneIsGone()
	{
		Assert.False(File.Exists(RepoPaths.Combine("eng", "validation", "run-validation-lane.sh")));
		Assert.False(File.Exists(RepoPaths.Combine("eng", "validation", "validation-lane.csproj.template")));
		Assert.DoesNotContain("net9.0-tizen7.0", SourcesProps(), StringComparison.Ordinal);
	}

	[Fact]
	public void ImplementationAdaptersAreNotPartOfTheShippingPublicApi()
	{
		var api = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "PublicAPI", "slice", "PublicAPI.Unshipped.txt"));

		Assert.DoesNotContain("Microsoft.Maui.Platforms.Tizen.Adapters.", api, StringComparison.Ordinal);
		Assert.DoesNotContain("ToolbarOwnership", api, StringComparison.Ordinal);
	}
}
