using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;
using TizenConnectionType = Tizen.Network.Connection.ConnectionType;
using TizenDeviceOrientation = Tizen.Applications.DeviceOrientation;
using TizenPixelFormat = Tizen.NUI.PixelFormat;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Behavioural tests for the pure translation logic ported from the dotnet/maui Tizen backend.
/// These run without a Tizen device because they never call into native libraries.
/// </summary>
public class TizenTranslationTests
{
	[Theory]
	[InlineData("1.2.3", 1, 2, 3)]
	[InlineData("1.2", 1, 2, -1)]
	[InlineData("1.0.0-rc1", 1, 0, 0)]
	[InlineData("7", 7, 0, -1)]
	[InlineData("", 0, 0, -1)]
	[InlineData(null, 0, 0, -1)]
	[InlineData("not-a-version", 0, 0, -1)]
	public void ParsesTizenPackageVersions(string? input, int major, int minor, int build)
	{
		var version = TizenPlatform.ParseVersion(input);

		Assert.Equal(major, version.Major);
		Assert.Equal(minor, version.Minor);
		Assert.Equal(build, version.Build);
	}

	[Theory]
	[InlineData("mobile", TizenDeviceProfile.Mobile)]
	[InlineData("MOBILE", TizenDeviceProfile.Mobile)]
	[InlineData("wearable", TizenDeviceProfile.Wearable)]
	[InlineData("tv", TizenDeviceProfile.TV)]
	[InlineData("common", TizenDeviceProfile.Common)]
	[InlineData("iot-headed", TizenDeviceProfile.Common)]
	[InlineData("zzz", TizenDeviceProfile.Unknown)]
	[InlineData("", TizenDeviceProfile.Unknown)]
	[InlineData(null, TizenDeviceProfile.Unknown)]
	public void ClassifiesTizenDeviceProfiles(string? profile, TizenDeviceProfile expected) =>
		Assert.Equal(expected, TizenSystemInformation.ParseProfile(profile));

	[Theory]
	[InlineData("armv7", true, false, DeviceType.Physical)]
	[InlineData("x86", false, true, DeviceType.Virtual)]
	[InlineData("armv7", true, true, DeviceType.Unknown)]
	[InlineData("aarch64", false, false, DeviceType.Unknown)]
	[InlineData(null, false, false, DeviceType.Unknown)]
	public void ClassifiesDeviceType(string? arch, bool armv7, bool x86, DeviceType expected) =>
		Assert.Equal(expected, TizenDeviceInfo.ClassifyDeviceType(arch, armv7, x86));

	[Theory]
	[InlineData(SensorSpeed.Fastest, 0u)]
	[InlineData(SensorSpeed.Game, 20u)]
	[InlineData(SensorSpeed.UI, 60u)]
	[InlineData(SensorSpeed.Default, 200u)]
	public void MapsSensorSpeedToTizenIntervals(SensorSpeed speed, uint expected) =>
		Assert.Equal(expected, speed.ToPlatform());

	[Theory]
	[InlineData(HapticFeedbackType.LongPress, "Hold")]
	[InlineData(HapticFeedbackType.Click, "Tap")]
	public void MapsHapticFeedbackTypes(HapticFeedbackType type, string expected) =>
		Assert.Equal(expected, TizenHapticFeedback.ConvertType(type));

	[Theory]
	[InlineData(null, "key", "maui.tizen.preferences:v2:d:key")]
	[InlineData("", "key", "maui.tizen.preferences:v2:d:key")]
	[InlineData("shared", "key", "maui.tizen.preferences:v2:n:shared~key")]
	public void PrefixesSharedPreferenceKeys(string? sharedName, string key, string expected) =>
		Assert.Equal(expected, TizenPreferences.GetFullKey(key, sharedName));

	[Theory]
	[InlineData(TizenConnectionType.WiFi, ConnectionProfile.WiFi)]
	[InlineData(TizenConnectionType.Cellular, ConnectionProfile.Cellular)]
	[InlineData(TizenConnectionType.Ethernet, ConnectionProfile.Ethernet)]
	[InlineData(TizenConnectionType.Bluetooth, ConnectionProfile.Bluetooth)]
	public void MapsConnectionProfiles(TizenConnectionType type, ConnectionProfile expected) =>
		Assert.Equal(expected, TizenConnectivity.MapProfileType(type));

	[Theory]
	[InlineData(TizenConnectionType.Disconnected)]
	public void MapsUnknownConnectionTypesToNoProfile(TizenConnectionType type) =>
		Assert.Null(TizenConnectivity.MapProfileType(type));

	[Fact]
	public void MapsNetProxyToInternetAccess() =>
		Assert.Equal(
			NetworkAccess.Internet,
			TizenConnectivity.GetNetworkAccess(static () => TizenConnectionType.NetProxy));

