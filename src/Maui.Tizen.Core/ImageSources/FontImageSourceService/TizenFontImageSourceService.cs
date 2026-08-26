// Ported from dotnet/maui as part of the Maui.Tizen extraction.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui
{
	/// <summary>
	/// Resolves an <see cref="IFontImageSource"/> on Tizen.
	/// </summary>
	/// <remarks>
	/// UNSUPPORTED, carried over from dotnet/maui: Tizen has no glyph-to-bitmap rasterisation path
	/// wired up, so this returns an empty <see cref="MauiImageSource"/> and the image renders blank
	/// rather than throwing. Behaviour is unchanged from the upstream Tizen backend.
	/// See docs/wave-b-mapper-parity.md.
	/// </remarks>
	public class TizenFontImageSourceService : TizenImageSourceService, ITizenImageSourceService<IFontImageSource>
	{
		public TizenFontImageSourceService()
			: this(null)
		{
		}

		public TizenFontImageSourceService(ILogger<TizenFontImageSourceService>? logger = null)
			: base(logger)
		{
		}

		public override Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default) =>
			GetImageAsync((IFontImageSource)imageSource, cancellationToken);

		public Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(IFontImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource.IsEmpty)
				return FromResult(null);

			try
			{
				// Font images are not rasterised on Tizen; see the class remarks.
				var image = new MauiImageSource();
				return FromResult(new ImageSourceServiceResult(image, () => image.Dispose()));
			}
			catch (Exception ex)
			{
				Logger?.LogWarning(ex, "Unable to generate font image '{Glyph}'.", imageSource.Glyph);
				throw;
			}
		}
	}
}
