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
	public static class TizenMauiAppBuilderExtensions
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
			builder.Services.TryAddSingleton<IDispatcherProvider, TizenDispatcherProvider>();
			builder.Services.TryAddScoped<IDispatcher>(static services =>
				services.GetRequiredService<IDispatcherProvider>().GetForCurrentThread()
				?? throw new InvalidOperationException(
					"No SynchronizationContext is installed on this thread, so no IDispatcher could be "
					+ "created. On Tizen this means the call happened before the NUI main loop was "
					+ "started - resolve IDispatcher from inside the application lifecycle instead."));
			// Scoped, matching dotnet/maui's ConfigureAnimations. Two reasons this must not be
			// transient/singleton: TizenTicker is IDisposable and owns a Timer, so a transient
			// resolved from the root provider is retained with its timer for the whole process;
			// and TizenTicker captures SynchronizationContext.Current in its constructor, so a
			// singleton would pin every animation callback to whichever thread happened to
			// resolve it first.
			builder.Services.TryAddScoped<ITicker>(static _ => new TizenTicker());
			builder.Services.TryAddScoped<IAnimationManager>(
				static services => new AnimationManager(services.GetRequiredService<ITicker>()));

			return builder;
		}
	}
}
