#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Removes items that share a destination path, comparing destinations with
	/// <see cref="StringComparer.Ordinal"/> and preserving the first occurrence of each.
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
	/// destinations are therefore still collapsed - only the case-insensitive collision is
	/// dropped.
	/// </para>
	/// </remarks>
	public class SelectDistinctTizenResources : Task
	{
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
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var filtered = new List<ITaskItem>();
			var duplicates = new List<ITaskItem>();

			foreach (var item in Inputs ?? Array.Empty<ITaskItem>())
			{
				var key = Key(item);

				// An item with no usable key cannot be reasoned about; keep it rather than
				// silently dropping a file from the package.
				if (string.IsNullOrEmpty(key) || seen.Add(key))
					filtered.Add(item);
				else
					duplicates.Add(item);
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
		/// Normalizes only the directory separator. Casing is deliberately left alone; that is the
		/// whole point of this task.
		/// </summary>
		static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
	}
}
