using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Hosting
{
	/// <summary>
	/// Registers the Tizen Essentials implementations with a <see cref="MauiAppBuilder"/>.
	/// </summary>
	public static class TizenEssentialsMauiAppBuilderExtensions
	{
		/// <summary>
		/// Registers every Tizen Essentials service implementation into the application's
		/// dependency injection container.
		/// </summary>
		/// <param name="builder">The app builder to configure.</param>
		/// <returns>The same <paramref name="builder"/>, for chaining.</returns>
		/// <remarks>
		/// <para>
		/// Every service is registered as a singleton with <c>TryAdd</c> semantics, so an application
		/// (or another platform backend) can replace any individual implementation simply by
		/// registering its own before calling this method.
		/// </para>
		/// <para>
		/// Registration alone is enough to also drive the static Essentials facades
		/// (<c>Battery.Default</c>, <c>Connectivity.Current</c>, ...). .NET 11 MAUI bridges
		/// DI-registered Essentials services onto their facades during <c>MauiApp</c> initialization
		/// (dotnet/maui#36657), so this method deliberately performs no <c>SetDefault</c> reflection
		/// of its own.
		/// </para>
		/// <para>
		/// <c>MainThread</c> is likewise not configured here: MAUI bridges
		/// <c>MainThread.BeginInvokeOnMainThread</c> from the registered <c>IDispatcher</c> for
		/// non in-box platforms, so the Tizen dispatcher provided by the core Tizen backend is the
		/// single source of truth for main-thread marshalling.
		/// </para>
		/// <para>
		/// Services whose contract Tizen cannot satisfy (<see cref="IAppActions"/>,
		/// <see cref="IClipboard"/>, <see cref="IWebAuthenticator"/>,
		/// <see cref="IAppleSignInAuthenticator"/> and <see cref="IPasskeys"/>) are still registered.
		/// Their implementations throw <see cref="FeatureNotSupportedException"/> with an explicit
		/// reason, which is a materially better developer experience than resolving MAUI's neutral
		/// "not implemented in reference assembly" fallbacks.
		/// </para>
		/// </remarks>
		public static MauiAppBuilder AddTizenEssentials(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			AddTizenEssentials(builder.Services);

			return builder;
		}

		/// <summary>
		/// Registers every Tizen Essentials service implementation into a service collection.
		/// </summary>
		/// <param name="services">The service collection to configure.</param>
		/// <returns>The same <paramref name="services"/>, for chaining.</returns>
		public static IServiceCollection AddTizenEssentials(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			// Application model
			services.TryAddSingleton<IAppActions, TizenAppActions>();
			services.TryAddSingleton<IAppInfo, TizenAppInfo>();
			services.TryAddSingleton<IBrowser, TizenBrowser>();
			services.TryAddSingleton<ILauncher, TizenLauncher>();
			services.TryAddSingleton<IMap, TizenMap>();
			services.TryAddSingleton<IPermissions, TizenPermissions>();

			// Communication
			services.TryAddSingleton<IContacts, TizenContacts>();
			services.TryAddSingleton<IEmail, TizenEmail>();
			services.TryAddSingleton<IPhoneDialer, TizenPhoneDialer>();
			services.TryAddSingleton<ISms, TizenSms>();

			// Data transfer
			services.TryAddSingleton<IClipboard, TizenClipboard>();
			services.TryAddSingleton<IShare, TizenShare>();

			// Storage
			services.TryAddSingleton<IFilePicker, TizenFilePicker>();
			services.TryAddSingleton<IFileSystem, TizenFileSystem>();
			services.TryAddSingleton<IPreferences, TizenPreferences>();
			services.TryAddSingleton<ISecureStorage, TizenSecureStorage>();

			// Device
			services.TryAddSingleton<IBattery, TizenBattery>();
			services.TryAddSingleton<IDeviceDisplay, TizenDeviceDisplay>();
			services.TryAddSingleton<IDeviceInfo, TizenDeviceInfo>();
			services.TryAddSingleton<IFlashlight, TizenFlashlight>();
			services.TryAddSingleton<IHapticFeedback, TizenHapticFeedback>();
			services.TryAddSingleton<IVibration, TizenVibration>();

			// Sensors
			services.TryAddSingleton<IAccelerometer, TizenAccelerometer>();
			services.TryAddSingleton<IBarometer, TizenBarometer>();
			services.TryAddSingleton<ICompass, TizenCompass>();
			services.TryAddSingleton<IGyroscope, TizenGyroscope>();
			services.TryAddSingleton<IMagnetometer, TizenMagnetometer>();
			services.TryAddSingleton<IOrientationSensor, TizenOrientationSensor>();

			// Location
			services.TryAddSingleton<TizenGeocoding>();
			services.TryAddSingleton<IGeocoding>(static sp => sp.GetRequiredService<TizenGeocoding>());
			services.TryAddSingleton<IPlatformGeocoding>(static sp => sp.GetRequiredService<TizenGeocoding>());
			services.TryAddSingleton<IGeolocation, TizenGeolocation>();

			// Networking
			services.TryAddSingleton<IConnectivity, TizenConnectivity>();

			// Media
			services.TryAddSingleton<IMediaPicker, TizenMediaPicker>();
			services.TryAddSingleton<IScreenshot, TizenScreenshot>();
			services.TryAddSingleton<ITextToSpeech, TizenTextToSpeech>();

			// Accessibility
			services.TryAddSingleton<ISemanticScreenReader, TizenSemanticScreenReader>();

			// Authentication
			services.TryAddSingleton<IAppleSignInAuthenticator, TizenAppleSignInAuthenticator>();
			services.TryAddSingleton<IPasskeys, TizenPasskeys>();
			services.TryAddSingleton<IWebAuthenticator, TizenWebAuthenticator>();

			return services;
		}
	}
}
