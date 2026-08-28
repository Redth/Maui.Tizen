using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Selects how the Tizen backend plugs into .NET MAUI's alert infrastructure.
	/// </summary>
	public enum TizenAlertRegistrationMode
	{
		/// <summary>
		/// Register <see cref="TizenAlertManager"/> as the window's <see cref="IAlertManager"/>.
		/// </summary>
		/// <remarks>
		/// This is the default because native NUI popups must be dismissed explicitly. .NET MAUI's
		/// built-in manager treats unsubscribe as "drop the reference", which on Tizen would leave
		/// an orphaned modal popup on screen and leave the awaiting caller pending forever.
		/// </remarks>
		FullManager,

		/// <summary>
		/// Register only <see cref="TizenAlertManagerSubscription"/> and let .NET MAUI's built-in
		/// manager own subscription lifecycle.
		/// </summary>
		/// <remarks>
		/// Use this when application code needs .NET MAUI's default manager semantics, including
		/// its delegate-based dialog conventions. Dialogs that are open when the window tears down
		/// are not dismissed in this mode.
		/// </remarks>
		SubscriptionOnly,
	}

	/// <summary>
	/// Registers the Tizen alert, modal and gesture infrastructure.
	/// </summary>
	public static class TizenControlsServiceCollectionExtensions
	{
		/// <summary>
		/// Registers the Tizen alert and modal coordination services.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <param name="mode">How the backend plugs into .NET MAUI's alert infrastructure.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The alert services are registered as <b>scoped</b>. .NET MAUI creates a service scope
		/// per window and resolves <see cref="IAlertManager"/> from that scope, so a scoped
		/// lifetime is what gives each window its own window-affine manager. A singleton would
		/// route every window's dialogs through shared state.
		/// </para>
		/// <para>
		/// <see cref="ITizenAlertDialogFactory"/> and <see cref="ITizenModalHost"/> are not
		/// registered here because they are the NUI presentation layer. Register them before
		/// calling this method, or call the NUI hosting extension which does it for you. Every
		/// registration uses try-add semantics, so an application can override any single piece.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenAlerts(
			this IServiceCollection services,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddScoped<ITizenWindowContext, TizenWindowContext>();
			services.TryAddSingleton<ITizenPlatformWindowProvider, TizenPlatformWindowProvider>();

			if (mode == TizenAlertRegistrationMode.FullManager)
			{
				services.TryAddScoped<IAlertManager, TizenAlertManager>();
			}
			else
			{
				services.TryAddScoped<IAlertManagerSubscription>(
					static provider => CreateSubscriptionOnly(provider));
			}

			return services;
		}

		internal static IAlertManagerSubscription CreateSubscriptionOnly(IServiceProvider provider)
		{
			if (provider is not IKeyedServiceProvider keyedProvider)
			{
				return CreateAlertSubscription(provider);
			}

			var alertHandler = keyedProvider.GetKeyedService(
				typeof(Func<Page, AlertArguments, Task<bool>>),
				TizenDelegateAlertManagerSubscription.DisplayAlertServiceKey)
				as Func<Page, AlertArguments, Task<bool>>;
			var actionSheetHandler = keyedProvider.GetKeyedService(
				typeof(Func<Page, ActionSheetArguments, Task<string?>>),
				TizenDelegateAlertManagerSubscription.DisplayActionSheetServiceKey)
				as Func<Page, ActionSheetArguments, Task<string?>>;
			var promptHandler = keyedProvider.GetKeyedService(
				typeof(Func<Page, PromptArguments, Task<string?>>),
				TizenDelegateAlertManagerSubscription.DisplayPromptServiceKey)
				as Func<Page, PromptArguments, Task<string?>>;

			if (alertHandler is null && actionSheetHandler is null && promptHandler is null)
			{
				return CreateAlertSubscription(provider);
			}

			return new TizenDelegateAlertManagerSubscription(
				alertHandler,
				actionSheetHandler,
				promptHandler,
				() => CreateAlertSubscription(provider));
		}

		static TizenAlertManagerSubscription CreateAlertSubscription(IServiceProvider provider)
		{
			var windowContext = provider.GetRequiredService<ITizenWindowContext>();

			// Late-bound for the same reason as the full manager: the window may not be attached
			// yet when this scope is first resolved.
			return new TizenAlertManagerSubscription(
				() => windowContext.PlatformWindow,
				provider.GetRequiredService<ITizenAlertDialogFactory>(),
				provider.GetRequiredService<ITizenModalHost>(),
				provider.GetRequiredService<ITizenPlatformWindowProvider>());
		}

		/// <summary>
		/// Registers the Tizen gesture infrastructure.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The gesture services are registered as <b>singletons</b>. Unlike alerts, gesture
		/// handling has no window affinity: .NET MAUI resolves
		/// <see cref="IGesturePlatformManagerFactory"/> once per handler connection and the factory
		/// creates a fresh manager for each, so no per-window state is held.
		/// </para>
		/// <para>
		/// <see cref="ITizenNativeGestureDetectorFactory"/> is not registered here because it is
		/// the NUI detection layer. A default <see cref="ITizenPixelScaler"/> using a scaling
		/// factor of one is registered so that host-side tests and non-scaled displays work
		/// without extra configuration; the NUI hosting extension replaces it with the real
		/// display metrics.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenGestures(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			// Identity scaling. Correct only on a 1x display, and registered with TryAdd so the
			// platform layer's real scaler - registered earlier by AddTizenNuiControlsPlatform -
			// always wins. It exists so host-side tests and 1x displays work with no extra
			// configuration; it is NOT a sensible default for a device.
			services.TryAddSingleton<ITizenPixelScaler>(static _ => new TizenPixelScaler());
			services.TryAddSingleton<ITizenGestureDispatcher, TizenGestureDispatcher>();
			services.TryAddSingleton<ITizenGestureHandlerFactory, TizenGestureHandlerFactory>();
			services.TryAddSingleton<IGesturePlatformManagerFactory, TizenGesturePlatformManagerFactory>();

			return services;
		}

		/// <summary>
		/// Registers the pixel scaler used to convert native gesture coordinates into
		/// device-independent units.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <param name="scalingFactorProvider">
		/// Returns the display's scaling factor - device pixels per device-independent unit.
		/// Invoked once, lazily, when the scaler is first resolved.
		/// </param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The platform layer calls this with the real display metrics. It is a separate method so
		/// that the wiring is executable on the host: the only part that genuinely needs a device
		/// is reading the scaling factor, which is parameterized here rather than baked in.
		/// </para>
		/// <para>
		/// A non-positive or non-finite factor falls back to 1 rather than throwing. This runs
		/// during window creation, and a mis-scaled UI is a far better failure than an app that
		/// will not start.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenPixelScaler(
			this IServiceCollection services,
			Func<double> scalingFactorProvider)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(scalingFactorProvider);

			services.TryAddSingleton<ITizenPixelScaler>(_ =>
			{
				var factor = scalingFactorProvider();

				return new TizenPixelScaler(
					factor > 0 && !double.IsNaN(factor) && !double.IsInfinity(factor) ? factor : 1d);
			});

			return services;
		}

		/// <summary>
		/// Registers the Tizen modal page navigation services.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The factory is a singleton: dotnet/maui#37853 calls it once per window and the returned
		/// platform holds the per-window state, so the factory itself needs none.
		/// </para>
		/// <para>
		/// <see cref="ITizenNavigationStack"/> and <see cref="ITizenWindowBackButton"/> are scoped,
		/// because both wrap objects the window owns. They are registered as holders that the Tizen
		/// window handler fills in once the native window exists, so dependents can be resolved
		/// from the window scope before the window is realized.
		/// </para>
		/// <para>
		/// Note that <see cref="IModalNavigationPlatformFactory"/> is currently the provisional
		/// contract in this repository rather than the one from dotnet/maui#37853, which has not
		/// shipped. .NET MAUI will not resolve it until that PR lands and the registration is
		/// pointed at the real interface.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenModalNavigation(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddSingleton<ITizenModalPageRealizer, TizenModalPageRealizer>();
			services.TryAddScoped<ITizenNavigationStack, TizenScopedNavigationStack>();
			services.TryAddScoped<ITizenWindowBackButton, TizenScopedWindowBackButton>();
			services.TryAddSingleton<IModalNavigationPlatformFactory, TizenModalNavigationPlatformFactory>();

			return services;
		}

		/// <summary>
		/// Registers the Tizen alert, modal and gesture infrastructure.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <param name="mode">How the backend plugs into .NET MAUI's alert infrastructure.</param>
		/// <returns>The same service collection, for chaining.</returns>
		public static IServiceCollection AddTizenControlsPlatform(
			this IServiceCollection services,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager) =>
			services
				.AddTizenAlerts(mode)
				.AddTizenModalNavigation()
				.AddTizenGestures();
	}

	/// <summary>
	/// <see cref="MauiAppBuilder"/> conveniences for the Tizen backend.
	/// </summary>
	public static class TizenControlsMauiAppBuilderExtensions
	{
		/// <summary>
		/// Registers the Tizen alert, modal and gesture infrastructure.
		/// </summary>
		/// <param name="builder">The app builder to configure.</param>
		/// <param name="mode">How the backend plugs into .NET MAUI's alert infrastructure.</param>
		/// <returns>The same builder, for chaining.</returns>
		public static MauiAppBuilder UseTizenControlsPlatform(
			this MauiAppBuilder builder,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.AddTizenControlsPlatform(mode);

			return builder;
		}
	}
}
