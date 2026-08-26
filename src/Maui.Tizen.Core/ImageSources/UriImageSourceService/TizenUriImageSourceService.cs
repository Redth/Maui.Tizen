// Ported from dotnet/maui as part of the Maui.Tizen extraction.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui
{
	/// <summary>Loads an <see cref="IUriImageSource"/> by handing the URI to the Tizen image loader.</summary>
	public class TizenUriImageSourceService : TizenImageSourceService, ITizenImageSourceService<IUriImageSource>
	{
		public TizenUriImageSourceService()
			: this(null)
		{
		}

		public TizenUriImageSourceService(ILogger<TizenUriImageSourceService>? logger = null)
			: base(logger)
		{
		}

		public override Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default) =>
			GetImageAsync((IUriImageSource)imageSource, cancellationToken);

		public Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IUriImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return FromResult(null);

			var uri = imageSource.Uri;

			try
			{
				var image = new MauiImageSource
				{
					ResourceUrl = uri.AbsoluteUri
				};

				var result = new ImageSourceServiceResult(image, image.Dispose);
				return FromResult(result);
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to load image URI '{Uri}'.", uri);
				throw;
			}
		}
	}
}
