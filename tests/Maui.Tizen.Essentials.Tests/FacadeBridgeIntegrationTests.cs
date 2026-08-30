using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Microsoft.Maui.Storage;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// End-to-end checks that building a <see cref="MauiApp"/> with this backend actually hands the
/// Tizen implementations to the static Essentials facades.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that would have caught the <c>useDefaults: false</c> defect.
/// <c>MauiApp.CreateBuilder(useDefaults: true)</c> installs MAUI's Essentials initializer through
/// the <b>internal</b> <c>UseEssentials()</c>; nothing installs it when defaults are skipped. Simply
/// registering services into DI was therefore not enough - <c>Battery.Default</c> and friends kept
/// resolving lazy platform defaults while DI held the real Tizen implementations, leaving two live
/// instances and a silently wrong static API.
/// </para>
/// <para>
/// They mutate process-global facade state, so they are collected into a non-parallel collection.
/// </para>
/// </remarks>
[Collection(nameof(EssentialsStaticStateCollection))]
public class FacadeBridgeIntegrationTests
{
	/// <summary>
	/// Facades whose identity can be asserted without touching a Tizen device.
	/// </summary>
	/// <remarks>
	/// Reading a facade only resolves the registered implementation; it does not call into it. The
	/// services excluded here are the ones whose <c>Default</c>/<c>Current</c> getters would
	/// construct a lazy platform default that P/Invokes on a non-Tizen host.
	/// </remarks>
	public static TheoryData<Type, Func<object>> BridgedFacades() =>
		new()
		{
			{ typeof(IBattery), static () => Battery.Default },
			{ typeof(IConnectivity), static () => Connectivity.Current },
			{ typeof(IDeviceDisplay), static () => DeviceDisplay.Current },
			{ typeof(IDeviceInfo), static () => DeviceInfo.Current },
			{ typeof(IPreferences), static () => Preferences.Default },
			{ typeof(ISecureStorage), static () => SecureStorage.Default },
			{ typeof(IFileSystem), static () => FileSystem.Current },
			{ typeof(IGeolocation), static () => Geolocation.Default },
			{ typeof(IAccelerometer), static () => Accelerometer.Default },
			{ typeof(ICompass), static () => Compass.Default },
			{ typeof(IPermissions), static () => Permissions.Current },
		};

	static MauiApp BuildApp(bool useDefaults)
	{
		var builder = MauiApp.CreateBuilder(useDefaults);
		builder.AddTizenEssentials();
		return builder.Build();
	}

	[Theory]
	[MemberData(nameof(BridgedFacades))]
	public void FacadesResolveToTheRegisteredTizenServiceWithoutDefaults(Type serviceType, Func<object> readFacade)
	{
		using var app = BuildApp(useDefaults: false);

		var fromContainer = app.Services.GetService(serviceType);
		Assert.NotNull(fromContainer);

		// Identity, not merely type: a second instance of the right type would still mean the
		// static API and DI were talking to different objects.
		Assert.Same(fromContainer, readFacade());
	}

	[Theory]
	[MemberData(nameof(BridgedFacades))]
	public void FacadesResolveToTheRegisteredTizenServiceWithDefaults(Type serviceType, Func<object> readFacade)
	{
		using var app = BuildApp(useDefaults: true);

		var fromContainer = app.Services.GetService(serviceType);
		Assert.NotNull(fromContainer);
		Assert.Same(fromContainer, readFacade());
	}

	[Fact]
	public void RegisteringEssentialsInstallsMauisInitializerWhenDefaultsAreSkipped()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.AddTizenEssentials();

		// The initializer is what performs the bridging. Without it the registrations above are
		// inert as far as the static API is concerned.
		var initializers = builder.Services
			.Where(d => d.ServiceType == typeof(IMauiInitializeService))
			.ToList();

