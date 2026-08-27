using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Host-builder entry points for the Tizen backend.
	/// </summary>
	public static partial class TizenMauiAppBuilderExtensions
	{
		/// <summary>
		/// Configures the app class and wires up every Tizen service this backend provides.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Named <c>UseMauiAppTizen</c> rather than <c>UseMauiApp</c> so it never collides with
		/// MAUI Controls' own <c>Microsoft.Maui.Hosting.AppHostBuilderExtensions.UseMauiApp&lt;TApp&gt;</c>
		/// when both namespaces are in scope.
		/// </para>
		/// <para>
		/// This registers the dispatcher provider, which is what makes
		/// <c>Microsoft.Maui.ApplicationModel.MainThread</c> work on Tizen through the .NET 11
		/// dispatcher bridge. There is deliberately no port of MAUI's <c>MainThread.tizen.cs</c>.
		/// </para>
		/// </remarks>
		/// <typeparam name="TApp">The application type.</typeparam>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder UseMauiAppTizen<TApp>(this MauiAppBuilder builder)
			where TApp : class, IApplication
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.TryAddSingleton<IApplication, TApp>();

			return builder.ConfigureTizen();
		}

		/// <summary>
		/// Configures the app class from a factory and wires up every Tizen service this backend
		/// provides.
		/// </summary>
		/// <typeparam name="TApp">The application type.</typeparam>
		/// <param name="builder">The app builder.</param>
		/// <param name="implementationFactory">Factory that creates the application.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder UseMauiAppTizen<TApp>(
			this MauiAppBuilder builder,
			Func<IServiceProvider, TApp> implementationFactory)
			where TApp : class, IApplication
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(implementationFactory);

			builder.Services.TryAddSingleton<IApplication>(implementationFactory);

			return builder.ConfigureTizen();
		}

		/// <summary>
		/// Registers the Tizen handlers, dispatcher and animation ticker without taking an opinion
		/// on the application type.
		/// </summary>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizen(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenHandlers());

			// Implemented in TizenMauiAppBuilderExtensions.Content.cs, which is compiled only into
			// the lanes that have real TizenFX. The container, image, graphics and swipe handlers
			// derive from NUI types, so they cannot be referenced from this file: it is also
			// compiled into the host-side lane, where NUI is replaced by stubs.
			//
			// This is a hook rather than a separate public entry point on purpose. Wave A's image
			// composition comment makes the same argument: a second method a host has to remember
			// to call fails silently when it is forgotten, because MAUI's neutral handlers and image
			// source services still resolve - they just do nothing on Tizen.
			ConfigurePlatformContent(builder);

			builder.Services.TryAddSingleton<IDispatcherProvider, TizenDispatcherProvider>();
			builder.Services.TryAddScoped<IDispatcher>(static services =>
				services.GetRequiredService<IDispatcherProvider>().GetForCurrentThread()
				?? throw new InvalidOperationException(
					"No SynchronizationContext is installed on this thread, so no IDispatcher could be "
					+ "created. On Tizen this means the call happened before the NUI main loop was "
					+ "started - resolve IDispatcher from inside the application lifecycle instead."));
			builder.Services.TryAddTransient<ITicker, TizenTicker>();
			builder.Services.TryAddSingleton<IAnimationManager, AnimationManager>();

			return builder;
		}

		/// <summary>
		/// Registers the platform content handlers and image sources.
		/// </summary>
		/// <remarks>
		/// Declared here and implemented in the platform-only half. When that half is not compiled
		/// - the host-side verification lane - the call disappears, which is exactly what should
		/// happen for handlers whose platform views cannot be loaded there.
		/// </remarks>
		static partial void ConfigurePlatformContent(MauiAppBuilder builder);
	}
}
