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
	/// under <c>#if TIZEN</c>, and it gives Wave B a seam it can extend without introducing another
	/// public composition method.
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
	public static partial class TizenImageSourceServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen image source services.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Wave A owns the file and stream sources. Wave B contributes font and URI sources through
		/// the private partial hook below, so all four remain behind this single public entry point.
		/// </para>
		/// <para>
		/// <b>Extending this method is the supported path for image sources.</b> The private partial
		/// hook keeps the Wave B implementation in its owned source group while preserving the
		/// single call in <c>ConfigureTizen</c>. That is not a style preference: a second method a
		/// host has to remember would fail silently when omitted.
		/// </para>
		/// <para>
		/// <b>Register a Tizen implementation for every source type added here.</b> MAUI's neutral
		/// services resolve perfectly well and then render nothing, so a registration that maps to
		/// one cannot be caught by asserting that a service exists.
		/// <c>CompositionRootTests.EveryImageSourceTheSeamRegistersUsesATizenImplementation</c>
		/// reads the emitted registration type and fails if any required <c>Tizen*</c>
		/// implementation is absent.
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
			AddWaveBImageSources(services);

			return services;
		}

		static partial void AddWaveBImageSources(IImageSourceServiceCollection services);
	}
}
