// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// URI and font image sources. The core/Wave A slice registers the file and stream services and
// deliberately leaves these two to this workstream, so the pairing here completes the set rather
// than duplicating it.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Loads <see cref="IUriImageSource"/> images by handing the absolute URI to the NUI image loader.
	/// </summary>
	/// <remarks>
	/// Named with a <c>Tizen</c> prefix because <c>Microsoft.Maui.UriImageSourceService</c> already
	/// exists in the neutral assembly.
	/// </remarks>
	public class TizenUriImageSourceService : ITizenImageSourceService, IImageSourceService<IUriImageSource>
	{
		readonly ILogger? _logger;

		/// <summary>Initializes a new instance of the <see cref="TizenUriImageSourceService"/> class.</summary>
		public TizenUriImageSourceService()
			: this(null)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenUriImageSourceService"/> class.</summary>
		/// <param name="logger">An optional logger.</param>
		public TizenUriImageSourceService(ILogger<TizenUriImageSourceService>? logger)
		{
			_logger = logger;
		}

		/// <inheritdoc />
		public async Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource is not IUriImageSource uriImageSource || uriImageSource.IsEmpty)
				return null;

			var uri = uriImageSource.Uri;

			try
			{
				var image = new TizenImageSource();

#if TIZEN
				if (!await image.LoadUrlAsync(uri.AbsoluteUri, cancellationToken))
				{
					image.Dispose();
					_logger?.LogWarning("Unable to load image URI '{Uri}'.", uri);
					return null;
				}
#else
				image.ResourceUrl = uri.AbsoluteUri;
#endif

				return new TizenImageSourceServiceResult(image, image.Dispose);
			}
			catch (Exception ex)
			{
				_logger?.LogWarning(ex, "Unable to load image URI '{Uri}'.", uri);
				throw;
			}
		}
	}

	/// <summary>
	/// Resolves <see cref="IFontImageSource"/> images on Tizen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// UNSUPPORTED: Tizen has no glyph rasterisation path in this backend. Upstream returned an
	/// empty image source, which reported <em>success</em> while rendering nothing — the worst of
	/// both worlds, because the caller is told the image loaded and has no way to discover it did
	/// not.
	/// </para>
	/// <para>
	/// This returns <see langword="null"/> instead, which the loader treats as a failed load: the
	/// previous image is cleared, <c>LoadingCompleted(false)</c> is raised, and a warning is logged.
	/// The caller can then act on the failure. Nothing hangs and nothing reports false success.
	/// </para>
	/// </remarks>
	public class TizenFontImageSourceService : ITizenImageSourceService, IImageSourceService<IFontImageSource>
	{
		readonly ILogger? _logger;

		/// <summary>Initializes a new instance of the <see cref="TizenFontImageSourceService"/> class.</summary>
		public TizenFontImageSourceService()
			: this(null)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenFontImageSourceService"/> class.</summary>
		/// <param name="logger">An optional logger.</param>
		public TizenFontImageSourceService(ILogger<TizenFontImageSourceService>? logger)
		{
			_logger = logger;
		}

		/// <inheritdoc />
		public Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource is not IFontImageSource fontImageSource || fontImageSource.IsEmpty)
				return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);

			// Rasterising a glyph needs a font rasteriser this backend does not have. Returning a
			// blank image would report success for something that renders nothing, so the load is
			// failed explicitly instead: the loader clears the view and raises
			// LoadingCompleted(false), which the caller can actually act on.
			_logger?.LogWarning(
				"Font image sources are not supported on Tizen; '{Glyph}' cannot be rendered.",
				fontImageSource.Glyph);

			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);
		}
	}

}
