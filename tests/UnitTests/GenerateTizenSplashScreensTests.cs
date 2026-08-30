using System;
using System.IO;
using System.Linq;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

public class GenerateTizenSplashScreensTests : TestBase
{
	private (GenerateTizenSplashScreens Task, RecordingBuildEngine Engine, string Intermediate) CreateTask(
		string splashName = "splash.png",
		string? color = "#512BD4",
		params string[] buckets)
	{
		var sourceRoot = CreateTempDirectory();
		var splashSource = WritePng(Path.Combine(sourceRoot, splashName), 128, 128);

		var processedRoot = CreateTempDirectory();
		var processed = buckets
			.Select(bucket => Item(WritePng(
				Path.Combine(processedRoot, "res", "contents", $"default_All-{bucket}", Path.ChangeExtension(splashName, ".png")),
				128,
				128)))
			.ToArray();

		var metadata = color is null
			? Array.Empty<(string, string)>()
			: new[] { ("Color", color) };

		var task = new GenerateTizenSplashScreens
		{
			MauiSplashScreen = new[] { Item(splashSource, metadata) },
			ProcessedImages = processed,
			IntermediateOutputPath = CreateTempDirectory(),
		};

		return (task, task.UseRecordingEngine(), task.IntermediateOutputPath);
	}

	[Fact]
	public void ComposesPortraitAndLandscapeForEachBucket()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI", "HDPI");

		Assert.True(task.Execute(), engine.AllErrors());

