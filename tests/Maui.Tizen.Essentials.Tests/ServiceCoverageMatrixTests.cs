using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Keeps <c>docs/tizen-essentials-service-coverage.md</c> honest by asserting it against the real
/// DI registrations rather than trusting it to be updated by hand.
/// </summary>
public class ServiceCoverageMatrixTests
{
	static readonly string[] ValidLevels = ["Implemented", "Partial", "Unsupported", "Blocked"];

	static readonly string[] ValidProfiles =
		["All", "Mobile", "Wearable", "TV", "Common", "–"];

	sealed record Row(string Contract, string Implementation, string Level, string Profiles, string Notes);

	static IReadOnlyList<Row> ReadMatrix()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "tizen-essentials-service-coverage.md");
		Assert.True(File.Exists(path), $"Coverage matrix not found at '{path}'.");

		var lines = File.ReadAllLines(path);
		var begin = Array.FindIndex(lines, l => l.Contains("coverage-matrix:begin", StringComparison.Ordinal));
		var end = Array.FindIndex(lines, l => l.Contains("coverage-matrix:end", StringComparison.Ordinal));

		Assert.True(begin >= 0 && end > begin, "The coverage matrix markers are missing or out of order.");

		var rows = new List<Row>();

		foreach (var line in lines[(begin + 1)..end])
		{
			var trimmed = line.Trim();
			if (!trimmed.StartsWith('|') || trimmed.StartsWith("| ---", StringComparison.Ordinal))
				continue;

			var cells = trimmed.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
			if (cells.Length < 5 || cells[0] == "Contract")
				continue;

			rows.Add(new Row(
				cells[0].Trim('`'),
				cells[1].Trim('`'),
				cells[2],
				cells[3],
				cells[4]));
		}

		return rows;
	}

	[Fact]
	public void DocumentsEveryRegisteredService()
	{
		var documented = ReadMatrix().Select(r => r.Contract).ToHashSet(StringComparer.Ordinal);

		var missing = TizenEssentialsRegistrationTests.ExpectedRegistrations.Keys
			.Select(t => t.Name)
			.Where(name => !documented.Contains(name))
			.ToList();

		Assert.Empty(missing);
	}

	[Fact]
	public void DocumentsGeocodingEvenThoughItIsRegisteredThroughAFactory() =>
		Assert.Contains("IGeocoding", ReadMatrix().Select(r => r.Contract));

	[Fact]
	public void DocumentsNothingThatIsNotRegistered()
	{
		var registered = TizenEssentialsRegistrationTests.ExpectedRegistrations.Keys
			.Select(t => t.Name)
			.Append("IGeocoding")
			.ToHashSet(StringComparer.Ordinal);

		var extra = ReadMatrix()
			.Select(r => r.Contract)
			.Where(name => !registered.Contains(name))
			.ToList();

		Assert.Empty(extra);
	}

	[Fact]
	public void NamesRealImplementationTypesInTheExpectedNamespace()
	{
		var assembly = typeof(TizenAppInfo).Assembly;

		foreach (var row in ReadMatrix())
		{
			var type = assembly.GetType($"Microsoft.Maui.Platforms.Tizen.Essentials.{row.Implementation}", throwOnError: false);

			Assert.True(type is not null, $"'{row.Implementation}' is documented but does not exist in the backend assembly.");
			Assert.True(type!.IsPublic, $"'{row.Implementation}' must be public.");
		}
	}

	[Fact]
	public void UsesOnlyTheDefinedSupportLevels()
	{
		var invalid = ReadMatrix()
			.Where(r => !ValidLevels.Contains(r.Level, StringComparer.Ordinal))
			.Select(r => $"{r.Contract} => '{r.Level}'")
			.ToList();

		Assert.Empty(invalid);
	}

	[Fact]
	public void ClassifiesProfilesForEverySupportedService()
	{
		var invalid = new List<string>();

		foreach (var row in ReadMatrix())
		{
			var profiles = row.Profiles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			if (profiles.Length == 0 || profiles.Any(p => !ValidProfiles.Contains(p, StringComparer.Ordinal)))
				invalid.Add($"{row.Contract} => '{row.Profiles}'");

			// Unsupported services must not claim any profile.
			if (row.Level == "Unsupported" && row.Profiles != "–")
				invalid.Add($"{row.Contract} is Unsupported but claims profiles '{row.Profiles}'.");

			// Everything else must claim at least one real profile.
			if (row.Level != "Unsupported" && row.Profiles == "–")
				invalid.Add($"{row.Contract} is {row.Level} but claims no profile.");
		}

		Assert.Empty(invalid);
	}

	[Fact]
	public void ExplainsEveryPartialAndUnsupportedService()
	{
		var missingNotes = ReadMatrix()
			.Where(r => r.Level != "Implemented" && string.IsNullOrWhiteSpace(r.Notes))
			.Select(r => r.Contract)
			.ToList();

		Assert.Empty(missingNotes);
	}

	[Fact]
	public void MatchesTheImplementationTypesUsedByDependencyInjection()
	{
		var byContract = ReadMatrix().ToDictionary(r => r.Contract, r => r.Implementation, StringComparer.Ordinal);

		foreach (var (serviceType, implementationType) in TizenEssentialsRegistrationTests.ExpectedRegistrations)
			Assert.Equal(implementationType.Name, byContract[serviceType.Name]);
	}
}
