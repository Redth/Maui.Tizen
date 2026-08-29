using System.Text.Json;
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

	[Fact]
	public void ClipboardNativeRelayUsesTheExactGenerationHandler()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src",
			"Maui.Tizen.Essentials",
			"Clipboard",
			"TizenClipboard.cs"));

		Assert.DoesNotContain("Action? _changed", source, StringComparison.Ordinal);
		Assert.Contains(
			"dataSelectedHandler = (_, _) => changed();",
			source,
			StringComparison.Ordinal);
		Assert.Contains(
			"Clipboard.DataSelected += dataSelectedHandler;",
			source,
			StringComparison.Ordinal);
		Assert.Contains(
			"Clipboard.DataSelected -= _dataSelectedHandler;",
			source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void MigrationDocumentationRecordsMergedHandlersAndImplementedEssentials()
	{
		var migration = File.ReadAllText(RepoPaths.Combine("docs", "migration.md"));

		Assert.Contains("Core and Waves A/B/C merged", migration, StringComparison.Ordinal);
		Assert.Contains("Implemented and host/API15 tested", migration, StringComparison.Ordinal);
		Assert.DoesNotContain("| 2 | Handler implementation (`Maui.Tizen.Core`, `Maui.Tizen.Controls`) | Not started |", migration, StringComparison.Ordinal);
		Assert.DoesNotContain("| 3 | Essentials implementation | Not started |", migration, StringComparison.Ordinal);
	}

	[Fact]
	public void ExactExternalMauiApiBlockersRemainMachineReadable()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine(
			"eng",
			"validation",
			"essentials-external-blockers.json")));
		var blockers = document.RootElement
			.GetProperty("blockers")
			.EnumerateArray()
			.ToDictionary(
				blocker => blocker.GetProperty("id").GetString()!,
				blocker => blocker);

		Assert.Equal("active", blockers["maui-file-result-path-open"].GetProperty("status").GetString());
		Assert.Equal("active", blockers["maui-passkey-response-factory"].GetProperty("status").GetString());
		Assert.Contains(
			"FileResult",
			blockers["maui-file-result-path-open"].GetProperty("reason").GetString(),
			StringComparison.Ordinal);
		Assert.Contains(
			"response",
			blockers["maui-passkey-response-factory"].GetProperty("reason").GetString(),
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Api15GeocodingContractMatchesTheRegisteredUnsupportedService()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine(
			"eng",
			"validation",
			"api15-contract.json")));
		var geocoding = document.RootElement
			.GetProperty("unsupportedServices")
			.EnumerateArray()
			.Single(service => service.GetProperty("contract").GetString() == "IGeocoding");

		Assert.False(geocoding.GetProperty("doNotRegisterInDi").GetBoolean());
		Assert.Contains(
			"IPlatformGeocoding",
			geocoding.GetProperty("behaviour").GetString(),
			StringComparison.Ordinal);
		Assert.Contains(
			"FeatureNotSupportedException",
			geocoding.GetProperty("behaviour").GetString(),
			StringComparison.Ordinal);
		Assert.Contains(
			"MapServiceToken",
			geocoding.GetProperty("behaviour").GetString(),
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
