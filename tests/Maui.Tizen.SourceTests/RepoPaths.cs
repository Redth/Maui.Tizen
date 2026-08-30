namespace Maui.Tizen.SourceTests;

/// <summary>Locates the repository root from the test output directory.</summary>
/// <remarks>
/// Deliberately walks up from <see cref="AppContext.BaseDirectory"/> rather than using
/// <c>[CallerFilePath]</c>: CI builds set <c>DeterministicSourcePaths</c>, which rewrites source
/// paths to <c>/_/…</c> and makes any compiled-in path useless at runtime. This matches how
/// tests/UnitTests locates the root.
/// </remarks>
public static class RepoPaths
{
	public static string Root { get; } = FindRoot();

	public static string Combine(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

	static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);

		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
		{
			dir = dir.Parent;
		}

		return dir?.FullName
			?? throw new InvalidOperationException(
				$"Could not locate the repository root by walking up from '{AppContext.BaseDirectory}'.");
	}
}
