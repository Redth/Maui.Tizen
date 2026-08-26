#nullable enable
using System;
using System.IO;
using Microsoft.Build.Framework;
using SkiaSharp;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// The subset of <c>ResizeImageInfo</c> metadata this package needs in order to name and colour
	/// Tizen resources. Unlike the MAUI internal type this does not require the source file to
	/// exist, because manifest generation legitimately runs before image processing has produced
	/// anything on disk.
	/// </summary>
	public sealed class TizenImageInfo
	{
		public string? ItemSpec { get; private set; }

		public string? Filename { get; private set; }

		public string? Alias { get; private set; }

		public SKColor? Color { get; private set; }

		public SKColor? DarkColor { get; private set; }

		/// <summary>
		/// The base file name (without extension) that the Resizetizer uses for generated outputs.
		/// Mirrors <c>ResizeImageInfo.OutputName</c>: the <c>Link</c> alias wins, otherwise the
		/// source file name.
		/// </summary>
		public string OutputName =>
			!string.IsNullOrWhiteSpace(Alias)
				? Path.GetFileNameWithoutExtension(Alias)
				: Path.GetFileNameWithoutExtension(Filename ?? ItemSpec ?? string.Empty);

		public static TizenImageInfo Parse(ITaskItem item)
		{
			if (item is null)
				throw new ArgumentNullException(nameof(item));

			var fullPath = item.GetMetadata("FullPath");

			return new TizenImageInfo
			{
				ItemSpec = item.ItemSpec,
				Filename = string.IsNullOrEmpty(fullPath) ? item.ItemSpec : fullPath,
				Alias = item.GetMetadata("Link"),
				Color = TizenColorTable.Parse(item.GetMetadata("Color")),
				DarkColor = TizenColorTable.Parse(item.GetMetadata("DarkColor")),
			};
		}
	}
}
