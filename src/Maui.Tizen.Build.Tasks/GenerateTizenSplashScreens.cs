#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SkiaSharp;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Produces the per-resolution / per-orientation splash images that a Tizen TPK expects under
	/// <c>shared/res/splash</c>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>TizenSplashUpdater</c> in dotnet/maui, with one deliberate change: instead of
	/// re-running the Resizetizer's own rasterizer, this task consumes the already processed images
	/// published through the public <c>MauiProcessedImage</c> contract (dotnet/maui PR 36653). The
	/// <c>MauiSplashScreen</c> item is added to <c>MauiImage</c> before image processing, so the
	/// scaled sources are guaranteed to exist in the Tizen resource buckets, and this task only has
	/// to perform the Tizen specific letterbox composition onto an HD / FHD canvas.
	///
	/// The resulting resolution/orientation to source map is persisted next to the generated images
	/// so that manifest generation stays correct on incremental builds where this task is skipped.
	/// </remarks>
	public class GenerateTizenSplashScreens : Task
	{
		public const string SplashDirectoryName = "splash";
		public const string SplashMapFileName = "tizen-splash.map";

		static readonly SKSizeI HdSize = new SKSizeI(720, 1080);
		static readonly SKSizeI FhdSize = new SKSizeI(1080, 1920);
		static readonly string[] Orientations = { "portrait", "landscape" };

		[Required]
		public ITaskItem[] MauiSplashScreen { get; set; } = Array.Empty<ITaskItem>();

		/// <summary>The <c>MauiProcessedImage</c> items published by the Resizetizer.</summary>
		public ITaskItem[] ProcessedImages { get; set; } = Array.Empty<ITaskItem>();

		[Required]
		public string IntermediateOutputPath { get; set; } = null!;

		[Output]
		public ITaskItem[] SplashScreens { get; set; } = Array.Empty<ITaskItem>();

		/// <summary>Resolution / orientation entries consumed by <see cref="GenerateTizenManifest"/>.</summary>
		[Output]
		public ITaskItem[] SplashScreenEntries { get; set; } = Array.Empty<ITaskItem>();

		[Output]
		public string SplashScreenMapFile { get; set; } = null!;

		public override bool Execute()
		{
			try
			{
				// SkiaSharp's native library is not resolvable by default from inside an MSBuild
				// task assembly; see SkiaSharpHost for why. This must run before the first call
				// into Skia, which is the ResizeImageInfo/SKImage work below.
				SkiaSharpHost.EnsureNativeLibraryResolved();

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
			if (MauiSplashScreen.Length == 0)
				return;

			var splashInfo = TizenImageInfo.Parse(MauiSplashScreen[0]);
			// Absolute throughout: these paths are handed to the Samsung packaging targets as TPK
			// inputs alongside the image items, which are already absolute. A relative identity
			// here would be resolved against whatever the consuming target's working directory
			// happens to be.
			var intermediateFullPath = Path.GetFullPath(IntermediateOutputPath);
			var splashFullPath = Path.Combine(intermediateFullPath, SplashDirectoryName);
			SplashScreenMapFile = Path.Combine(intermediateFullPath, SplashMapFileName);

			DeletePreviouslyOwnedOutputs(SplashScreenMapFile, intermediateFullPath, splashFullPath);
			Directory.CreateDirectory(splashFullPath);

			var color = splashInfo.Color;
			if (color is null)
			{
				Log.LogWarning($"Unable to parse color for '{splashInfo.Filename}'. Falling back to white.");
				color = SKColors.White;
			}

			var sources = IndexProcessedImages(splashInfo.OutputName);

			// ResizeQuality decides how the processed source is sampled when it is scaled onto the
			// Tizen canvas, which is a different scaling step from the Resizetizer's own DPI
			// resize. Honouring it here is what makes the metadata mean something on this backend:
			// a splash source larger than the target screen is downscaled by THIS task, and the
			// sampling used is the only thing that decides what the device shows.
			if (!TizenImageInfo.TryParseSampling(splashInfo.ResizeQuality, out var sampling))
			{
				Log.LogWarning(
					$"Unrecognized ResizeQuality '{splashInfo.ResizeQuality}' on splash screen '{splashInfo.OutputName}'. " +
					"Expected one of None, Low, Medium or High. Falling back to High.");
				sampling = TizenImageInfo.HighQualitySampling;
			}

			var generated = new List<ITaskItem>();
			var entries = new List<ITaskItem>();
			var map = new List<string>();

			foreach (var dpi in TizenDpiPath.SplashScreen)
			{
				var resolution = dpi.Resolution.ToLowerInvariant();

				if (!sources.TryGetValue(resolution, out var source))
				{
					Log.LogWarning(
						$"No processed image was found for splash screen '{splashInfo.OutputName}' at '{dpi.Path}'. " +
						"Tizen splash screens require MAUI image processing to be enabled.");
					continue;
				}

				foreach (var orientation in Orientations)
				{
					var fileName = $"{splashInfo.OutputName}.{resolution}.{orientation}.png";
					var destination = Path.Combine(splashFullPath, fileName);

					Compose(source, GetScreenSize(resolution, orientation), color.Value, sampling, destination);

					var relative = $"{SplashDirectoryName}/{fileName}";

					var generatedItem = new TaskItem(destination);
					generatedItem.SetMetadata("TizenTpkSubDir", Path.Combine("shared", "res", SplashDirectoryName));
					generated.Add(generatedItem);

					var entry = new TaskItem(relative);
					entry.SetMetadata("Resolution", resolution);
					entry.SetMetadata("Orientation", orientation);
					entries.Add(entry);

					map.Add($"{resolution}|{orientation}|{relative}");
				}
			}

			// Deterministic ordering keeps generated manifests byte stable between builds.
			map.Sort(StringComparer.Ordinal);

			File.WriteAllLines(SplashScreenMapFile, map);

			SplashScreens = generated.ToArray();
			SplashScreenEntries = entries.ToArray();
		}

		static void DeletePreviouslyOwnedOutputs(string mapFile, string intermediatePath, string splashPath)
		{
			foreach (var entry in ReadMap(mapFile))
			{
				var relative = entry.Source.Replace('/', Path.DirectorySeparatorChar);
				var candidate = Path.GetFullPath(Path.Combine(intermediatePath, relative));
				var splashPrefix = splashPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;

				if (!candidate.StartsWith(
					splashPrefix,
					Environment.OSVersion.Platform == PlatformID.Win32NT
						? StringComparison.OrdinalIgnoreCase
						: StringComparison.Ordinal))
				{
					continue;
				}

				if (File.Exists(candidate))
					File.Delete(candidate);
			}
		}

		/// <summary>
		/// Buckets the processed images by their Tizen resolution folder, e.g. an image written to
		/// <c>res/contents/default_All-MDPI/splash.png</c> is indexed as <c>mdpi</c>.
		/// </summary>
		Dictionary<string, string> IndexProcessedImages(string outputName)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var item in ProcessedImages ?? Array.Empty<ITaskItem>())
			{
				var path = item.GetMetadata("FullPath");
				if (string.IsNullOrEmpty(path))
					path = item.ItemSpec;

				if (!string.Equals(Path.GetFileNameWithoutExtension(path), outputName, StringComparison.OrdinalIgnoreCase))
					continue;

				var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
				var separator = folder.LastIndexOf('-');
				if (separator < 0)
					continue;

				var resolution = folder.Substring(separator + 1).ToLowerInvariant();
				if (!File.Exists(path))
					continue;

				// Prefer the highest scale available for a bucket; identical names cannot collide
				// because the Resizetizer writes exactly one file per bucket.
				result[resolution] = path;
			}

			return result;
		}

		static SKSizeI GetScreenSize(string resolution, string orientation)
		{
			var size = string.Equals(resolution, "mdpi", StringComparison.OrdinalIgnoreCase) ? HdSize : FhdSize;

			return string.Equals(orientation, "portrait", StringComparison.Ordinal)
				? size
				: new SKSizeI(size.Height, size.Width);
		}

		static void Compose(string sourceFilePath, SKSizeI screenSize, SKColor color, SKSamplingOptions sampling, string destFilePath)
		{
			using var img = SKImage.FromEncodedData(sourceFilePath);
			if (img is null)
				throw new InvalidDataException($"Unable to decode splash screen source image '{sourceFilePath}'.");

			var info = new SKImageInfo(screenSize.Width, screenSize.Height);
			using var surface = SKSurface.Create(info);

			var canvas = surface.Canvas;
			canvas.Clear(color);

			using var paint = new SKPaint { IsAntialias = true };

			var left = screenSize.Width <= img.Width ? 0 : (screenSize.Width - img.Width) / 2;
			var top = screenSize.Height <= img.Height ? 0 : (screenSize.Height - img.Height) / 2;
			var right = screenSize.Width <= img.Width ? left + screenSize.Width : left + img.Width;
			var bottom = screenSize.Height <= img.Height ? top + screenSize.Height : top + img.Height;

			canvas.DrawImage(img, new SKRect(left, top, right, bottom), sampling, paint);
			canvas.Flush();

			using var snapshot = surface.Snapshot();
			using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = File.Create(destFilePath);
			data.SaveTo(stream);
		}

		/// <summary>
		/// Reads a previously persisted splash map. Used by manifest generation when the splash
		/// task itself was skipped because its inputs were up to date.
		/// </summary>
		public static IReadOnlyList<(string Resolution, string Orientation, string Source)> ReadMap(string? mapFile)
		{
			var entries = new List<(string, string, string)>();

			if (string.IsNullOrEmpty(mapFile) || !File.Exists(mapFile))
				return entries;

			foreach (var line in File.ReadAllLines(mapFile))
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				var parts = line.Split('|');
				if (parts.Length != 3)
					continue;

				entries.Add((parts[0], parts[1], parts[2]));
			}

			return entries;
		}
	}
}
