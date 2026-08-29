// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// NUI-free so the registration itself can be composed and asserted on the host lane. The platform
// half only has to supply an ITizenFontDirectoryProvider.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Registers the Tizen font services.
	/// </summary>
	public static class TizenFontServiceCollectionExtensions
	{
		/// <summary>
		/// Replaces MAUI's neutral embedded font loader with the Tizen one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>Replace</c>, not <c>TryAdd</c>. <c>MauiApp.CreateBuilder</c> registers
		/// <see cref="IEmbeddedFontLoader"/> before any of this backend's configuration runs, so a
		/// <c>TryAdd</c> here is a no-op and MAUI's loader - which has no Tizen implementation -
		/// stays in place. The result is not an error: every <c>ConfigureFonts</c> alias just
		/// quietly resolves to the system font.
		/// </para>
		/// <para>
		/// <see cref="ITizenFontDirectoryProvider"/> is expected to have been registered by the
		/// platform half already; it is the only piece that needs TizenFX.
		/// </para>
		/// </remarks>
		/// <param name="services">The service collection.</param>
		/// <returns>The service collection, for chaining.</returns>
		public static IServiceCollection AddTizenFontServices(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.Replace(ServiceDescriptor.Singleton<IEmbeddedFontLoader>(static sp =>
				new TizenEmbeddedFontLoader(
					sp.GetRequiredService<ITizenFontDirectoryProvider>(),
					sp.GetService<ILogger<TizenEmbeddedFontLoader>>())));

			return services;
		}
	}
}