		Assert.Contains(
			initializers,
			d => d.ImplementationType?.Name.Contains("EssentialsInitializer", StringComparison.Ordinal) == true);
	}

	[Fact]
	public void InstallingTheInitializerTwiceDoesNotDuplicateIt()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: true);

		var before = builder.Services
			.Count(d => d.ImplementationType?.Name.Contains("EssentialsInitializer", StringComparison.Ordinal) == true);

		builder.AddTizenEssentials();
		builder.AddTizenEssentials();

		var after = builder.Services
			.Count(d => d.ImplementationType?.Name.Contains("EssentialsInitializer", StringComparison.Ordinal) == true);

		// MAUI's AddEssentialsInitializer uses TryAddEnumerable, so useDefaults: true plus two
		// explicit registrations must still yield exactly one initializer.
		Assert.Equal(before == 0 ? 1 : before, after);
	}

	[Theory]
	[InlineData(typeof(IBattery), typeof(TizenBattery))]
	[InlineData(typeof(ILauncher), typeof(TizenLauncher))]
	[InlineData(typeof(IShare), typeof(TizenShare))]
	[InlineData(typeof(IPreferences), typeof(TizenPreferences))]
	[InlineData(typeof(ISecureStorage), typeof(TizenSecureStorage))]
	public void TizenRegistrationReplacesMauiDefaultsWithoutShadowDescriptors(
		Type serviceType,
		Type expectedImplementation)
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<BridgeApplication>();

		// This extra descriptor models a registration-order conflict in addition to MAUI's
		// defaults. The backend must remove all predecessors and leave one authoritative service.
		builder.Services.AddSingleton(serviceType, static _ => new object());
		builder.AddTizenEssentials();

		var descriptor = Assert.Single(builder.Services, d => d.ServiceType == serviceType);
		Assert.Equal(expectedImplementation, descriptor.ImplementationType);
		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
	}

	[Fact]
	public void ReleasesTheFacadesWhenTheAppIsDisposed()
	{
		object bridged;

		using (var app = BuildApp(useDefaults: false))
		{
			bridged = app.Services.GetRequiredService<IBattery>();
			Assert.Same(bridged, Battery.Default);
		}

		// After disposal the facade must not keep handing out a service whose owning provider is
		// gone. MAUI restores the predecessor; the only thing this backend must not do is leave the
		// disposed instance installed.
		Assert.NotSame(bridged, Battery.Default);
	}

	[Fact]
	public void ConfiguringAMapServiceTokenDoesNotFailStartup()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.AddTizenEssentials();
		builder.ConfigureEssentials(essentials => essentials.UseMapServiceToken("map-service-token"));

		// Tizen.Maps is gone at API15, so the token cannot be used for anything - but a
		// ConfigureEssentials line must never be the thing that crashes an application at startup.
		using var app = builder.Build();

		Assert.NotNull(app.Services.GetRequiredService<IGeocoding>());
	}

	[Fact]
	public void PlatformGeocodingCarriesNoTokenContractInTheNeutralMauiPackage()
	{
		// Documents a real gap rather than assuming the token is forwarded.
		//
		// MAUI declares IPlatformGeocoding.MapServiceToken inside `#if WINDOWS || TIZEN`, so in the
		// neutral (non platform specific) Microsoft.Maui.Essentials assembly this package builds
		// against, the interface has NO members - and the bridge code that would forward a
		// configured token is compiled out with it.
		//
		// That is harmless here only because geocoding is Unsupported on API15 anyway. If Tizen
		// ever regains a geocoding service, the token will have to be plumbed explicitly; it will
		// not arrive through this interface.
		Assert.Empty(typeof(IPlatformGeocoding).GetMembers(
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

		// The concrete implementation still exposes the property, so an application can set it.
		Assert.NotNull(typeof(TizenGeocoding).GetProperty(nameof(TizenGeocoding.MapServiceToken)));
	}

	sealed class BridgeApplication : Application
	{
	}
}

/// <summary>
/// Serializes tests that mutate the process-global Essentials facade state.
/// </summary>
[CollectionDefinition(nameof(EssentialsStaticStateCollection), DisableParallelization = true)]
public class EssentialsStaticStateCollection
{
}
