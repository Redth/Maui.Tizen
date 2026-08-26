using System.Text.Json;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Enforces the superseded-source manifest: the raw dotnet/maui sources Wave C rewrote are retained
/// for provenance but must never be compiled.
/// </summary>
/// <remarks>
/// The foundation import preserved the unmodified Tizen tree on purpose, and Wave C rewrote large
/// parts of it under <c>src/Maui.Tizen.Controls.Navigation</c> without deleting the originals - a
/// later rebase onto finalized predecessor branches is then a content merge rather than a
/// delete/add conflict.
/// <para>
/// The cost of that choice is ambiguity: two files that both look authoritative, one of which
/// silently reaches into <c>Microsoft.Maui.Controls.Internals</c>. These tests remove the ambiguity
/// by making the manifest binding.
/// </para>
/// </remarks>
public class WaveCSupersededSourceTests
{
	const string ManifestRelativePath = "eng/manifests/wave-c-superseded.json";

	static JsonElement Manifest()
	{
		var path = RepoPaths.Combine(ManifestRelativePath.Split('/'));
		Assert.True(File.Exists(path), $"{ManifestRelativePath} is missing.");

		using var doc = JsonDocument.Parse(
			File.ReadAllText(path),
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

		return doc.RootElement.Clone();
	}

	static IReadOnlyList<string> Superseded() => Manifest()
		.GetProperty("supersededSources")
		.EnumerateArray()
		.Select(e => e.GetString()!)
		.ToList();

	[Fact]
	public void ManifestIsWellFormedAndNonEmpty()
	{
		var manifest = Manifest();

		Assert.Equal(1, manifest.GetProperty("schemaVersion").GetInt32());
		Assert.Equal("C", manifest.GetProperty("wave").GetString());
		Assert.False(manifest.GetProperty("policy").GetProperty("compiled").GetBoolean());
		Assert.NotEmpty(Superseded());
	}

	[Fact]
	public void EverySupersededFileStillExists()
	{
		// If one disappears the manifest is stale, and a stale manifest is worse than none: it
		// implies a guarantee it is no longer checking.
		var missing = Superseded()
			.Where(p => !File.Exists(RepoPaths.Combine(p.Split('/'))))
			.ToList();

		Assert.True(
			missing.Count == 0,
			$"{ManifestRelativePath} lists files that no longer exist; prune it: " + string.Join(", ", missing));
	}

	[Fact]
	public void NoSupersededFileAppearsInAnyCompiledItemList()
	{
		// This is the point of the manifest. A superseded file reaching a compile list would either
		// duplicate a migrated type or drag Controls internals back in.
		var engRoot = RepoPaths.Combine("eng");
		var buildFiles = Directory
			.EnumerateFiles(engRoot, "*.props", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(RepoPaths.Combine("src"), "*.csproj", SearchOption.AllDirectories))
			.Concat(Directory.EnumerateFiles(RepoPaths.Combine("tests"), "*.csproj", SearchOption.AllDirectories))
			.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.ToList();

		var offenders = new List<string>();

		foreach (var superseded in Superseded())
		{
			var fileName = Path.GetFileName(superseded);

			foreach (var buildFile in buildFiles)
			{
				var text = File.ReadAllText(buildFile);

				// Match on the file name inside a Compile item only; a <None> include or a comment
				// mentioning the path is fine and expected.
				foreach (var line in text.Split('\n'))
				{
					if (line.Contains("<Compile", StringComparison.Ordinal)
						&& line.Contains(fileName, StringComparison.Ordinal))
					{
						offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, buildFile)} compiles superseded {fileName}");
					}
				}
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void BlockedSourcesAreRecordedAsNotSupersededRatherThanSilentlyDropped()
	{
		// Modal navigation is blocked upstream, so it is neither ported nor superseded. Saying that
		// explicitly is what stops it from looking like an oversight later.
		var notCovered = Manifest().GetProperty("notCovered").EnumerateArray().ToList();

		Assert.NotEmpty(notCovered);

		foreach (var entry in notCovered)
		{
			var path = entry.GetProperty("path").GetString();
			var reason = entry.GetProperty("reason").GetString();

			Assert.False(string.IsNullOrWhiteSpace(reason), $"{path} is listed as not-covered with no reason.");
			Assert.DoesNotContain(path!, Superseded());
		}
	}
}
