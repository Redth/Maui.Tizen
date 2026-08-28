#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// The Tizen resource buckets (<c>res/contents/default_All-HDPI</c> and friends) implied by a
	/// set of processed images.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bucket set is the ONLY thing <c>res.xml</c> is derived from, so it is also the state that
	/// has to be recorded for the generating target to be incremental. Two callers therefore need
	/// the same answer: <see cref="GenerateTizenResourceXml"/>, which turns buckets into XML, and
	/// <see cref="ComputeTizenResourceLayout"/>, which publishes them so the targets can persist
	/// them to a file MSBuild's up-to-date check can compare.
	/// </para>
	/// <para>
	/// It lives here rather than being implemented twice because two implementations of "which
	/// bucket is this image in" that disagree produce the worst possible outcome: a recorded state
	/// that says nothing changed while the generated file would have changed.
	/// </para>
	/// </remarks>
	internal static class TizenResourceBuckets
	{
		internal const string ContentsDirectoryName = "contents";

		/// <summary>
		/// Bucket folder names, ordered deterministically so repeated builds produce a byte
		/// identical res.xml and a byte identical recorded state.
		/// </summary>
		internal static IReadOnlyList<string> FromProcessedImages(IEnumerable<ITaskItem>? processedImages)
		{
			var folders = new SortedSet<string>(StringComparer.Ordinal);

			foreach (var item in processedImages ?? Array.Empty<ITaskItem>())
			{
				if (item is null)
					continue;

				var path = item.GetMetadata("FullPath");
				if (string.IsNullOrEmpty(path))
					path = item.ItemSpec;

				var bucket = FromProcessedImagePath(path);
				if (bucket is not null)
					folders.Add(bucket);
			}

			return folders.ToList();
		}

		/// <summary>
		/// The bucket a processed image belongs to, or <c>null</c> when it does not live directly
		/// below a <c>contents</c> directory (app icons, for example, land under
		/// <c>shared/res/xhdpi</c> and describe no bucket at all).
		/// </summary>
		internal static string? FromProcessedImagePath(string? path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			var directory = Path.GetDirectoryName(path);
			if (string.IsNullOrEmpty(directory))
				return null;

			var parent = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);
			if (!string.Equals(parent, ContentsDirectoryName, StringComparison.OrdinalIgnoreCase))
				return null;

			var folder = Path.GetFileName(directory);
			return string.IsNullOrEmpty(folder) ? null : folder;
		}

		/// <summary>Bucket folder names discovered by scanning a <c>res/contents</c> directory.</summary>
		internal static IReadOnlyList<string> FromContentsDirectory(string? contentsDirectory)
		{
			var folders = new SortedSet<string>(StringComparer.Ordinal);

			if (!string.IsNullOrEmpty(contentsDirectory) && Directory.Exists(contentsDirectory))
			{
				foreach (var subDir in new DirectoryInfo(contentsDirectory).GetDirectories())
					folders.Add(subDir.Name);
			}

			return folders.ToList();
		}
	}
}
