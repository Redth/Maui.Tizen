// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static partial class TizenImageSourceServiceCollectionExtensions
	{
		static partial void AddWaveBImageSources(IImageSourceServiceCollection services)
		{
			services.AddService<IUriImageSource>(static provider =>
				new TizenUriImageSourceService(provider.GetService<ILogger<TizenUriImageSourceService>>()));

			services.AddService<IFontImageSource>(static provider =>
				new TizenFontImageSourceService(provider.GetService<ILogger<TizenFontImageSourceService>>()));
		}
	}
}
