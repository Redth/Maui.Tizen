// Ported from dotnet/maui as part of the Maui.Tizen extraction.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Loads an <see cref="IStreamImageSource"/> by buffering it into a Tizen image source.</summary>
	public class TizenStreamImageSourceService : TizenImageSourceService, ITizenImageSourceService<IStreamImageSource>
	{
		public TizenStreamImageSourceService()
			: this(null)
		{
		}

		public TizenStreamImageSourceService(ILogger<TizenStreamImageSourceService>? logger = null)
			: base(logger)
		{
		}

		public override Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default) =>
			GetImageAsync((IStreamImageSource)imageSource, cancellationToken);

		public async Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IStreamImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return null;

			try
			{
				var stream = await imageSource.GetStreamAsync(cancellationToken);
				var image = new MauiImageSource();

				await image.LoadSource(stream);

				return new ImageSourceServiceResult(image, image.Dispose);
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to load image stream.");
				throw;
			}
		}
	}
}
