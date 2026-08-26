using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Microsoft.Maui.Storage;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Contracts covering <c>AddTizenEssentials</c>.
/// </summary>
/// <remarks>
/// These assertions inspect <see cref="ServiceDescriptor"/> metadata instead of resolving through
/// the container wherever possible, so they run on any host without a Tizen device present.
/// </remarks>
public class TizenEssentialsRegistrationTests
{
	/// <summary>
	/// Every Essentials contract this backend implements, mapped to its implementation type.
	/// </summary>
	public static readonly IReadOnlyDictionary<Type, Type> ExpectedRegistrations = new Dictionary<Type, Type>
	{
		[typeof(IAppActions)] = typeof(TizenAppActions),
		[typeof(IAppInfo)] = typeof(TizenAppInfo),
		[typeof(IBrowser)] = typeof(TizenBrowser),
		[typeof(ILauncher)] = typeof(TizenLauncher),
		[typeof(IMap)] = typeof(TizenMap),
		[typeof(IPermissions)] = typeof(TizenPermissions),

		[typeof(IContacts)] = typeof(TizenContacts),
		[typeof(IEmail)] = typeof(TizenEmail),
		[typeof(IPhoneDialer)] = typeof(TizenPhoneDialer),
		[typeof(ISms)] = typeof(TizenSms),

		[typeof(IClipboard)] = typeof(TizenClipboard),
		[typeof(IShare)] = typeof(TizenShare),

		[typeof(IFilePicker)] = typeof(TizenFilePicker),
		[typeof(IFileSystem)] = typeof(TizenFileSystem),
		[typeof(IPreferences)] = typeof(TizenPreferences),
		[typeof(ISecureStorage)] = typeof(TizenSecureStorage),

		[typeof(IBattery)] = typeof(TizenBattery),
		[typeof(IDeviceDisplay)] = typeof(TizenDeviceDisplay),
		[typeof(IDeviceInfo)] = typeof(TizenDeviceInfo),
		[typeof(IFlashlight)] = typeof(TizenFlashlight),
		[typeof(IHapticFeedback)] = typeof(TizenHapticFeedback),
		[typeof(IVibration)] = typeof(TizenVibration),

		[typeof(IAccelerometer)] = typeof(TizenAccelerometer),
		[typeof(IBarometer)] = typeof(TizenBarometer),
		[typeof(ICompass)] = typeof(TizenCompass),
		[typeof(IGyroscope)] = typeof(TizenGyroscope),
		[typeof(IMagnetometer)] = typeof(TizenMagnetometer),
		[typeof(IOrientationSensor)] = typeof(TizenOrientationSensor),

		[typeof(IGeolocation)] = typeof(TizenGeolocation),

		[typeof(IConnectivity)] = typeof(TizenConnectivity),

		[typeof(IMediaPicker)] = typeof(TizenMediaPicker),
		[typeof(IScreenshot)] = typeof(TizenScreenshot),
		[typeof(ITextToSpeech)] = typeof(TizenTextToSpeech),

		[typeof(ISemanticScreenReader)] = typeof(TizenSemanticScreenReader),

		[typeof(IAppleSignInAuthenticator)] = typeof(TizenAppleSignInAuthenticator),
		[typeof(IPasskeys)] = typeof(TizenPasskeys),
		[typeof(IWebAuthenticator)] = typeof(TizenWebAuthenticator),
	};

	public static TheoryData<Type> ExpectedServiceTypes()
	{
		var data = new TheoryData<Type>();
		foreach (var serviceType in ExpectedRegistrations.Keys)
			data.Add(serviceType);
		return data;
	}

	static IServiceCollection Registered()
	{
		var services = new ServiceCollection();
		services.AddTizenEssentials();
		return services;
	}

	[Theory]
	[MemberData(nameof(ExpectedServiceTypes))]
	public void RegistersEachEssentialsContractExactlyOnce(Type serviceType)
	{
		var descriptor = Assert.Single(Registered(), d => d.ServiceType == serviceType);
		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
		Assert.Equal(ExpectedRegistrations[serviceType], descriptor.ImplementationType);
	}

	[Fact]
	public void RegistersGeocodingAsASingleSharedInstance()
	{
		var services = Registered();

		var concrete = Assert.Single(services, d => d.ServiceType == typeof(TizenGeocoding));
		Assert.Equal(ServiceLifetime.Singleton, concrete.Lifetime);
		Assert.Equal(typeof(TizenGeocoding), concrete.ImplementationType);

		// IGeocoding and IPlatformGeocoding must resolve to the *same* instance, otherwise the
		// map service token forwarded by the .NET 11 Essentials DI bridge to IPlatformGeocoding
		// would not be visible to code resolving IGeocoding.
		foreach (var serviceType in new[] { typeof(IGeocoding), typeof(IPlatformGeocoding) })
		{
			var descriptor = Assert.Single(services, d => d.ServiceType == serviceType);
			Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
			Assert.NotNull(descriptor.ImplementationFactory);
		}

		using var provider = services.BuildServiceProvider();

		var geocoding = provider.GetRequiredService<IGeocoding>();
		Assert.Same(geocoding, provider.GetRequiredService<IPlatformGeocoding>());
		Assert.Same(geocoding, provider.GetRequiredService<TizenGeocoding>());
	}

	[Fact]
	public void EveryServiceResolvesToItsRegisteredSingleton()
	{
		using var provider = Registered().BuildServiceProvider();

		foreach (var (serviceType, implementationType) in ExpectedRegistrations)
		{
			var instance = provider.GetService(serviceType);

			Assert.NotNull(instance);
			Assert.IsType(implementationType, instance);
			Assert.Same(instance, provider.GetService(serviceType));
		}
	}

	[Fact]
	public void IsIdempotent()
	{
		var services = new ServiceCollection();
		services.AddTizenEssentials();
		var afterFirst = services.Count;

		services.AddTizenEssentials();

		Assert.Equal(afterFirst, services.Count);
	}

	[Theory]
	[MemberData(nameof(ExpectedServiceTypes))]
	public void UsesTryAddSoApplicationsCanOverrideAnyService(Type serviceType)
	{
		var services = new ServiceCollection();
		var replacement = new object();

		services.AddSingleton(serviceType, _ => replacement);
		services.AddTizenEssentials();

		var descriptor = Assert.Single(services, d => d.ServiceType == serviceType);
		Assert.NotNull(descriptor.ImplementationFactory);
		Assert.Null(descriptor.ImplementationType);
	}

	[Fact]
	public void RejectsANullServiceCollection() =>
		Assert.Throws<ArgumentNullException>(
			static () => TizenEssentialsMauiAppBuilderExtensions.AddTizenEssentials((IServiceCollection)null!));

	[Fact]
	public void RejectsANullAppBuilder() =>
		Assert.Throws<ArgumentNullException>(
			static () => TizenEssentialsMauiAppBuilderExtensions.AddTizenEssentials((MauiAppBuilder)null!));

	[Fact]
	public void RegistersNothingBeyondTheDocumentedSurface()
	{
		var allowed = ExpectedRegistrations.Keys
			.Append(typeof(TizenGeocoding))
			.Append(typeof(IGeocoding))
			.Append(typeof(IPlatformGeocoding))
			.ToHashSet();

		var unexpected = Registered()
			.Select(d => d.ServiceType)
			.Where(t => !allowed.Contains(t))
			.Distinct()
			.ToList();

		Assert.Empty(unexpected);
	}
}
