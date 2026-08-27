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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

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
		public Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default)
		{
			if (imageSource is not IUriImageSource uriImageSource || uriImageSource.IsEmpty)
				return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);

			var uri = uriImageSource.Uri;

			try
			{
				// NUI resolves remote and file URIs itself, so the URL is handed over as-is.
				var image = new TizenImageSource { ResourceUrl = uri.AbsoluteUri };

				return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(
					new TizenImageSourceServiceResult(image, image.Dispose));
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
	/// UNSUPPORTED, carried over from dotnet/maui: the Tizen backend has never had a glyph
	/// rasterisation path, so upstream returned an empty image source and the glyph rendered blank.
	/// That behaviour is preserved deliberately - the alternative is throwing from inside a property
	/// mapper. See docs/wave-b-mapper-parity.md.
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

			// No rasterisation path exists; see the class remarks. Log it, because a blank image with
			// no diagnostic is indistinguishable from a broken glyph name.
			_logger?.LogWarning(
				"Font image sources are not rasterised on Tizen; '{Glyph}' will render blank.",
				fontImageSource.Glyph);

			var image = new TizenImageSource();

			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(
				new TizenImageSourceServiceResult(image, image.Dispose));
		}
	}

	/// <summary>
	/// Registers the URI and font image source services.
	/// </summary>
	public static class TizenWaveBImageSourceServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the URI and font image source services, completing the set the core slice starts.
		/// </summary>
		/// <param name="services">The image source service collection.</param>
		/// <returns>The collection, for chaining.</returns>
		public static IImageSourceServiceCollection AddTizenUriAndFontImageSources(this IImageSourceServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			// Resolve the loggers from DI. The constructors have always accepted an ILogger, but the
			// previous registration passed none, so the parameter was dead and every diagnostic these
			// services try to emit went nowhere.
			services.AddService<IUriImageSource>(static provider =>
				new TizenUriImageSourceService(provider.GetService<ILogger<TizenUriImageSourceService>>()));

			services.AddService<IFontImageSource>(static provider =>
				new TizenFontImageSourceService(provider.GetService<ILogger<TizenFontImageSourceService>>()));

			return services;
		}
	}
}
