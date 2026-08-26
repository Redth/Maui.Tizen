#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Maps processed MAUI resources onto their location inside a Tizen TPK.
	/// </summary>
	/// <remarks>
	/// Tizen resources are laid out below a <c>res/</c> or <c>shared/</c> root (for example
	/// <c>res/contents/default_All-HDPI</c> for images and <c>shared/res/xhdpi</c> for app icons).
	/// The Resizetizer publishes absolute paths through <c>MauiProcessedImage</c>, so rather than
	/// depending on the Resizetizer's private intermediate path properties this task recovers the
	/// resource root by locating the innermost recognized <c>res</c> / <c>shared</c> ancestor below
	/// the build's own output directory, and derives the TPK sub directory from what remains.
	/// </remarks>
	public class ComputeTizenResourceLayout : Task
	{
		public ITaskItem[]? ProcessedImages { get; set; }

		/// <summary>Optional explicit resource root, used when it cannot be inferred.</summary>
		public string? ResourceRootHint { get; set; }

		/// <summary>
		/// The directory below which generated resources live, normally the project's intermediate
		/// output path. Constrains where a resource root may be recognized.
		/// </summary>
		public string? SearchRoot { get; set; }

		/// <summary>The processed images annotated with <c>TizenTpkSubDir</c> metadata.</summary>
		[Output]
		public ITaskItem[] TpkFiles { get; set; } = Array.Empty<ITaskItem>();

		/// <summary>The directory that contains the <c>res</c> / <c>shared</c> resource trees.</summary>
		[Output]
		public string ResourceRoot { get; set; } = string.Empty;

		public override bool Execute()
		{
			try
			{
				ExecuteCore();
			}
			catch (Exception ex)
			{
				Log.LogErrorFromException(ex);
			}

			return !Log.HasLoggedErrors;
		}

		void ExecuteCore()
		{
			var results = new List<ITaskItem>();
			var roots = new SortedSet<string>(StringComparer.Ordinal);

			foreach (var item in ProcessedImages ?? Array.Empty<ITaskItem>())
			{
				var path = item.GetMetadata("FullPath");
				if (string.IsNullOrEmpty(path))
					path = item.ItemSpec;

				var directory = Path.GetDirectoryName(Path.GetFullPath(path));
				if (string.IsNullOrEmpty(directory))
					continue;

				if (!TrySplit(directory!, SearchRoot, out var root, out var subDir))
				{
					Log.LogMessage(MessageImportance.Low, $"Skipping '{path}': it is not below a Tizen 'res' or 'shared' resource root.");
					continue;
				}

				roots.Add(root);

				var result = new TaskItem(item);
				result.ItemSpec = path;
				// Tizen tooling expects a trailing separator on the sub directory.
				result.SetMetadata("TizenTpkSubDir", subDir + Path.DirectorySeparatorChar);
				results.Add(result);
			}

			// Deterministic ordering so repeated builds produce identical item lists.
			TpkFiles = results.OrderBy(i => i.ItemSpec, StringComparer.Ordinal).ToArray();

			if (roots.Count == 1)
			{
				ResourceRoot = roots.First();
			}
			else if (roots.Count > 1)
			{
				// More than one root should not happen, but prefer the shortest so the generated
				// res.xml still covers every bucket.
				ResourceRoot = roots.OrderBy(r => r.Length, Comparer<int>.Default).First();
				Log.LogMessage(MessageImportance.Low, $"Multiple Tizen resource roots were found; using '{ResourceRoot}'.");
			}
			else if (!string.IsNullOrEmpty(ResourceRootHint))
			{
				ResourceRoot = Path.GetFullPath(ResourceRootHint!);
			}
		}

		/// <summary>
		/// Splits an absolute directory into the resource root and the TPK relative sub directory.
		/// </summary>
		/// <remarks>
		/// The anchor is the LAST <c>res</c> / <c>shared</c> segment that begins a recognized Tizen
		/// layout, not the first. Taking the first meant any ancestor directory happening to be
		/// called "res" or "shared" won over the real resource folder: a repository cloned to
		/// <c>/mnt/shared/work</c> anchored on <c>/mnt</c> and produced nonsense TPK sub-directories
		/// for every resource in the build.
		///
		/// When <paramref name="searchRoot"/> is supplied only the portion of the path below it is
		/// considered, so ancestors cannot participate at all. That is the precise fix; the
		/// last-match rule is the fallback for callers that cannot supply a root.
		///
		/// <c>shared/res</c> is deliberately treated as one anchor: app icons live in
		/// <c>shared/res/{dpi}</c>, and anchoring on the inner <c>res</c> would drop the
		/// <c>shared</c> segment and misplace every icon in the TPK.
		/// </remarks>
		internal static bool TrySplit(string directory, string? searchRoot, out string root, out string subDir)
		{
			root = string.Empty;
			subDir = string.Empty;

			var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var segments = trimmed.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

			// Everything at or above the search root is out of bounds.
			var lowerBound = 0;
			if (!string.IsNullOrEmpty(searchRoot))
			{
				var normalizedRoot = Path.GetFullPath(searchRoot!)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

				if (!trimmed.StartsWith(normalizedRoot, PathComparison))
					return false;

				lowerBound = normalizedRoot
					.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
					.Length;
			}

			var anchor = -1;
			for (var i = segments.Length - 1; i >= lowerBound; i--)
			{
				if (IsSegment(segments[i], "res"))
				{
					anchor = i > lowerBound && IsSegment(segments[i - 1], "shared") ? i - 1 : i;
					break;
				}

				if (IsSegment(segments[i], "shared") && i + 1 < segments.Length && IsSegment(segments[i + 1], "res"))
				{
					anchor = i;
					break;
				}
			}

			if (anchor < 0)
				return false;

			var rootLength = trimmed.Length;
			for (var i = segments.Length - 1; i >= anchor; i--)
				rootLength -= segments[i].Length + 1;

			root = trimmed.Substring(0, Math.Max(rootLength, 0));
			if (string.IsNullOrEmpty(root))
				root = Path.GetPathRoot(trimmed) ?? string.Empty;

			subDir = string.Join(Path.DirectorySeparatorChar.ToString(), segments.Skip(anchor));

			return true;
		}

		static bool IsSegment(string segment, string name)
			=> string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);

		static StringComparison PathComparison =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
				? StringComparison.Ordinal
				: StringComparison.OrdinalIgnoreCase;
	}
}
