#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Removes repeated source/destination mappings and rejects different sources that target the
	/// same destination path. Destinations are compared with <see cref="StringComparer.Ordinal"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists because every de-duplication primitive MSBuild offers is case INSENSITIVE:
	/// the <c>RemoveDuplicates</c> task and the <c>Distinct()</c> item function both compare with
	/// <see cref="StringComparer.OrdinalIgnoreCase"/>. Tizen is Linux, so <c>Foo.js</c> and
	/// <c>foo.js</c> are two different files that a Tizen application may legitimately ship side
	/// by side - and an app that did lost one of them from the TPK silently, with a successful
	/// build and a 404 at runtime.
	/// </para>
	/// <para>
	/// The one ordinal primitive MSBuild does have, <c>DistinctWithCase()</c>, is not usable here:
	/// it returns items stripped of their metadata, and the destination-to-source mapping this
	/// pipeline needs lives entirely in metadata.
	/// </para>
	/// <para>
	/// De-duplication is still required. An application that references both this package and
	/// something that already converts the same assets (a direct
	/// <c>Microsoft.AspNetCore.Components.WebView.Maui</c> reference, for instance) contributes
	/// every file twice, and packing the same destination twice produces a corrupt TPK. Identical
	/// source/destination mappings are therefore collapsed. Two different sources may never use
	/// the same destination, because silently choosing the first makes package contents depend on
	/// item ordering.
	/// </para>
	/// </remarks>
	public class SelectDistinctTizenResources : Task
	{
		static readonly StringComparer SourceComparer =
			Environment.OSVersion.Platform == PlatformID.Win32NT
				? StringComparer.OrdinalIgnoreCase
				: StringComparer.Ordinal;

		/// <summary>The items to filter.</summary>
		public ITaskItem[]? Inputs { get; set; }

		/// <summary>
		/// The metadata that carries the destination path. When empty, or when an item does not
		/// define it, the item spec is used instead.
		/// </summary>
		public string? KeyMetadata { get; set; }

		/// <summary>The input items with duplicate destinations removed, order preserved.</summary>
		[Output]
		public ITaskItem[] Filtered { get; set; } = Array.Empty<ITaskItem>();

		/// <summary>The destinations that appeared more than once.</summary>
		[Output]
		public ITaskItem[] Duplicates { get; set; } = Array.Empty<ITaskItem>();

		public override bool Execute()
		{
			var sourceByDestination = new Dictionary<string, string>(StringComparer.Ordinal);
			var filtered = new List<ITaskItem>();
			var duplicates = new List<ITaskItem>();

			foreach (var item in Inputs ?? Array.Empty<ITaskItem>())
			{
				var key = Key(item);
				var source = Source(item);

				// An item with no usable key cannot be reasoned about; keep it rather than
				// silently dropping a file from the package.
				if (string.IsNullOrEmpty(key))
				{
					filtered.Add(item);
					continue;
				}

				if (!sourceByDestination.TryGetValue(key, out var existingSource))
				{
					sourceByDestination.Add(key, source);
					filtered.Add(item);
					continue;
				}

				if (SourceComparer.Equals(existingSource, source))
				{
					duplicates.Add(item);
					continue;
				}

				Log.LogError(
					subcategory: null,
					errorCode: "MAUITIZEN1021",
					helpKeyword: null,
					file: source,
					lineNumber: 0,
					columnNumber: 0,
					endLineNumber: 0,
					endColumnNumber: 0,
					message: $"Tizen resources '{existingSource}' and '{source}' both target "
						+ $"'{key}'. Each TPK resource destination must have exactly one source.");
			}

			Filtered = filtered.ToArray();
			Duplicates = duplicates.ToArray();

			if (duplicates.Count > 0)
			{
				Log.LogMessage(
					MessageImportance.Low,
					"Maui.Tizen: dropped " + duplicates.Count + " duplicate resource destination(s): "
						+ string.Join(", ", duplicates.Select(Key)));
			}

			return !Log.HasLoggedErrors;
		}

		static string Source(ITaskItem item)
		{
			var source = item.GetMetadata("SourcePath");
			if (string.IsNullOrEmpty(source))
				source = item.GetMetadata("FullPath");
			if (string.IsNullOrEmpty(source))
				source = item.ItemSpec;

			return Path.GetFullPath(source);
		}

		string Key(ITaskItem item)
		{
			if (!string.IsNullOrEmpty(KeyMetadata))
			{
				var value = item.GetMetadata(KeyMetadata);
				if (!string.IsNullOrEmpty(value))
					return Normalize(value);
			}

			return Normalize(item.ItemSpec);
		}

		/// <summary>
		/// Normalizes directory separators and repeated separators. Casing is deliberately left
		/// alone; that is the whole point of this task.
		/// </summary>
		static string Normalize(string value)
		{
			var normalized = value.Replace('\\', '/').TrimStart('/');
			while (normalized.Contains("//"))
				normalized = normalized.Replace("//", "/");

			return normalized;
		}
	}
}
