using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Publishes the Tizen <see cref="IDispatcherProvider"/> onto the static
	/// <see cref="DispatcherProvider.Current"/> when the app starts.
	/// </summary>
	/// <remarks>
	/// <c>Microsoft.Maui.ApplicationModel.MainThread</c> reads the static provider rather than
	/// resolving one from DI, so this is what makes <c>MainThread.BeginInvokeOnMainThread</c> and
	/// friends work on Tizen. Runs from <c>MauiApp.Build()</c> for both <c>useDefaults</c> modes.
	/// </remarks>
	internal sealed class TizenDispatcherProviderInitializer : IMauiInitializeService
	{
		/// <inheritdoc />
		public void Initialize(IServiceProvider services)
		{
			var provider = services.GetService<IDispatcherProvider>();
			if (provider is not null)
				DispatcherProvider.SetCurrent(provider);
		}
	}

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
		/// <remarks>
		/// <para>
		/// Platform services are registered with <c>Replace</c>/<c>RemoveAll</c>, <b>not</b>
		/// <c>TryAdd</c>. <see cref="MauiApp.CreateBuilder(bool)"/> defaults to
		/// <c>useDefaults: true</c>, which runs MAUI's <c>ConfigureDispatching</c> and
		/// <c>ConfigureAnimations</c> before any of this. Those register neutral implementations
		/// (<c>Microsoft.Maui.Dispatching.DispatcherProvider</c>, <c>PlatformTicker</c>), so a
		/// <c>TryAdd</c> here is a silent no-op and the Tizen services never win.
		/// </para>
		/// <para>
		/// That failure mode is quiet and severe: the neutral <c>DispatcherProvider</c> returns no
		/// dispatcher for the NUI main loop, so <c>MainThread</c> marshalling and every animation
		/// stop working with no error at all.
		/// </para>
		/// <para>
		/// <see cref="IApplication"/> is deliberately left on <c>TryAdd</c> - a host that registers
		/// its own application instance before calling this should keep it.
		/// </para>
		/// </remarks>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizen(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddTizenHandlers();

				// The Wave A control handlers (button, entry, editor, check box, switch, slider,
				// progress bar, activity indicator, picker, date/time picker, search bar, stepper,
				// radio button). Without this they are never registered in a real app, however
				// thoroughly they are unit tested - the tests called AddTizenControlHandlers
				// themselves and so never exercised the composition root.
				handlers.AddTizenControlHandlers();
			});

			// Services the control handlers resolve: the font manager they use to turn a MAUI Font
			// into a NUI family name, and the modal host the pickers open through. Registered with
			// TryAdd, so a host that supplies its own keeps it.
			builder.Services.AddTizenControlServices();

			// The composition root for image sources. Without this call the Tizen services are
			// never registered - and the failure is silent rather than loud, because MAUI's neutral
			// package already registers FileImageSourceService, StreamImageSourceService,
			// FontImageSourceService and UriImageSourceService by default. Every source type
			// therefore still resolves; it just resolves to an implementation that produces no
			// image on Tizen, so images are simply blank with nothing thrown and nothing logged.
			//
			// This is the single hook: the image workstream should extend AddTizenImageSources with
			// the font and URI services rather than adding a second entry point a host has to
			// remember to call.
			builder.ConfigureImageSources(sources => sources.AddTizenImageSources());

			var services = builder.Services;

			// Singleton: one provider for the process, matching MAUI's own ConfigureDispatching.
			services.Replace(ServiceDescriptor.Singleton<IDispatcherProvider, TizenDispatcherProvider>());

			// Scoped: there may be a different dispatcher per window.
			//
			// Publishing the provider through DispatcherProvider.SetCurrent is load-bearing, not
			// incidental. Microsoft.Maui.ApplicationModel.MainThread resolves through the *static*
			// DispatcherProvider.Current, not through DI, so replacing only the DI registration
			// would leave MainThread talking to the neutral provider - which has no dispatcher for
			// the NUI main loop and fails silently. MAUI's own registration performs this side
			// effect internally; doing it here makes both useDefaults paths behave identically.
			services.Replace(ServiceDescriptor.Scoped<IDispatcher>(static sp =>
			{
				var provider = sp.GetRequiredService<IDispatcherProvider>();
				DispatcherProvider.SetCurrent(provider);

				return provider.GetForCurrentThread()
					?? throw new InvalidOperationException(
						"No SynchronizationContext is installed on this thread, so no IDispatcher "
						+ "could be created. On Tizen this means the call happened before the NUI "
						+ "main loop was started - resolve IDispatcher from inside the application "
						+ "lifecycle instead.");
			}));

			// MAUI's ApplicationDispatcher (internal, registered by ConfigureDispatching) is left
			// alone deliberately: it resolves IDispatcherProvider from DI lazily, so it picks up
			// the replacement above rather than capturing the neutral provider.

			// Scoped, matching dotnet/maui's ConfigureAnimations. Neither may be transient or
			// singleton: TizenTicker is IDisposable and owns a Timer, so a transient resolved from
			// the root provider is retained with its timer for the whole process; and TizenTicker
			// captures SynchronizationContext.Current in its constructor, so a singleton would pin
			// every animation callback to whichever thread happened to resolve it first.
			services.Replace(ServiceDescriptor.Scoped<ITicker>(static _ => new TizenTicker()));
			services.Replace(ServiceDescriptor.Scoped<IAnimationManager>(
				static sp => new AnimationManager(sp.GetRequiredService<ITicker>())));

			// Publish the provider onto the STATIC DispatcherProvider.Current during app startup.
			//
			// MainThread resolves through the static provider, not through DI, so replacing only
			// the DI registration is not enough. With useDefaults:true MAUI's own
			// ApplicationDispatcherInitializer happens to do this for us as a side effect of
			// resolving IDispatcher at Build time - but with useDefaults:false nothing does, and
			// MainThread is left pointing at the neutral provider with no error. Verified: without
			// this initializer, DispatcherProvider.Current after Build() is
			// Microsoft.Maui.Dispatching.DispatcherProvider on the useDefaults:false path.
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<IMauiInitializeService, TizenDispatcherProviderInitializer>());

			builder.ConfigureTizenWindowLifecycle();

			return builder;
		}

		/// <summary>
		/// Bridges the Tizen application lifecycle onto the cross-platform <see cref="IWindow"/>
		/// lifecycle events.
		/// </summary>
		/// <remarks>
		/// Without this, <c>Created</c>, <c>Activated</c>, <c>Deactivated</c>, <c>Stopped</c>,
		/// <c>Resumed</c> and <c>Destroying</c> never fire on Tizen, because <c>CoreApplication</c>
		/// only surfaces <c>OnCreate</c>/<c>OnResume</c>/<c>OnPause</c>/<c>OnTerminate</c>. The
		/// The bridge is registered in DI and driven from <c>TizenMauiApplication</c>'s own
		/// lifecycle overrides. It is deliberately not wired through the Tizen lifecycle-event
		/// delegates, because those take <c>Tizen.Applications.CoreApplication</c> and therefore
		/// cannot be referenced from the platform-independent hosting code that this method lives
		/// in - which is also what lets the bridge be unit tested on the host.
		/// </remarks>
		/// <param name="builder">The app builder.</param>
		/// <returns>The app builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizenWindowLifecycle(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			// Singleton so TizenMauiApplication and any host observer share one instance, which
			// matters because the bridge tracks Activated/Deactivated balance across callbacks.
			builder.Services.TryAddSingleton<TizenWindowLifecycleBridge>();

			return builder;
		}
	}
}
