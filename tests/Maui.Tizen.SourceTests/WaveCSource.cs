namespace Maui.Tizen.SourceTests;

/// <summary>
/// Discovers and parses the migrated Wave C sources: navigation, Shell, CollectionView, toolbar and
/// menu handlers, plus the Tizen-owned adapters that replace Controls internals.
/// </summary>
/// <remarks>
/// <para>
/// Wave C ships in <c>Maui.Tizen.Controls</c>. Its source root remains a distinct subdirectory so
/// ownership is explicit without creating a second assembly or startup API.
/// </para>
/// <para>
/// The Roslyn parsing itself is shared with <see cref="WaveBSource"/> - see
/// <see cref="WaveBSource.Parse"/>. The extraction rules are wave-independent and duplicating them
/// would just create two things to keep in sync.
/// </para>
/// </remarks>
public static class WaveCSource
{
	/// <summary>Repository-relative root of the Wave C sources.</summary>
	public static readonly string[] Root = { "src", "Maui.Tizen.Controls", "Navigation" };

	/// <summary>Every migrated Wave C source file.</summary>
	public static IReadOnlyList<string> Files { get; } = Discover();

	/// <summary>Every handler declared by Wave C, parsed from source.</summary>
	public static IReadOnlyList<HandlerSource> Handlers { get; } = Files
		.SelectMany(WaveBSource.Parse)
		.Where(h => h.TypeName.EndsWith("Handler", StringComparison.Ordinal))
		.OrderBy(h => h.TypeName, StringComparer.Ordinal)
		.ToList();

	static IReadOnlyList<string> Discover()
	{
		var root = RepoPaths.Combine(Root);

		if (!Directory.Exists(root))
		{
			return Array.Empty<string>();
		}

		return Directory
			.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(p => !IsBuildOutput(p))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToList();
	}

	static bool IsBuildOutput(string path)
	{
		var sep = Path.DirectorySeparatorChar;
		return path.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
			|| path.Contains($"{sep}bin{sep}", StringComparison.Ordinal);
	}
}
