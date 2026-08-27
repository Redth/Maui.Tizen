using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using global::Tizen.UIExtensions.NUI;
using TWindow = global::Tizen.NUI.Window;

namespace Microsoft.Maui.Platforms.Tizen.Nui
{
	/// <summary>
	/// Registers the NUI-backed implementations of the Tizen presentation contracts.
	/// </summary>
	public static class TizenNuiHostingExtensions
	{
		/// <summary>
		/// Registers the NUI alert, modal and gesture implementations along with the
		/// toolkit-independent Tizen alert and gesture infrastructure.
		/// </summary>
		/// <param name="services">The service collection to add to.</param>
		/// <param name="mode">How the backend plugs into .NET MAUI's alert infrastructure.</param>
		/// <returns>The same service collection, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// The NUI implementations are registered first so that the try-add registrations in
		/// <see cref="TizenControlsServiceCollectionExtensions.AddTizenControlsPlatform"/> bind to
		/// them rather than to a placeholder.
		/// </para>
		/// <para>
		/// <see cref="ITizenModalHost"/> is scoped because it drives the window's navigation stack,
		/// which is itself registered scoped and filled in by <see cref="AttachTizenWindow"/>.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenNuiControlsPlatform(
			this IServiceCollection services,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddSingleton<ITizenAlertDialogFactory, NuiAlertDialogFactory>();
			services.TryAddEnumerable(
				ServiceDescriptor.Scoped<IMauiInitializeScopedService, NuiWindowScopeInitializer>());

			// Must be registered before AddTizenGestures, whose TryAdd would otherwise bind the
			// identity scaler. Identity is only correct on a 1x display; Tizen wearables and TVs
			// are not, and every pan, swipe, pinch, tap and pointer coordinate would be wrong by
			// the display factor.
			//
			// The factor is read lazily, because DeviceInfo is not usable until the Tizen
			// application has initialised.
			services.AddTizenPixelScaler(static () => global::Tizen.UIExtensions.Common.DeviceInfo.ScalingFactor);
			services.TryAddScoped<ITizenModalHost>(static provider => new TizenModalHost(
				provider.GetRequiredService<ITizenNavigationStack>(),
				provider.GetService<ILogger<TizenModalHost>>()));
			services.TryAddSingleton<ITizenPlatformWindowProvider, NuiPlatformWindowProvider>();
			services.TryAddSingleton<ITizenNativeGestureDetectorFactory, NuiGestureDetectorFactory>();

			return services.AddTizenControlsPlatform(mode);
		}

		/// <summary>
		/// Registers the NUI-backed Tizen alert, modal and gesture infrastructure.
		/// </summary>
		/// <param name="builder">The app builder to configure.</param>
		/// <param name="mode">How the backend plugs into .NET MAUI's alert infrastructure.</param>
		/// <returns>The same builder, for chaining.</returns>
		public static MauiAppBuilder UseTizenNuiControlsPlatform(
			this MauiAppBuilder builder,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.AddTizenNuiControlsPlatform(mode);

			return builder;
		}

		/// <summary>
		/// Associates a native window and its navigation stack with the window scope described by
		/// <paramref name="mauiContext"/>.
		/// </summary>
		/// <param name="mauiContext">The window's context.</param>
		/// <param name="window">The native window.</param>
		/// <param name="navigationStack">The window's navigation stack.</param>
		/// <param name="backButton">
		/// Routes the hardware back button to the current page. Optional.
		/// </param>
		/// <remarks>
		/// <para>
		/// The Core layer publishes the native window and navigation stack into the window context,
		/// and this package's scoped initializer calls this method so window-affine alert routing,
		/// dialog modal coordination and modal page navigation all resolve the right window. The
		/// window scope must already contain the scoped registrations added by
		/// <see cref="AddTizenNuiControlsPlatform"/>; when it does not, this is a no-op rather than
		/// a throw, so a partially configured host degrades instead of failing at window creation.
		/// </para>
		/// <para>
		/// The scoped initializer supplies an adapter over Core's existing per-window back route.
		/// Modal handling is registered ahead of the cross-platform window fallback and removing the
		/// modal registration restores that fallback; Controls never replaces Core's callback or
		/// creates a competing registry.
		/// </para>
		/// </remarks>
		public static void AttachTizenWindow(
			IMauiContext mauiContext,
			TWindow window,
			NavigationStack navigationStack,
			ITizenWindowBackButton? backButton = null)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(window);
			ArgumentNullException.ThrowIfNull(navigationStack);

			var services = mauiContext.Services;

			TizenWindowContext.AttachTo(mauiContext, window);

			(services?.GetService<ITizenNavigationStack>() as TizenScopedNavigationStack)
				?.Attach(new NuiNavigationStack(navigationStack));

			if (backButton is not null)
			{
				(services?.GetService<ITizenWindowBackButton>() as TizenScopedWindowBackButton)
					?.Attach(backButton);
			}
		}

		internal sealed class NuiWindowScopeInitializer : IMauiInitializeScopedService
		{
			public void Initialize(IServiceProvider services)
			{
				ArgumentNullException.ThrowIfNull(services);

				var mauiContext = services.GetService<IMauiContext>();
				var window = services.GetService<TWindow>();
				var navigationStack = services.GetService<NavigationStack>();

				if (mauiContext is null || window is null || navigationStack is null)
				{
					return;
				}

				TizenNuiHostingExtensions.AttachTizenWindow(
					mauiContext,
					window,
					navigationStack,
					new NuiWindowBackButton(window));
			}
		}

		internal sealed class NuiWindowBackButton : ITizenWindowBackButton
		{
			readonly TWindow _window;

			public NuiWindowBackButton(TWindow window) =>
				_window = window ?? throw new ArgumentNullException(nameof(window));

			public IDisposable RegisterBackButtonPressedHandler(Func<bool> handler) =>
				_window.RegisterBackButtonPressedHandler(handler);
		}
	}
}
