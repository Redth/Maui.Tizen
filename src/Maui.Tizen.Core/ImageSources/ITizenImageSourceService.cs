// Part of the Maui.Tizen extraction.
//
// Upstream, IImageSourceService declared a Tizen-specific GetImageAsync overload under
// "#if TIZEN". .NET MAUI 11 ships no Tizen target, so the neutral IImageSourceService in
// Microsoft.Maui.Core has no Tizen member and this repository must own that contract.
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Resolves an <see cref="IImageSource"/> to a Tizen <see cref="MauiImageSource"/>.</summary>
	public interface ITizenImageSourceService
	{
		Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(
			IImageSource imageSource,
			CancellationToken cancellationToken = default);
	}

	/// <summary>Strongly typed marker for a Tizen image source service.</summary>
	/// <typeparam name="T">The image source kind handled by this service.</typeparam>
	public interface ITizenImageSourceService<in T> : ITizenImageSourceService, IImageSourceService<T>
		where T : IImageSource
	{
	}

	/// <summary>Base class for the Tizen image source services.</summary>
	public abstract class TizenImageSourceService : ITizenImageSourceService
	{
		protected TizenImageSourceService(ILogger? logger = null)
		{
			Logger = logger;
		}

		public ILogger? Logger { get; }

		public abstract Task<IImageSourceServiceResult<MauiImageSource>?> GetImageAsync(
			IImageSource imageSource,
			CancellationToken cancellationToken = default);

		private protected static Task<IImageSourceServiceResult<MauiImageSource>?> FromResult(
			IImageSourceServiceResult<MauiImageSource>? result) =>
			Task.FromResult(result);
	}

	/// <summary>Provider helpers for resolving Tizen image source services.</summary>
	public static class TizenImageSourceServiceProviderExtensions
	{
		/// <summary>
		/// Resolves the registered service for <paramref name="imageSource"/> and returns it as a
		/// Tizen image source service.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// The registered service does not implement <see cref="ITizenImageSourceService"/>.
		/// </exception>
		public static ITizenImageSourceService GetRequiredTizenImageSourceService(
			this IImageSourceServiceProvider provider,
			IImageSource imageSource)
		{
			var service = provider.GetRequiredImageSourceService(imageSource);

			if (service is ITizenImageSourceService tizenService)
			{
				return tizenService;
			}

			throw new InvalidOperationException(
				$"Unable to find a Tizen image source service for {imageSource.GetType().FullName}. " +
				$"The registered service '{service.GetType().FullName}' does not implement {nameof(ITizenImageSourceService)}.");
		}
	}
}
