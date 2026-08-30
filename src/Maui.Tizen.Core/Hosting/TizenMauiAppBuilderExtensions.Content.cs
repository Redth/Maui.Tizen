// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The platform half of the composition root. Compiled only into the lanes that have real TizenFX,
// because everything it registers derives from Tizen.NUI types.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	public static partial class TizenMauiAppBuilderExtensions
	{
		/// <summary>
		/// Registers the container, image, graphics and swipe handlers, and the platform half of
		/// the embedded-font service.
		/// </summary>
		/// <remarks>
		/// Image sources are deliberately not registered here. Wave B extends the finalized shared
		/// <c>AddTizenImageSources</c> seam, so <c>ConfigureTizen</c> retains one authoritative call
		/// site for file, stream, URI and font services.
		/// </remarks>
		static partial void ConfigurePlatformContent(MauiAppBuilder builder)
		{
			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenContentHandlers());

			// Replace, not TryAdd - see TizenFontServiceCollectionExtensions. Only the directory
			// provider needs TizenFX, so the registration itself lives in a NUI-free file where a
			// host test can prove the replacement actually beats MAUI's default.
			builder.Services.TryAddSingleton<ITizenFontDirectoryProvider, TizenPlatformFontDirectoryProvider>();
			builder.Services.AddTizenFontServices();
		}
	}
}
