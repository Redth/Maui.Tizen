// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Registers the Tizen image source services on an <see cref="IImageSourceServiceCollection"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This lives in the portable compile group, separate from the service implementations it
	/// registers, for two reasons. It keeps the call in
	/// <c>TizenMauiAppBuilderExtensions.ConfigureTizen</c> compiling on both lanes rather than only
	/// under <c>#if TIZEN</c>, and it gives the later image workstream a seam it can extend without
	/// touching NUI-dependent files.
	/// </para>
	/// <para>
	/// <b>Registration order matters and is not obvious.</b> MAUI's neutral package already
	/// registers <c>FileImageSourceService</c>, <c>StreamImageSourceService</c>,
	/// <c>FontImageSourceService</c> and <c>UriImageSourceService</c> by default, so every image
	/// source type <em>resolves</em> whether or not this method is ever called. That is precisely
	/// why the missing composition-root call was silent: nothing threw, no service was reported
	/// missing, and images simply never appeared. A later <c>AddService</c> for the same source type
	/// replaces the earlier registration - verified by
	/// <c>CompositionRootTests.ATizenRegistrationReplacesMauisNeutralDefault</c> - which is
	/// what lets these win.
	/// </para>
	/// </remarks>
	public static class TizenImageSourceServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen image source services.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Wave A owns the file and stream sources. Font and URI sources belong to the image
		/// workstream and are deliberately absent rather than stubbed: a stub would turn a clear
		/// "no Tizen service registered" failure into a silently blank image, which is the harder
		/// bug to find.
		/// </para>
		/// <para>
		/// <b>Extending this is the supported path for the image workstream.</b> Add the font and
		/// URI registrations here so they are picked up by the single call in <c>ConfigureTizen</c>,
		/// rather than introducing a second entry point that a host has to remember to call - that
		/// is the mistake this method's own missing call site demonstrated.
		/// </para>
		/// </remarks>
		/// <param name="services">The image source service collection.</param>
		/// <returns>The collection, for chaining.</returns>
		public static IImageSourceServiceCollection AddTizenImageSources(this IImageSourceServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

#if TIZEN
			services.AddService<IFileImageSource>(static _ => new TizenFileImageSourceService());
			services.AddService<IStreamImageSource>(static _ => new TizenStreamImageSourceService());
#endif

			return services;
		}
	}
}
