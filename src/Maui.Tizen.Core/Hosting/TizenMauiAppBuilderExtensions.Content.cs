// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The platform half of the composition root. Compiled only into the lanes that have real TizenFX,
// because everything it registers derives from Tizen.NUI types.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	public static partial class TizenMauiAppBuilderExtensions
	{
		/// <summary>
		/// Registers the container, image, graphics and swipe handlers, and the Tizen image source
		/// services.
		/// </summary>
		/// <remarks>
		/// Image sources are registered here rather than by the host, because forgetting them fails
		/// silently: MAUI's neutral package already registers file, stream, URI and font services,
		/// so every source type still resolves - just to an implementation that produces no image on
		/// Tizen. The symptom is a blank image with nothing thrown and nothing logged.
		/// </remarks>
		static partial void ConfigurePlatformContent(MauiAppBuilder builder)
		{
			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenContentHandlers());

			builder.ConfigureImageSources(sources =>
			{
				sources.AddTizenImageSources();
				sources.AddTizenUriAndFontImageSources();
			});

			// Replace, not TryAdd - see TizenFontServiceCollectionExtensions. Only the directory
			// provider needs TizenFX, so the registration itself lives in a NUI-free file where a
			// host test can prove the replacement actually beats MAUI's default.
			builder.Services.TryAddSingleton<ITizenFontDirectoryProvider, TizenPlatformFontDirectoryProvider>();
			builder.Services.AddTizenFontServices();

			builder.Services.TryAddSingleton<ITizenFontManager, TizenFontManager>();
			builder.Services.Replace(ServiceDescriptor.Singleton<IFontManager>(
				static sp => sp.GetRequiredService<ITizenFontManager>()));
		}
	}
}
