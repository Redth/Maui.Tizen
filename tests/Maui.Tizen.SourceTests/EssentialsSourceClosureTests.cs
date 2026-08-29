using System.Xml.Linq;

namespace Maui.Tizen.SourceTests;

public class EssentialsSourceClosureTests
{
	[Fact]
	public void SharedSourceClosureMatchesEveryShippingEssentialsSource()
	{
		var manifest = XDocument.Load(RepoPaths.Combine("eng", "Maui.Tizen.Essentials.Sources.props"));

		var declared = manifest
			.Descendants("MauiTizenEssentialsCompile")
			.Select(element => element.Attribute("Include")?.Value)
			.Where(path => path is not null)
			.Select(path => path!.Replace("$(MauiTizenEssentialsDir)", string.Empty, StringComparison.Ordinal))
			.OrderBy(path => path, StringComparer.Ordinal)
			.ToArray();

		var sourceRoot = RepoPaths.Combine("src", "Maui.Tizen.Essentials");
		var actual = Directory
			.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Select(path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
			.OrderBy(path => path, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(actual, declared);
		Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
	}

	[Theory]
	[InlineData("src/Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj")]
	[InlineData("src/Maui.Tizen.Essentials.HostVerification/Maui.Tizen.Essentials.HostVerification.csproj")]
	[InlineData("tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj")]
	public void EveryEssentialsLaneConsumesTheSharedSourceClosure(string projectPath)
	{
		var project = File.ReadAllText(RepoPaths.Combine(projectPath.Split('/')));

		Assert.Contains("Maui.Tizen.Essentials.Sources.props", project, StringComparison.Ordinal);
		Assert.Contains("@(MauiTizenEssentialsCompile)", project, StringComparison.Ordinal);
		Assert.DoesNotContain("Maui.Tizen.Essentials/**/*.cs", project, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("src/Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj")]
	[InlineData("tests/Maui.Tizen.Essentials.RefPackCompile/Maui.Tizen.Essentials.RefPackCompile.csproj")]
	public void ProductAndApi15LaneUseThePackageSpecificPublicApiBaseline(string projectPath)
	{
		var project = File.ReadAllText(RepoPaths.Combine(projectPath.Split('/')));

		Assert.Contains(
			"src/Maui.Tizen.Essentials/PublicAPI/slice/PublicAPI.Shipped.txt".Replace(
				"src/Maui.Tizen.Essentials/",
				projectPath.StartsWith("src/", StringComparison.Ordinal)
					? string.Empty
					: "$(RepositoryRoot)src/Maui.Tizen.Essentials/",
				StringComparison.Ordinal),
			project,
			StringComparison.Ordinal);
		Assert.Contains(
			"src/Maui.Tizen.Essentials/PublicAPI/slice/PublicAPI.Unshipped.txt".Replace(
				"src/Maui.Tizen.Essentials/",
				projectPath.StartsWith("src/", StringComparison.Ordinal)
					? string.Empty
					: "$(RepositoryRoot)src/Maui.Tizen.Essentials/",
				StringComparison.Ordinal),
			project,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"AdditionalFiles Include=\"PublicAPI/net-tizen/PublicAPI.Shipped.txt",
			project,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ControlsProductionCompositionIncludesEssentialsExactlyOnce()
	{
		var startup = File.ReadAllText(RepoPaths.Combine(
			"src",
			"Maui.Tizen.Controls",
			"Hosting",
			"TizenControlsMauiAppBuilderExtensions.cs"));
		var project = File.ReadAllText(RepoPaths.Combine(
			"src",
			"Maui.Tizen.Controls",
			"Maui.Tizen.Controls.csproj"));

		Assert.Equal(1, CountOccurrences(startup, "builder.AddTizenEssentials();"));
		Assert.Equal(1, CountOccurrences(
			project,
			"<ProjectReference Include=\"../Maui.Tizen.Essentials/Maui.Tizen.Essentials.csproj\" />"));
	}

	[Fact]
	public void EssentialsRegistrationReplacesRatherThanShadowsPlatformServices()
	{
		var registration = File.ReadAllText(RepoPaths.Combine(
			"src",
			"Maui.Tizen.Essentials",
			"Hosting",
			"TizenEssentialsMauiAppBuilderExtensions.cs"));

		Assert.DoesNotContain("services.TryAddSingleton<I", registration, StringComparison.Ordinal);
		Assert.Contains(
			"ReplaceSingleton<IBattery, TizenBattery>(services);",
			registration,
			StringComparison.Ordinal);
	}

	static int CountOccurrences(string text, string value)
	{
		var count = 0;
		var index = 0;

		while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += value.Length;
		}

		return count;
	}
}
