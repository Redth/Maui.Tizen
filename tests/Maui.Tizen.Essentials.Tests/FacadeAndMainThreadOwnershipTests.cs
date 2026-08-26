using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Guards the two integration invariants this backend depends on:
/// the .NET 11 Essentials DI to static facade bridge owns facade assignment, and the MAUI
/// dispatcher bridge owns main-thread marshalling.
/// </summary>
public class FacadeAndMainThreadOwnershipTests
{
	static readonly Assembly Backend = typeof(TizenAppInfo).Assembly;

	/// <summary>
	/// The Essentials facades that dotnet/maui#36657 bridges from DI. Every contract this backend
	/// registers must be in this set, otherwise registering it would never reach the static facade.
	/// </summary>
	static readonly HashSet<string> BridgedContracts = new(StringComparer.Ordinal)
	{
		"IAccelerometer", "IAppActions", "IAppInfo", "IAppleSignInAuthenticator", "IBarometer",
		"IBattery", "IBrowser", "IClipboard", "ICompass", "IConnectivity", "IContacts",
		"IDeviceDisplay", "IDeviceInfo", "IEmail", "IFilePicker", "IFileSystem", "IFlashlight",
		"IGeocoding", "IGeolocation", "IGyroscope", "IHapticFeedback", "ILauncher", "IMagnetometer",
		"IMap", "IMediaPicker", "IOrientationSensor", "IPasskeys", "IPermissions", "IPhoneDialer",
		"IPreferences", "IScreenshot", "ISecureStorage", "ISemanticScreenReader", "IShare", "ISms",
		"ITextToSpeech", "IVersionTracking", "IVibration", "IWebAuthenticator",
	};

	[Fact]
	public void EveryRegisteredContractIsBridgedToItsStaticFacadeByMaui()
	{
		var notBridged = TizenEssentialsRegistrationTests.ExpectedRegistrations.Keys
			.Select(t => t.Name)
			.Where(name => !BridgedContracts.Contains(name))
			.ToList();

		Assert.Empty(notBridged);
	}

	[Fact]
	public void DoesNotCallSetDefaultOrSetCurrentOnAnyEssentialsFacade()
	{
		var forbidden = new[] { "SetDefault", "SetCurrent" };

		var offenders = ReferencedMemberNames()
			.Where(name => forbidden.Contains(name, StringComparer.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void DoesNotUseReflectionToReachEssentialsFacadeBackingFields()
	{
		var reflectionEntryPoints = new[]
		{
			"GetField", "GetFields", "GetRuntimeField", "GetRuntimeFields", "InvokeMember",
		};

		var offenders = ReferencedMemberNames()
			.Where(name => reflectionEntryPoints.Contains(name, StringComparer.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void DoesNotShipItsOwnMainThreadImplementation()
	{
		// MainThread marshalling is bridged from the registered IDispatcher by MAUI for
		// non in-box platforms, so this backend must not declare a MainThread type of its own...
		var mainThreadTypes = Backend.GetTypes()
			.Where(t => t.Name.Contains("MainThread", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(mainThreadTypes);
	}

	[Fact]
	public void DoesNotTouchTheEcoreMainLoopDirectly()
	{
		// ...nor reach around the dispatcher into the EFL main loop, which is what the
		// in-box dotnet/maui MainThread.tizen.cs did.
		var offenders = ReferencedTypeNames()
			.Where(name => name.Contains("EcoreMainloop", StringComparison.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void MarshalsThroughTheMauiMainThreadFacade()
	{
		// The bridge replaces MainThread's platform delegate, so calling into MainThread is the
		// correct way for this backend to reach the UI thread.
		Assert.Contains("MainThread", ReferencedTypeNames());
	}

	[Fact]
	public void ExposesGeocodingThroughTheTokenAwarePlatformContract() =>
		Assert.True(typeof(IPlatformGeocoding).IsAssignableFrom(typeof(TizenGeocoding)));

	static IEnumerable<string> ReferencedMemberNames()
	{
		using var stream = System.IO.File.OpenRead(Backend.Location);
		using var peReader = new PEReader(stream);
		var reader = peReader.GetMetadataReader();

		foreach (var handle in reader.MemberReferences)
			yield return reader.GetString(reader.GetMemberReference(handle).Name);
	}

	static IEnumerable<string> ReferencedTypeNames()
	{
		using var stream = System.IO.File.OpenRead(Backend.Location);
		using var peReader = new PEReader(stream);
		var reader = peReader.GetMetadataReader();

		foreach (var handle in reader.TypeReferences)
			yield return reader.GetString(reader.GetTypeReference(handle).Name);
	}
}
