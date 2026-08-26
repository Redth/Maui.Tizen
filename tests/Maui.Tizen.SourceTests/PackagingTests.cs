namespace Maui.Tizen.SourceTests;

public class PackagingTests
{
	static IReadOnlyList<string> ProjectFiles { get; } =
		Directory.EnumerateFiles(RepoPaths.Root, "*.csproj", SearchOption.AllDirectories)
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToList();

	/// <summary>
	/// MAUI must be consumed as packages. A ProjectReference into a dotnet/maui checkout would make
	/// the build depend on a tree that is not part of this repository.
	/// </summary>
	[Fact]
	public void NoProjectReferencesIntoMauiSource()
	{
		var offenders = new List<string>();

		foreach (var project in ProjectFiles)
		{
			foreach (var line in File.ReadLines(project))
			{
				if (!line.Contains("ProjectReference", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (line.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase) ||
					line.Contains("/maui/src/", StringComparison.OrdinalIgnoreCase) ||
					line.Contains("Core.csproj", StringComparison.OrdinalIgnoreCase))
				{
					offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, project)}: {line.Trim()}");
				}
			}
		}

		Assert.Empty(offenders);
	}

	[Fact]
	public void SourceTestProjectDoesNotTargetTizen()
	{
		var text = File.ReadAllText(RepoPaths.Combine("tests", "Maui.Tizen.SourceTests", "Maui.Tizen.SourceTests.csproj"));

		Assert.DoesNotContain("-tizen", text, StringComparison.OrdinalIgnoreCase);
	}
}
