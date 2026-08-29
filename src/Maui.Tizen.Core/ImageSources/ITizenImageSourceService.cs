// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The Tizen-typed image source service contract.
	/// </summary>
	/// <remarks>
	/// The neutral <c>Microsoft.Maui.IImageSourceService</c> is a marker interface: the typed
	/// <c>GetImageAsync</c> only exists in each platform's own build of MAUI. Since this backend
	/// consumes the neutral assembly it has to declare the Tizen-typed contract itself.
	/// </remarks>
	public interface ITizenImageSourceService : IImageSourceService
	{
		/// <summary>
		/// Resolves <paramref name="imageSource"/> into a NUI resource URL.
		/// </summary>
		Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(IImageSource imageSource, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Locates <see cref="ITizenImageSourceService"/> implementations.
	/// </summary>
	public static class TizenImageSourceServiceProviderExtensions
	{
		/// <summary>
		/// Resolves the registered service for <paramref name="imageSource"/> and loads it.
		/// </summary>
		/// <returns>
		/// <see langword="null"/> when no Tizen-aware service is registered for the source type,
		/// which lets callers degrade to "no image" rather than throwing during a property map.
		/// </returns>
		public static Task<IImageSourceServiceResult<TizenImageSource>?> GetTizenImageAsync(
			this IImageSourceServiceProvider provider,
			IImageSource imageSource,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(provider);
			ArgumentNullException.ThrowIfNull(imageSource);

			if (provider.GetImageSourceService(imageSource.GetType()) is ITizenImageSourceService service)
				return service.GetImageAsync(imageSource, cancellationToken);

			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null);
		}
	}
}