	[Theory]
	[InlineData("custom://resource")]
	[InlineData("unknown:value")]
	public void UsesViewOperationForCustomUriSchemes(string uri) =>
		Assert.Equal(TizenAppControlOperations.View, TizenLauncher.GetOperation(new Uri(uri)));

	[Theory]
	[InlineData(TizenDeviceOrientation.Orientation_0, DisplayRotation.Rotation0, DisplayOrientation.Portrait)]
	[InlineData(TizenDeviceOrientation.Orientation_90, DisplayRotation.Rotation90, DisplayOrientation.Landscape)]
	[InlineData(TizenDeviceOrientation.Orientation_180, DisplayRotation.Rotation180, DisplayOrientation.Portrait)]
	[InlineData(TizenDeviceOrientation.Orientation_270, DisplayRotation.Rotation270, DisplayOrientation.Landscape)]
	public void MapsDisplayOrientationFromAPortraitNaturalOrientation(
		TizenDeviceOrientation deviceOrientation,
		DisplayRotation expectedRotation,
		DisplayOrientation expectedOrientation)
	{
		var (rotation, orientation) = TizenDeviceDisplay.MapOrientation(deviceOrientation, DisplayOrientation.Portrait);

		Assert.Equal(expectedRotation, rotation);
		Assert.Equal(expectedOrientation, orientation);
	}

	[Fact]
	public void MapsDisplayOrientationFromALandscapeNaturalOrientation()
	{
		var (rotation, orientation) =
			TizenDeviceDisplay.MapOrientation(TizenDeviceOrientation.Orientation_90, DisplayOrientation.Landscape);

		Assert.Equal(DisplayRotation.Rotation90, rotation);
		Assert.Equal(DisplayOrientation.Portrait, orientation);
	}

	[Fact]
	public void UsesTheAndroidStyleBaseLogicalDpiForDensity() =>
		Assert.Equal(160.0f, TizenDeviceDisplay.BaseLogicalDpi);

	[Fact]
	public void ConvertsGpsSpeedFromKilometresPerHourToMetresPerSecond() =>
		Assert.Equal(10.0, TizenGeolocation.KilometersPerHourToMetersPerSecond(36), 1);

	[Fact]
	public void EscapesPlacemarkAddressesForGeoQueries()
	{
		var placemark = new Placemark
		{
			Thoroughfare = "1 Main St",
			Locality = "Redmond",
			AdminArea = "WA",
			PostalCode = "98052",
			CountryName = "USA",
		};

		var escaped = placemark.GetEscapedAddress();

		Assert.DoesNotContain(' ', escaped);
		Assert.Equal(
			Uri.EscapeDataString("1 Main St Redmond WA 98052 USA"),
			escaped);
	}

	[Theory]
	[InlineData(TizenPixelFormat.RGBA8888, 4)]
	[InlineData(TizenPixelFormat.BGRA8888, 4)]
	[InlineData(TizenPixelFormat.RGB888, 3)]
	[InlineData(TizenPixelFormat.RGB565, 2)]
	public void MapsScreenshotPixelStrides(TizenPixelFormat format, int expected) =>
		Assert.Equal(expected, TizenScreenshotResult.BytesPerPixel(format));

	[Fact]
	public void RejectsScreenshotPixelFormatsWithoutAMatchingColorSpace()
	{
		Assert.Throws<FeatureNotSupportedException>(
			static () => { _ = TizenScreenshotResult.MapColorSpace(TizenPixelFormat.A8); });
		Assert.Throws<FeatureNotSupportedException>(
			static () => { _ = TizenScreenshotResult.BytesPerPixel(TizenPixelFormat.A8); });
	}

	[Fact]
	public void ScreenshotImplementsTheNeutralViewCaptureContract()
	{
		Assert.True(typeof(IScreenshot).IsAssignableFrom(typeof(TizenScreenshot)));
		Assert.True(typeof(IViewScreenshot).IsAssignableFrom(typeof(TizenScreenshot)));
	}

	[Fact]
	public async Task ScreenshotReturnsNullForForeignPlatformViews() =>
		Assert.Null(await new TizenScreenshot().CaptureViewAsync(new object()));

	[Fact]
	public void TizenTypedScreenshotHelpersRejectForeignImplementations() =>
		Assert.Throws<PlatformNotSupportedException>(
			static () => { _ = new FakeScreenshot().CaptureAsync((global::Tizen.NUI.Window)null!); });

	sealed class FakeScreenshot : IScreenshot
	{
		public bool IsCaptureSupported => false;

		public System.Threading.Tasks.Task<IScreenshotResult> CaptureAsync() =>
			throw new NotSupportedException();
	}
}
