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
		/// <see cref="ITizenModalHost"/> is scoped because it resolves the window's
		/// <see cref="NavigationStack"/> from the window scope it was created in.
		/// </para>
		/// </remarks>
		public static IServiceCollection AddTizenNuiControlsPlatform(
			this IServiceCollection services,
			TizenAlertRegistrationMode mode = TizenAlertRegistrationMode.FullManager)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddSingleton<ITizenAlertDialogFactory, NuiAlertDialogFactory>();
			services.TryAddScoped<ITizenModalHost>(static provider => new NuiModalHost(
				provider,
				provider.GetService<ILogger<NuiModalHost>>()));
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
		/// Associates a native window and its modal navigation stack with the window scope
		/// described by <paramref name="mauiContext"/>.
		/// </summary>
		/// <param name="mauiContext">The window's context.</param>
		/// <param name="window">The native window.</param>
		/// <remarks>
		/// This is the single call the Tizen window handler needs to make for window-affine alert
		/// routing to work. The navigation stack is resolved from the same scope, so it does not
		/// need to be passed here.
		/// </remarks>
		public static void AttachTizenWindow(IMauiContext mauiContext, TWindow window)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(window);

			TizenWindowContext.AttachTo(mauiContext, window);
		}
	}
}
