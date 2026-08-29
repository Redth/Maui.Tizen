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
		/// Every platform service replaces MAUI's neutral registration. This is required when the
		/// method is reached from <c>UseMauiAppTizenControls</c>, because <c>UseMauiApp</c> has
		/// already registered the neutral Essentials defaults. An application can replace an
		/// individual implementation after configuring the Tizen backend.
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

			// Installing the Essentials initializer is what actually makes the registrations below
			// reach the static facades. MauiApp.CreateBuilder(useDefaults: true) installs it via the
			// internal UseEssentials(); with useDefaults: false nothing does, so registering the
			// services alone would leave Battery.Default and friends resolving lazy platform
			// defaults while DI held the real Tizen implementations - two live instances, and the
			// static API silently wrong.
			//
			// ConfigureEssentials is the public entry point onto the same initializer and is
			// idempotent (it TryAdds), so calling it here is safe alongside useDefaults: true.
			builder.ConfigureEssentials();

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
			ReplaceSingleton<IAppActions, TizenAppActions>(services);
			ReplaceSingleton<IAppInfo, TizenAppInfo>(services);
			ReplaceSingleton<IBrowser, TizenBrowser>(services);
			ReplaceSingleton<ILauncher, TizenLauncher>(services);
			ReplaceSingleton<IMap, TizenMap>(services);
			ReplaceSingleton<IPermissions, TizenPermissions>(services);

			// Communication
			ReplaceSingleton<IContacts, TizenContacts>(services);
			ReplaceSingleton<IEmail, TizenEmail>(services);
			ReplaceSingleton<IPhoneDialer, TizenPhoneDialer>(services);
			ReplaceSingleton<ISms, TizenSms>(services);

			// Data transfer
			ReplaceSingleton<IClipboard, TizenClipboard>(services);
			ReplaceSingleton<IShare, TizenShare>(services);

			// Storage
			ReplaceSingleton<IFilePicker, TizenFilePicker>(services);
			ReplaceSingleton<IFileSystem, TizenFileSystem>(services);
			ReplaceSingleton<IPreferences, TizenPreferences>(services);
			ReplaceSingleton<ISecureStorage, TizenSecureStorage>(services);

			// Device
			ReplaceSingleton<IBattery, TizenBattery>(services);
			ReplaceSingleton<IDeviceDisplay, TizenDeviceDisplay>(services);
			ReplaceSingleton<IDeviceInfo, TizenDeviceInfo>(services);
			ReplaceSingleton<IFlashlight, TizenFlashlight>(services);
			ReplaceSingleton<IHapticFeedback, TizenHapticFeedback>(services);
			ReplaceSingleton<IVibration, TizenVibration>(services);

			// Sensors
			ReplaceSingleton<IAccelerometer, TizenAccelerometer>(services);
			ReplaceSingleton<IBarometer, TizenBarometer>(services);
			ReplaceSingleton<ICompass, TizenCompass>(services);
			ReplaceSingleton<IGyroscope, TizenGyroscope>(services);
			ReplaceSingleton<IMagnetometer, TizenMagnetometer>(services);
			ReplaceSingleton<IOrientationSensor, TizenOrientationSensor>(services);

			// Location
			ReplaceSingleton<TizenGeocoding, TizenGeocoding>(services);
			services.Replace(ServiceDescriptor.Singleton<IGeocoding>(
				static sp => sp.GetRequiredService<TizenGeocoding>()));
			services.Replace(ServiceDescriptor.Singleton<IPlatformGeocoding>(
				static sp => sp.GetRequiredService<TizenGeocoding>()));
			ReplaceSingleton<IGeolocation, TizenGeolocation>(services);

			// Networking
			ReplaceSingleton<IConnectivity, TizenConnectivity>(services);

			// Media
			ReplaceSingleton<IMediaPicker, TizenMediaPicker>(services);
			ReplaceSingleton<IScreenshot, TizenScreenshot>(services);
			ReplaceSingleton<ITextToSpeech, TizenTextToSpeech>(services);

			// Accessibility
			ReplaceSingleton<ISemanticScreenReader, TizenSemanticScreenReader>(services);

			// Authentication
			ReplaceSingleton<IAppleSignInAuthenticator, TizenAppleSignInAuthenticator>(services);
			ReplaceSingleton<IPasskeys, TizenPasskeys>(services);
			ReplaceSingleton<IWebAuthenticator, TizenWebAuthenticator>(services);

			return services;
		}

		static void ReplaceSingleton<TService, TImplementation>(IServiceCollection services)
			where TService : class
			where TImplementation : class, TService =>
			services.Replace(ServiceDescriptor.Singleton<TService, TImplementation>());
	}
}
