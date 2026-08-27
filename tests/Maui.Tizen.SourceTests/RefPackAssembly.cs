namespace Maui.Tizen.SourceTests;

/// <summary>
/// Locates the ref-pack assembly that the metadata-based tests read.
/// </summary>
/// <remarks>
/// <para>
/// This exists because getting it wrong is silent. The tests that assert on emitted metadata do not
/// build the ref-pack project — nothing references it, so <c>dotnet test</c> will not rebuild it —
/// and an earlier version of this helper simply preferred <c>Release</c> over <c>Debug</c>. A local
/// <c>dotnet build</c> writes <c>Debug</c>, so those tests happily read a <c>Release</c> assembly
/// built hours earlier and passed against metadata that no longer described the source.
/// </para>
/// <para>
/// That is the worst possible failure for this class of test: it does not report a false failure,
/// it reports a false <em>success</em>, which is exactly what a metadata guard is supposed to make
/// impossible. So this picks the most recently built configuration and then refuses to run at all
/// if that assembly is older than the sources it claims to describe.
/// </para>
/// </remarks>
public static class RefPackAssembly
{
	const string AssemblyName = "Maui.Tizen.Core.RefPackCompile";

	/// <summary>The freshest built ref-pack assembly.</summary>
	public static string Path { get; } = Locate();

	static string Locate()
	{
		var candidates = new[] { "Release", "Debug" }
			.Select(configuration => RepoPaths.Combine(
				"artifacts", "bin", AssemblyName, configuration, "net11.0", AssemblyName + ".dll"))
			.Where(File.Exists)
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.ToList();

		Assert.True(
			candidates.Count > 0,
			$"{AssemblyName} has not been built. Run: dotnet build tests/{AssemblyName}");

		var assembly = candidates[0];
		var built = File.GetLastWriteTimeUtc(assembly);

		var stale = BuildInputs()
			.Where(source => File.GetLastWriteTimeUtc(source) > built)
			.Select(source => System.IO.Path.GetFileName(source))
			.Take(5)
			.ToList();

		Assert.True(
			stale.Count == 0,
			$"{AssemblyName} was built at {built:u} but these sources changed afterwards: "
			+ $"{string.Join(", ", stale)}. Metadata assertions would be checking a stale assembly "
			+ $"and could pass for code that no longer exists. Rebuild: dotnet build tests/{AssemblyName}");

		return assembly;
	}

	/// <summary>Everything that determines the ref-pack assembly's contents.</summary>
	/// <remarks>
	/// The build inputs matter as much as the sources. Dropping a file from
	/// <c>eng/Maui.Tizen.Core.Sources.props</c> changes what is compiled without touching any
	/// <c>.cs</c> file at all, so a scan of sources alone would see nothing, the stale assembly would
	/// still contain the removed type, and the metadata assertion would pass for code no longer
	/// being built. That is defended structurally by the build-ordering ProjectReference in this
	/// project; this list is the second line, for the `--no-build` case where MSBuild is not
	/// consulted.
	/// </remarks>
	static IEnumerable<string> BuildInputs()
	{
		foreach (var manifest in new[]
		{
			"eng/Maui.Tizen.Core.Sources.props",
			"eng/Maui.props",
			"eng/targets/TizenPackage.props",
			"Directory.Build.props",
			"Directory.Packages.props",
			"tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj",
		})
		{
			var path = RepoPaths.Combine(manifest.Split('/'));

			if (File.Exists(path))
				yield return path;
		}

		foreach (var source in SourceFiles())
			yield return source;
	}

	/// <summary>The product sources the ref-pack lane compiles.</summary>
	static IEnumerable<string> SourceFiles()
	{
		foreach (var directory in new[] { "src/Maui.Tizen.Core", "src/Maui.Tizen.Controls" })
		{
			var root = RepoPaths.Combine(directory.Split('/'));

			if (!Directory.Exists(root))
				continue;

			foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				// The raw dotnet/maui import shares these directories and is never compiled.
				if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal)
					|| file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				{
					continue;
				}

				yield return file;
			}
		}
	}
}
