using System.Xml.Linq;

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
	/// <remarks>
	/// Resolves each reference rather than pattern-matching the text. Intra-repository references
	/// such as Maui.Tizen.Controls -> Maui.Tizen.Core are legitimate and must not be flagged; what
	/// matters is whether a reference escapes the repository or names a MAUI source project.
	/// </remarks>
	[Fact]
	public void NoProjectReferencesIntoMauiSource()
	{
		var offenders = new List<string>();
		var root = RepoPaths.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		foreach (var project in ProjectFiles)
		{
			var directory = Path.GetDirectoryName(project)!;

			foreach (var include in XDocument.Load(project)
				.Descendants()
				.Where(e => e.Name.LocalName == "ProjectReference")
				.Select(e => e.Attribute("Include")?.Value)
				.Where(v => !string.IsNullOrWhiteSpace(v))
				.Select(v => v!))
			{
				var normalized = include.Replace('\\', Path.DirectorySeparatorChar);
				var resolved = Path.GetFullPath(Path.Combine(directory, normalized));
				var relative = Path.GetRelativePath(RepoPaths.Root, project);

				if (!resolved.StartsWith(root, StringComparison.Ordinal))
				{
					offenders.Add($"{relative}: '{include}' resolves outside the repository ({resolved}).");
					continue;
				}

				if (Path.GetFileName(resolved).StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase))
				{
					offenders.Add($"{relative}: '{include}' references a MAUI source project.");
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
