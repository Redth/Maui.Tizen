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
		/// The raw <c>ResizeQuality</c> metadata, as authored on the item.
		/// </summary>
		/// <remarks>
		/// Kept as the authored string rather than a parsed enum so that an unrecognized value can
		/// be reported with the text the user wrote.
		/// </remarks>
		public string? ResizeQuality { get; private set; }

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
				ResizeQuality = item.GetMetadata("ResizeQuality"),
			};
		}

		/// <summary>
		/// Maps a MAUI <c>ResizeQuality</c> value onto the SkiaSharp sampling used when an image is
		/// scaled onto a Tizen splash canvas.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The names are the ones MAUI documents (None / Low / Medium / High, the historical
		/// <c>SKFilterQuality</c> members), so an application does not have to learn a second
		/// vocabulary for the Tizen backend. An empty value keeps the previous behaviour - Mitchell
		/// cubic - so adopting this changes no existing output.
		/// </para>
		/// <para>
		/// An unrecognized value returns false rather than silently falling back, so the caller can
		/// warn: a typo that quietly selects "high" is indistinguishable from the setting working.
		/// </para>
		/// </remarks>
		public static bool TryParseSampling(string? resizeQuality, out SKSamplingOptions sampling)
		{
			sampling = HighQualitySampling;

			if (string.IsNullOrWhiteSpace(resizeQuality))
				return true;

			switch (resizeQuality!.Trim().ToLowerInvariant())
			{
				case "none":
					sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
					return true;
				case "low":
					sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
					return true;
				case "medium":
					sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
					return true;
				case "high":
					sampling = HighQualitySampling;
					return true;
				default:
					return false;
			}
		}

		/// <summary>The default: bicubic Mitchell, which is what this backend has always used.</summary>
		public static SKSamplingOptions HighQualitySampling => new SKSamplingOptions(SKCubicResampler.Mitchell);
	}
}
