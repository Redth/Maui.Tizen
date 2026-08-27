// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Host-builder entry point for apps that use MAUI Controls on Tizen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists because the dependency runs one way: <c>Maui.Tizen.Controls</c> references
	/// <c>Maui.Tizen.Core</c>, so Core's <c>ConfigureTizen</c> cannot reach back and register the
	/// Controls-level shape handlers. Something on this side has to close the loop, and this is it —
	/// the same layering MAUI itself uses, where <c>UseMauiApp</c> lives in Controls and wraps the
	/// core configuration.
	/// </para>
	/// <para>
	/// Registering the shape handlers without calling this would be worse than useless: MAUI
	/// Controls already maps <c>BoxView</c>, <c>Line</c>, <c>Path</c>, <c>Polygon</c>,
	/// <c>Polyline</c>, <c>Rectangle</c> and <c>RoundRectangle</c> to its own neutral handlers, so
	/// nothing fails to resolve — the shapes simply never reach a Tizen handler and never draw.
	/// </para>
	/// </remarks>
	public static class TizenControlsMauiAppBuilderExtensions
	{
		/// <summary>
		/// Configures the Tizen backend for a MAUI Controls app: everything
		/// <see cref="TizenMauiAppBuilderExtensions.ConfigureTizen"/> registers, plus the
		/// Controls-level shape handlers.
		/// </summary>
		/// <remarks>
		/// Controls apps should call this rather than <c>ConfigureTizen</c>. Calling
		/// <c>ConfigureTizen</c> directly is still valid for a Core-only host; it simply leaves the
		/// concrete shapes on MAUI's neutral handlers.
		/// </remarks>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizenControls(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.ConfigureTizen();
			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenShapeHandlers());

			return builder;
		}

		/// <summary>
		/// Configures the app class and the whole Tizen Controls backend in one call.
		/// </summary>
		/// <remarks>
		/// The Controls-app counterpart to
		/// <c>TizenMauiAppBuilderExtensions.UseMauiAppTizen&lt;TApp&gt;</c>. Named distinctly rather
		/// than overloaded so that having both namespaces in scope can never make the call ambiguous
		/// — and so a reader can tell which layer they are configuring.
		/// </remarks>
		/// <typeparam name="TApp">The application type.</typeparam>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder UseMauiAppTizenControls<TApp>(this MauiAppBuilder builder)
			where TApp : class, IApplication
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.UseMauiAppTizen<TApp>();
			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenShapeHandlers());

			return builder;
		}
	}
}
