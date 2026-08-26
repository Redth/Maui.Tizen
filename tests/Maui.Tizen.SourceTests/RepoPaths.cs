using System.Runtime.CompilerServices;

namespace Maui.Tizen.SourceTests;

/// <summary>Locates the repository root from the compiled-in source path.</summary>
public static class RepoPaths
{
	public static string Root { get; } = FindRoot();

	public static string Combine(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

	static string FindRoot([CallerFilePath] string thisFile = "")
	{
		var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);

		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PROVENANCE.md")))
		{
			dir = dir.Parent;
		}

		return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
	}
}
