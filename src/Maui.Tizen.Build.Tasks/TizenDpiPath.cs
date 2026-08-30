#nullable enable
using System.Collections.Generic;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// The Tizen DPI buckets used by the Resizetizer when <c>ResizetizerPlatformType</c> is
	/// <c>tizen</c>. Ported from <c>DpiPath.Tizen</c> in dotnet/maui so that this package can map
	/// processed resources onto TPK layout without referencing MAUI source projects.
	/// </summary>
	public sealed class TizenDpiPath
	{
		public TizenDpiPath(string path, decimal scale, string? nameSuffix = null, string? scaleSuffix = null, int width = 0, int height = 0)
		{
			Path = path;
			Scale = scale;
			NameSuffix = nameSuffix;
			ScaleSuffix = scaleSuffix;
			Width = width;
			Height = height;
		}

		public string Path { get; }

		public decimal Scale { get; }

		public string? NameSuffix { get; }

		public string? ScaleSuffix { get; }

		public int Width { get; }

		public int Height { get; }

		public string FileSuffix => string.Concat(NameSuffix, ScaleSuffix);

		/// <summary>The bucket name, e.g. <c>MDPI</c> for <c>res/contents/default_All-MDPI</c>.</summary>
		public string Resolution
		{
			get
			{
				var index = Path.LastIndexOf('-');
				return index < 0 ? string.Empty : Path.Substring(index + 1);
			}
		}

		public static TizenDpiPath Original => new TizenDpiPath("res", 1.0m);

		public static IReadOnlyList<TizenDpiPath> Image { get; } = new[]
		{
			new TizenDpiPath("res/contents/default_All-LDPI", 0.8m),
			new TizenDpiPath("res/contents/default_All-MDPI", 1.0m),
			new TizenDpiPath("res/contents/default_All-HDPI", 1.5m),
			new TizenDpiPath("res/contents/default_All-XHDPI", 2.0m),
			new TizenDpiPath("res/contents/default_All-XXHDPI", 3.0m),
		};

		public static IReadOnlyList<TizenDpiPath> AppIcon { get; } = new[]
		{
			new TizenDpiPath("shared/res/hdpi", 1.0m, null, ".high", 78, 78),
			new TizenDpiPath("shared/res/xhdpi", 1.0m, null, ".xhigh", 117, 117),
		};

		public static IReadOnlyList<TizenDpiPath> SplashScreen { get; } = new[]
		{
			new TizenDpiPath("res/contents/default_All-MDPI", 1.0m),
			new TizenDpiPath("res/contents/default_All-HDPI", 1.5m),
		};

		/// <summary>
		/// Maps a Tizen resource bucket folder name onto the <c>screen-dpi-range</c> value expected
		/// by <c>res.xml</c>.
		/// </summary>
		public static IReadOnlyDictionary<string, string> ResolutionRanges { get; } = new Dictionary<string, string>(System.StringComparer.Ordinal)
		{
			{ "LDPI", "from 0 to 240" },
			{ "MDPI", "from 241 to 300" },
			{ "HDPI", "from 301 to 380" },
			{ "XHDPI", "from 381 to 480" },
			{ "XXHDPI", "from 481 to 600" },
		};
	}
}
