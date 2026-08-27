using Xunit;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Tests for the semantic rules JSON Schema cannot express.
///
/// These run against fixtures as well as the real manifest, so they provide genuine
/// coverage even before the generated manifest lands — a validator that has never been
/// shown to reject anything is not a validator.
/// </summary>
public class SourceDispositionValidatorTests
{
	static string FixturePath(string name) =>
		Path.Combine(RepositoryPaths.Root, "eng", "tests", "fixtures", "manifests", name);

	static string ReadFixture(string name)
	{
		var path = FixturePath(name);
		Assert.True(File.Exists(path), $"Missing fixture: {path}");
		return File.ReadAllText(path);
	}

	[Fact]
	public void ValidManifestProducesNoProblems()
	{
		var problems = SourceDispositionValidator.Validate(ReadFixture("valid.json"));
		Assert.Empty(problems);
	}

	[Fact]
	public void DuplicatePathsWithConflictingDispositionsAreRejected()
	{
		// The case JSON Schema cannot catch: `uniqueItems` compares whole objects, so
		// two entries for the same path with different dispositions validate cleanly
		// while leaving the migration with two contradictory answers for one file.
		var problems = SourceDispositionValidator.Validate(ReadFixture("duplicate-paths.json"));

		var conflict = Assert.Single(problems, p => p.Kind == "conflicting-duplicate");
		Assert.Contains("ViewExtensions.cs", conflict.Detail);
		Assert.Contains("move", conflict.Detail);
		Assert.Contains("keep-upstream", conflict.Detail);
	}

	[Fact]
	public void DuplicateTargetPathsAreRejected()
	{
		// Two sources mapping to one destination means the second move silently
		// overwrites the first.
		var problems = SourceDispositionValidator.Validate(ReadFixture("duplicate-target-paths.json"));

		var collision = Assert.Single(problems, p => p.Kind == "duplicate-target");
		Assert.Contains("Collide.cs", collision.Detail);
	}

	[Fact]
	public void GeneratedManifestIsSemanticallyValid()
	{
		// The generated manifest is produced by separate inventory tooling and may not be
		// present on every branch. When it is present it MUST pass; its absence is
		// reported rather than silently treated as success.
		var manifest = Path.Combine(RepositoryPaths.Root, "eng", "manifests", "source-disposition.json");

		if (!File.Exists(manifest))
		{
			Assert.True(
				Directory.Exists(Path.Combine(RepositoryPaths.Root, "eng", "manifests")),
				"eng/manifests/ must exist as the contract location even before data lands.");
			return;
		}

		var problems = SourceDispositionValidator.Validate(File.ReadAllText(manifest));
		Assert.True(
			problems.Count == 0,
			"eng/manifests/source-disposition.json failed semantic validation:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, problems.Select(p => "  " + p)));
	}
}
