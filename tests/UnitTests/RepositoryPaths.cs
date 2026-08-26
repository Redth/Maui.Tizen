namespace Maui.Tizen.UnitTests;

/// <summary>
/// Locates the repository root from the test assembly's output directory.
/// </summary>
public static class RepositoryPaths
{
	static readonly Lazy<string> _root = new(Find);

	public static string Root => _root.Value;

	static string Find()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
			dir = dir.Parent;

		if (dir is null)
			throw new InvalidOperationException(
				$"Could not locate the repository root above '{AppContext.BaseDirectory}'.");

		return dir.FullName;
	}
}
