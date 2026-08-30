using SkiaSharp;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

public class TizenColorTableTests
{
	[Theory]
	[InlineData("#FF0000", 255, 0, 0, 255)]
	[InlineData("#512BD4", 0x51, 0x2B, 0xD4, 255)]
	[InlineData("#80FF0000", 255, 0, 0, 0x80)]
	public void ParsesHexColors(string value, byte r, byte g, byte b, byte a)
	{
		var color = TizenColorTable.Parse(value);

		Assert.NotNull(color);
		Assert.Equal(new SKColor(r, g, b, a), color!.Value);
	}

	[Theory]
	[InlineData("White")]
	[InlineData("white")]
	[InlineData("  White  ")]
	public void ParsesNamedColorsCaseAndWhitespaceInsensitively(string value)
	{
		Assert.Equal(SKColors.White, TizenColorTable.Parse(value));
	}

	[Theory]
	[InlineData("DarkGrey", "DarkGray")]
	[InlineData("DimGrey", "DimGray")]
	[InlineData("LightSlateGrey", "LightSlateGray")]
	public void SupportsGreySpellingAliases(string grey, string gray)
	{
		Assert.Equal(TizenColorTable.Parse(gray), TizenColorTable.Parse(grey));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("definitely-not-a-color")]
	public void ReturnsNullForUnparseableValues(string? value)
	{
		Assert.Null(TizenColorTable.Parse(value));
	}
}

public class TizenDpiPathTests
{
	[Fact]
	public void ImageBucketsMatchTheResizetizerTizenLayout()
	{
		Assert.Equal(
			new[]
			{
				"res/contents/default_All-LDPI",
				"res/contents/default_All-MDPI",
				"res/contents/default_All-HDPI",
				"res/contents/default_All-XHDPI",
				"res/contents/default_All-XXHDPI",
			},
			System.Linq.Enumerable.Select(TizenDpiPath.Image, d => d.Path));
	}

	[Fact]
	public void AppIconSuffixesMatchTheGeneratedFileNames()
	{
		Assert.Equal(".high", TizenDpiPath.AppIcon[0].FileSuffix);
		Assert.Equal(".xhigh", TizenDpiPath.AppIcon[1].FileSuffix);
		Assert.Equal("shared/res/hdpi", TizenDpiPath.AppIcon[0].Path);
		Assert.Equal("shared/res/xhdpi", TizenDpiPath.AppIcon[1].Path);
	}

	[Fact]
	public void SplashScreenBucketsAreMdpiAndHdpi()
	{
		Assert.Equal(new[] { "MDPI", "HDPI" }, System.Linq.Enumerable.Select(TizenDpiPath.SplashScreen, d => d.Resolution));
	}

	[Theory]
	[InlineData("LDPI", "from 0 to 240")]
	[InlineData("MDPI", "from 241 to 300")]
	[InlineData("HDPI", "from 301 to 380")]
	[InlineData("XHDPI", "from 381 to 480")]
	[InlineData("XXHDPI", "from 481 to 600")]
	public void ResolutionRangesMatchUpstream(string resolution, string expected)
	{
		Assert.Equal(expected, TizenDpiPath.ResolutionRanges[resolution]);
	}
}
