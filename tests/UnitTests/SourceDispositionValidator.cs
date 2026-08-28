using System.Text.Json;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Semantic validation for the source disposition manifest — the rules JSON Schema
/// cannot express.
///
/// JSON Schema can enforce the shape of an entry, but it cannot enforce uniqueness of a
/// *key within an array*. <c>uniqueItems</c> compares whole objects, so two entries for
/// the same <c>path</c> with different dispositions are perfectly valid schema-wise and
/// completely wrong semantically: the migration would have two contradictory answers for
/// one file, and which one wins depends on whichever tool reads the array last.
///
/// The manifest's entire purpose is that every Tizen-relevant file has exactly one
/// decision, so that gap has to be closed here rather than in the schema.
/// </summary>
public static class SourceDispositionValidator
{
	public sealed record Problem(string Kind, string Detail)
	{
		public override string ToString() => $"{Kind}: {Detail}";
	}

	public static IReadOnlyList<Problem> Validate(string json)
	{
		var problems = new List<Problem>();

		using var doc = JsonDocument.Parse(
			json,
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

		if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
		{
			problems.Add(new Problem("structure", "manifest has no 'entries' array"));
			return problems;
		}

		var byPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		var byTargetPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		foreach (var entry in entries.EnumerateArray())
		{
			var path = entry.TryGetProperty("path", out var p) ? p.GetString() : null;
			var disposition = entry.TryGetProperty("disposition", out var d) ? d.GetString() : null;

			if (string.IsNullOrWhiteSpace(path))
			{
				problems.Add(new Problem("structure", "entry with missing or empty 'path'"));
				continue;
			}

			if (!byPath.TryGetValue(path!, out var dispositions))
				byPath[path!] = dispositions = new List<string>();
			dispositions.Add(disposition ?? "<none>");

			// Only move/rename/rebuild carry a destination; keep-upstream and exclude do not.
			if (entry.TryGetProperty("targetPath", out var tp) && tp.ValueKind == JsonValueKind.String)
			{
				var target = tp.GetString()!;
				if (!byTargetPath.TryGetValue(target, out var sources))
					byTargetPath[target] = sources = new List<string>();
				sources.Add(path!);
			}
		}

		foreach (var (path, dispositions) in byPath.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			var distinct = dispositions.Distinct(StringComparer.Ordinal).ToList();
			problems.Add(distinct.Count > 1
				// The dangerous case: the manifest disagrees with itself.
				? new Problem("conflicting-duplicate", $"'{path}' appears {dispositions.Count} times with differing dispositions [{string.Join(", ", distinct)}]")
				// Still invalid — one file, one decision, one row.
				: new Problem("duplicate", $"'{path}' appears {dispositions.Count} times"));
		}

		foreach (var (target, sources) in byTargetPath.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			// Two sources landing on one destination means the later move silently
			// overwrites the earlier one.
			problems.Add(new Problem(
				"duplicate-target",
				$"targetPath '{target}' is claimed by {sources.Count} entries [{string.Join(", ", sources)}]"));
		}

		return problems;
	}
}
