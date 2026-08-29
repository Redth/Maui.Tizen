namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards the Wave B integration with Core's finalized image loader and composition seam.
/// </summary>
public class ImageLoaderIntegrationTests
{
	static string Read(params string[] parts) => File.ReadAllText(RepoPaths.Combine(parts));

	[Theory]
	[InlineData("Image/TizenImageHandler.cs")]
	[InlineData("ImageButton/TizenImageButtonHandler.cs")]
	[InlineData("SwipeItemMenuItem/TizenSwipeItemMenuItemHandler.cs")]
	public void ImageHandlersUseTheSharedLoaderAndDispatcher(string relativePath)
	{
		var source = Read(
			new[] { "src", "Maui.Tizen.Core", "Handlers" }
				.Concat(relativePath.Split('/'))
				.ToArray());

		Assert.Contains("TizenImageLoader<TizenImageSource>", source, StringComparison.Ordinal);
		Assert.Contains("TizenDispatchExtensions.CaptureDispatcher(handler)", source, StringComparison.Ordinal);
		Assert.Contains("_sourceLoader.LoadPartAsync(", source, StringComparison.Ordinal);
		Assert.Contains(".FireAndForget(handler)", source, StringComparison.Ordinal);
		Assert.Contains("_sourceLoader.Dispose", source, StringComparison.Ordinal);
		Assert.DoesNotContain("TizenImageSourceLoader", source, StringComparison.Ordinal);
		Assert.DoesNotContain("ApplyImageSourceAsync", source, StringComparison.Ordinal);
	}

	[Fact]
	public void ObsoleteWaveBImageLoaderIsNotShipped()
	{
		var obsoletePath = RepoPaths.Combine(
			"src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenImageSourceLoader.cs");
		var sourceGraph = Read("eng", "Maui.Tizen.Core.Sources.props");

		Assert.False(File.Exists(obsoletePath));
		Assert.DoesNotContain("TizenImageSourceLoader.cs", sourceGraph, StringComparison.Ordinal);
	}

	[Fact]
	public void UriAndFontServicesExtendTheSingleSharedSeam()
	{
		var shared = Read(
			"src", "Maui.Tizen.Core", "ImageSources",
			"TizenImageSourceServiceCollectionExtensions.cs");
		var waveB = Read(
			"src", "Maui.Tizen.Core", "ImageSources",
			"ImageSourceServiceCollectionExtensions.WaveB.cs");
		var services = Read(
			"src", "Maui.Tizen.Core", "ImageSources", "TizenWaveBImageSourceServices.cs");

		Assert.Contains("AddWaveBImageSources(services)", shared, StringComparison.Ordinal);
		Assert.Contains("AddService<IUriImageSource>", waveB, StringComparison.Ordinal);
		Assert.Contains("AddService<IFontImageSource>", waveB, StringComparison.Ordinal);
		Assert.DoesNotContain("AddTizenUriAndFontImageSources", services, StringComparison.Ordinal);
	}

}