		var names = task.SplashScreens.Select(i => Path.GetFileName(i.ItemSpec)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

		Assert.Equal(
			new[]
			{
				"splash.hdpi.landscape.png",
				"splash.hdpi.portrait.png",
				"splash.mdpi.landscape.png",
				"splash.mdpi.portrait.png",
			},
			names);

		foreach (var item in task.SplashScreens)
		{
			Assert.True(File.Exists(item.ItemSpec));
			Assert.Equal(Path.Combine("shared", "res", "splash"), item.GetMetadata("TizenTpkSubDir"));
		}

		Assert.True(File.Exists(Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName)));
	}

	[Fact]
	public void UsesHdCanvasForMdpiAndFhdCanvasForHdpi()
	{
		var (task, engine, _) = CreateTask("splash.png", "#512BD4", "MDPI", "HDPI");

		Assert.True(task.Execute(), engine.AllErrors());

		(int Width, int Height) SizeOf(string suffix)
		{
			var path = task.SplashScreens.Single(i => i.ItemSpec.EndsWith(suffix, StringComparison.Ordinal)).ItemSpec;
			using var bitmap = SkiaSharp.SKBitmap.Decode(path);
			return (bitmap.Width, bitmap.Height);
		}

		Assert.Equal((720, 1080), SizeOf("splash.mdpi.portrait.png"));
		Assert.Equal((1080, 720), SizeOf("splash.mdpi.landscape.png"));
		Assert.Equal((1080, 1920), SizeOf("splash.hdpi.portrait.png"));
		Assert.Equal((1920, 1080), SizeOf("splash.hdpi.landscape.png"));
	}

	[Fact]
	public void WritesADeterministicSortedMap()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI", "HDPI");

		Assert.True(task.Execute(), engine.AllErrors());

		var lines = File.ReadAllLines(Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName));

		Assert.Equal(
			new[]
			{
				"hdpi|landscape|splash/splash.hdpi.landscape.png",
				"hdpi|portrait|splash/splash.hdpi.portrait.png",
				"mdpi|landscape|splash/splash.mdpi.landscape.png",
				"mdpi|portrait|splash/splash.mdpi.portrait.png",
			},
			lines);
	}

	[Fact]
	public void MapRoundTripsThroughReadMap()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI");

		Assert.True(task.Execute(), engine.AllErrors());

		var entries = GenerateTizenSplashScreens.ReadMap(Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName));

		Assert.Equal(2, entries.Count);
		Assert.All(entries, e => Assert.Equal("mdpi", e.Resolution));
		Assert.Contains(entries, e => e.Orientation == "portrait");
		Assert.Contains(entries, e => e.Orientation == "landscape");
	}

	[Fact]
	public void ReadMapToleratesMissingFile()
	{
		Assert.Empty(GenerateTizenSplashScreens.ReadMap(Path.Combine(CreateTempDirectory(), "absent.map")));
	}

	/// <summary>
	/// Missing processed images mean MAUI image processing was disabled. The task must warn rather
	/// than fabricate a splash screen from nothing.
	/// </summary>
	[Fact]
	public void WarnsWhenProcessedImagesAreMissing()
	{
		var (task, engine, _) = CreateTask("splash.png", "#512BD4");

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Empty(task.SplashScreens);
		Assert.Contains("require MAUI image processing to be enabled", engine.AllWarnings());
	}

	[Fact]
	public void FallsBackToWhiteAndWarnsWhenColorIsMissing()
	{
		var (task, engine, _) = CreateTask("splash.png", null, "MDPI");

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Contains("Unable to parse color", engine.AllWarnings());
		Assert.NotEmpty(task.SplashScreens);
	}

	[Fact]
	public void PreservesUnownedOutputsOnRerun()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI");
		Assert.True(task.Execute(), engine.AllErrors());

		var stale = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashDirectoryName, "stale.png");
		File.WriteAllText(stale, string.Empty);

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.True(File.Exists(stale));
	}

	[Fact]
	public void RemovesPreviouslyMappedAliasOutputsOnRerun()
	{
		var intermediate = CreateTempDirectory();

		GenerateTizenSplashScreens CreateAliasedTask(string alias)
		{
			var sourceRoot = CreateTempDirectory();
			var source = WritePng(Path.Combine(sourceRoot, "source.png"), 64, 64);
			var processedRoot = CreateTempDirectory();
			var processed = WritePng(
				Path.Combine(processedRoot, "res", "contents", "default_All-MDPI", alias + ".png"),
				64,
				64);

			var result = new GenerateTizenSplashScreens
			{
				MauiSplashScreen = new[] { Item(source, ("Link", alias + ".png"), ("Color", "White")) },
				ProcessedImages = new[] { Item(processed) },
				IntermediateOutputPath = intermediate,
			};
			result.UseRecordingEngine();
			return result;
		}

		var first = CreateAliasedTask("old-name");
		Assert.True(first.Execute());
		var oldOutputs = first.SplashScreens.Select(item => item.ItemSpec).ToArray();
		Assert.All(oldOutputs, path => Assert.True(File.Exists(path)));

		var second = CreateAliasedTask("new-name");
		Assert.True(second.Execute());

		Assert.All(oldOutputs, path => Assert.False(File.Exists(path)));
		Assert.All(second.SplashScreens, item => Assert.StartsWith("new-name.", Path.GetFileName(item.ItemSpec), StringComparison.Ordinal));
	}

	[Fact]
	public void RefusesToDeleteThroughALinkedSplashDirectory()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI");
		var external = CreateTempDirectory();
		var externalOutput = Path.Combine(external, "splash.mdpi.portrait.png");
		File.WriteAllText(externalOutput, "must survive");

		var splashDirectory = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashDirectoryName);
		try
		{
			Directory.CreateSymbolicLink(splashDirectory, external);
		}
		catch (UnauthorizedAccessException)
		{
			// Windows agents without Developer Mode cannot create an unprivileged directory link.
			return;
		}
		catch (IOException)
		{
			// Some filesystems do not support symbolic links.
			return;
		}

		File.WriteAllText(
			Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName),
			"mdpi|portrait|splash/splash.mdpi.portrait.png");

		Assert.False(task.Execute());
		Assert.Contains("symbolic link or reparse point", engine.AllErrors(), StringComparison.Ordinal);
		Assert.Equal("must survive", File.ReadAllText(externalOutput));

		var cleanup = new DeleteTizenSplashOutputs
		{
			SplashScreenMapFile = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName),
			IntermediateOutputPath = intermediate,
		};
		var cleanupEngine = cleanup.UseRecordingEngine();

		Assert.False(cleanup.Execute());
		Assert.Contains("symbolic link or reparse point", cleanupEngine.AllErrors(), StringComparison.Ordinal);
		Assert.Equal("must survive", File.ReadAllText(externalOutput));
	}

	[Fact]
	public void RefusesToWriteThroughADanglingSplashMapLink()
	{
		var (task, engine, intermediate) = CreateTask("splash.png", "#512BD4", "MDPI");
		var external = CreateTempDirectory();
		var externalMap = Path.Combine(external, "created-through-link.map");
		var map = Path.Combine(intermediate, GenerateTizenSplashScreens.SplashMapFileName);

		try
		{
			File.CreateSymbolicLink(map, externalMap);
		}
		catch (UnauthorizedAccessException)
		{
			return;
		}
		catch (IOException)
		{
			return;
		}

		Assert.False(File.Exists(externalMap));
		Assert.False(task.Execute());
		Assert.Contains("symbolic link or reparse point", engine.AllErrors(), StringComparison.Ordinal);
		Assert.False(File.Exists(externalMap));
	}

	[Fact]
	public void HonoursTheLinkAliasWhenNamingOutputs()
	{
		var sourceRoot = CreateTempDirectory();
		var source = WritePng(Path.Combine(sourceRoot, "source.png"), 64, 64);

		var processedRoot = CreateTempDirectory();
		var processed = WritePng(Path.Combine(processedRoot, "res", "contents", "default_All-MDPI", "aliased.png"), 64, 64);

		var task = new GenerateTizenSplashScreens
		{
			MauiSplashScreen = new[] { Item(source, ("Link", "aliased.png"), ("Color", "White")) },
			ProcessedImages = new[] { Item(processed) },
			IntermediateOutputPath = CreateTempDirectory(),
		};
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.All(task.SplashScreens, i => Assert.StartsWith("aliased.", Path.GetFileName(i.ItemSpec), StringComparison.Ordinal));
	}

	/// <summary>
	/// Writes a PNG of one-pixel vertical stripes, whose appearance after downscaling depends
	/// entirely on the sampling used.
	/// </summary>
	/// <remarks>
	/// A flat colour cannot show a resampling difference - every filter returns the same colour -
	/// so a quality test built on the usual solid test image would pass whether or not the setting
	/// was honoured.
	/// </remarks>
	private static string WriteStripedPng(string path, int size)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		using var bitmap = new SkiaSharp.SKBitmap(size, size);
		for (var y = 0; y < size; y++)
		{
			for (var x = 0; x < size; x++)
				bitmap.SetPixel(x, y, x % 2 == 0 ? SkiaSharp.SKColors.White : SkiaSharp.SKColors.Black);
		}

		using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(path);
		data.SaveTo(stream);

		return path;
	}

	private static GenerateTizenSplashScreens ComposeWithQuality(string processedSource, string? resizeQuality, string intermediate)
	{
		var metadata = resizeQuality is null
			? new[] { ("Color", "#512BD4") }
			: new[] { ("Color", "#512BD4"), ("ResizeQuality", resizeQuality) };

		var task = new GenerateTizenSplashScreens
		{
			MauiSplashScreen = new[] { Item(processedSource, metadata) },
			ProcessedImages = new[] { Item(processedSource) },
			IntermediateOutputPath = intermediate,
		};

		task.UseRecordingEngine();
		return task;
	}

	/// <summary>
	/// <c>ResizeQuality</c> must change the composed image when the source is scaled onto the
	/// canvas.
	/// </summary>
	/// <remarks>
	/// The Tizen composition is a second scaling step, separate from the Resizetizer's DPI resize:
	/// a splash source larger than the target screen is downscaled by this task, and the sampling
	/// it uses is the only thing that decides what the device shows. The source here is
	/// deliberately larger than the FHD canvas so that scaling actually happens.
	/// </remarks>
	[Fact]
	public void ResizeQualityChangesTheComposedImage()
	{
		var processedRoot = CreateTempDirectory();
		var source = WriteStripedPng(
			Path.Combine(processedRoot, "res", "contents", "default_All-HDPI", "splash.png"),
			2048);

		string HashOf(string? quality)
		{
			var task = ComposeWithQuality(source, quality, CreateTempDirectory());
			Assert.True(task.Execute());

			var portrait = task.SplashScreens.Single(i => i.ItemSpec.EndsWith("hdpi.portrait.png", StringComparison.Ordinal)).ItemSpec;
			return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(portrait)));
		}

		var none = HashOf("None");
		var high = HashOf("High");

		Assert.NotEqual(none, high);

		// The unset value must keep behaving exactly as it always has, so adopting this metadata
		// changes nothing for a project that never sets it.
		Assert.Equal(high, HashOf(null));
		Assert.Equal(high, HashOf(string.Empty));
	}

	/// <summary>
	/// An unrecognized quality is reported rather than silently treated as the default.
	/// </summary>
	[Fact]
	public void AnUnknownResizeQualityWarns()
	{
		var processedRoot = CreateTempDirectory();
		var source = WriteStripedPng(
			Path.Combine(processedRoot, "res", "contents", "default_All-MDPI", "splash.png"),
			256);

		var task = ComposeWithQuality(source, "Highest", CreateTempDirectory());
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Contains("Unrecognized ResizeQuality 'Highest'", engine.AllWarnings());
		Assert.NotEmpty(task.SplashScreens);
	}
}
