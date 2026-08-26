using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;
using TizenBatteryPowerSource = Tizen.System.BatteryPowerSource;
using TizenColorSpace = Tizen.Multimedia.ColorSpace;
using TizenConnectionState = Tizen.Network.Connection.ConnectionState;
using TizenPixelFormat = Tizen.NUI.PixelFormat;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Behavioural tests for the review fixes that can be exercised without a Tizen device.
/// </summary>
public class TizenServiceBehaviorTests
{
	// -------------------------------------------------------------------------------------------
	// Battery power source and state mapping.
	// -------------------------------------------------------------------------------------------

	[Theory]
	[InlineData(TizenBatteryPowerSource.Ac, BatteryPowerSource.AC)]
	[InlineData(TizenBatteryPowerSource.Usb, BatteryPowerSource.Usb)]
	[InlineData(TizenBatteryPowerSource.Wireless, BatteryPowerSource.Wireless)]
	[InlineData(TizenBatteryPowerSource.None, BatteryPowerSource.Battery)]
	public void MapsEveryNativeBatteryPowerSource(TizenBatteryPowerSource source, BatteryPowerSource expected) =>
		Assert.Equal(expected, TizenBattery.MapPowerSource(source));

	[Fact]
	public void ReportsWirelessChargingRatherThanAssumingUsb()
	{
		// The previous implementation inferred the source from the charging flag alone, so every
		// charging device claimed USB - including AC and wireless.
		Assert.Equal(BatteryPowerSource.Wireless, TizenBattery.MapPowerSource(TizenBatteryPowerSource.Wireless));
		Assert.Equal(BatteryPowerSource.AC, TizenBattery.MapPowerSource(TizenBatteryPowerSource.Ac));
	}

	[Theory]
	// Charging on any source.
	[InlineData(true, 50, TizenBatteryPowerSource.Ac, BatteryState.Charging)]
	[InlineData(true, 50, TizenBatteryPowerSource.Usb, BatteryState.Charging)]
	// Full: on a charger at 100%.
	[InlineData(true, 100, TizenBatteryPowerSource.Ac, BatteryState.Full)]
	[InlineData(false, 100, TizenBatteryPowerSource.Ac, BatteryState.Full)]
	// Plugged in but not charging is its own state, not "discharging".
	[InlineData(false, 80, TizenBatteryPowerSource.Usb, BatteryState.NotCharging)]
	// Actually running on battery.
	[InlineData(false, 80, TizenBatteryPowerSource.None, BatteryState.Discharging)]
	[InlineData(false, 100, TizenBatteryPowerSource.None, BatteryState.Discharging)]
	public void MapsBatteryStateFromChargingLevelAndSource(
		bool isCharging,
		int percent,
		TizenBatteryPowerSource source,
		BatteryState expected) =>
		Assert.Equal(expected, TizenBattery.MapState(isCharging, percent, source));

	// -------------------------------------------------------------------------------------------
	// Storage key encoding.
	// -------------------------------------------------------------------------------------------

	[Fact]
	public void SharedPreferenceKeysCannotCollide()
	{
		// The bug this encoding exists to prevent: with naive concatenation both of these produced
		// "a~b~c", so two different logical entries shared one physical entry.
		var first = TizenStorageKeyEncoding.GetFullKey("b~c", "a");
		var second = TizenStorageKeyEncoding.GetFullKey("c", "a~b");

		Assert.NotEqual(first, second);
	}

	[Theory]
	[InlineData("plain", "plain")]
	[InlineData("has~separator", "has\\~separator")]
	[InlineData("has\\escape", "has\\\\escape")]
	public void EscapesSeparatorAndEscapeCharacters(string value, string expected) =>
		Assert.Equal(expected, TizenStorageKeyEncoding.Encode(value));

	[Fact]
	public void EncodingIsInjectiveAcrossAwkwardInputs()
	{
		string[] components = ["a", "b", "a~b", "a\\b", "~", "\\", "", "a~", "~a"];

		var encoded = (from shared in components
					   from key in components
					   select TizenStorageKeyEncoding.GetFullKey(key, shared)).ToList();

		// A shared name of "" means the default store, where the key is used verbatim, so those
		// collapse legitimately. Every other pair must stay distinct.
		var qualified = (from shared in components.Where(c => c.Length > 0)
						 from key in components
						 select TizenStorageKeyEncoding.GetFullKey(key, shared)).ToList();

		Assert.Equal(qualified.Count, qualified.Distinct(StringComparer.Ordinal).Count());
		Assert.NotEmpty(encoded);
	}

	[Fact]
	public void DefaultStoreKeysAreUnprefixedForCompatibility()
	{
		// Entries written by the in-box dotnet/maui Tizen backend for the default store used the
		// raw key, so changing that would strand existing data.
		Assert.Equal("key", TizenStorageKeyEncoding.GetFullKey("key", null));
		Assert.Equal("key", TizenStorageKeyEncoding.GetFullKey("key", string.Empty));
		Assert.Equal("key", TizenPreferences.GetFullKey("key", null));
	}

	[Fact]
	public void ClearingASharedNameCannotMatchADifferentSharedName()
	{
		var prefix = TizenStorageKeyEncoding.GetSharedNamePrefix("a");

		// "a~b" encodes to "a\~b", which does not start with "a~", so Clear("a") leaves it alone.
		var otherStoreKey = TizenStorageKeyEncoding.GetFullKey("c", "a~b");

		Assert.False(otherStoreKey.StartsWith(prefix, StringComparison.Ordinal));

		// ...while keys genuinely in "a" do match.
		Assert.StartsWith(prefix, TizenStorageKeyEncoding.GetFullKey("c", "a"), StringComparison.Ordinal);
	}

	// -------------------------------------------------------------------------------------------
	// SecureStorage alias namespacing.
	// -------------------------------------------------------------------------------------------

	[Fact]
	public void SecureStorageAliasesAreNamespaced()
	{
		var alias = TizenSecureStorage.ToAlias("token");

		Assert.StartsWith(TizenSecureStorage.AliasPrefix, alias, StringComparison.Ordinal);
		Assert.EndsWith("token", alias, StringComparison.Ordinal);
	}

	[Fact]
	public void SecureStorageAliasesCannotCollideWithEachOther()
	{
		Assert.NotEqual(TizenSecureStorage.ToAlias("a~b"), TizenSecureStorage.ToAlias("a\\~b"));
	}

	[Theory]
	[InlineData("maui.tizen.securestorage:token", true)]
	[InlineData("org.example.app maui.tizen.securestorage:token", true)]
	[InlineData("some.other.component:token", false)]
	[InlineData("maui.tizen.securestorageX", false)]
	[InlineData("", false)]
	public void RecognisesOnlyItsOwnSecureStorageAliases(string alias, bool owned) =>
		Assert.Equal(owned, TizenSecureStorage.IsOwnedAlias(alias));

	[Fact]
	public void RemoveAllWouldNotTouchForeignSecureRepositoryAliases()
	{
		// RemoveAll previously deleted every alias the application could see, including
		// certificates and keys owned by unrelated components.
		string[] foreign =
		[
			"my.app.certificate",
			"vpn-client-key",
			"maui.tizen.securestorageNOTASEPARATOR",
		];

		Assert.All(foreign, alias => Assert.False(TizenSecureStorage.IsOwnedAlias(alias)));
	}

	// -------------------------------------------------------------------------------------------
	// Screenshot pixel-format handling.
	// -------------------------------------------------------------------------------------------

	[Theory]
	[InlineData(TizenPixelFormat.RGBA8888, TizenColorSpace.Rgba8888)]
	[InlineData(TizenPixelFormat.BGRA8888, TizenColorSpace.Bgra8888)]
	[InlineData(TizenPixelFormat.RGB888, TizenColorSpace.Rgb888)]
	[InlineData(TizenPixelFormat.RGB565, TizenColorSpace.Rgb565)]
	public void MapsScreenshotColorSpaces(TizenPixelFormat format, TizenColorSpace expected) =>
		Assert.Equal(expected, TizenScreenshotResult.MapColorSpace(format));

	[Fact]
	public void MapsBgrxToItsOpaqueTizenColorSpace()
	{
		// The X in BGRX is padding, not alpha. Tizen has an exact counterpart, so no conversion is
		// needed - but mapping it to Bgra8888 would have treated driver padding as transparency.
		Assert.Equal(
			TizenColorSpace.Bgrx8888,
			TizenScreenshotResult.MapColorSpace(TizenPixelFormat.BGR8888));
	}

	[Fact]
	public void MapsRgbxToRgbaBecauseTizenHasNoRgbxColorSpace() =>
		Assert.Equal(
			TizenColorSpace.Rgba8888,
			TizenScreenshotResult.MapColorSpace(TizenPixelFormat.RGB8888));

	[Fact]
	public void ForcesTheAlphaByteOpaqueWhenConvertingRgbx()
	{
		// Whatever the driver left in the padding byte must not become transparency.
		var pixels = new byte[] { 1, 2, 3, 0x00, 4, 5, 6, 0x7F };

		TizenScreenshotResult.MakeOpaque(pixels);

		Assert.Equal(0xFF, pixels[3]);
		Assert.Equal(0xFF, pixels[7]);

		// Colour channels are untouched.
		Assert.Equal(new byte[] { 1, 2, 3 }, pixels[..3]);
		Assert.Equal(new byte[] { 4, 5, 6 }, pixels[4..7]);
	}

	[Theory]
	[InlineData(TizenPixelFormat.RGBA8888, 4)]
	[InlineData(TizenPixelFormat.BGRA8888, 4)]
	[InlineData(TizenPixelFormat.RGB8888, 4)]
	[InlineData(TizenPixelFormat.BGR8888, 4)]
	[InlineData(TizenPixelFormat.RGB888, 3)]
	[InlineData(TizenPixelFormat.RGB565, 2)]
	public void MapsScreenshotPixelStrides(TizenPixelFormat format, int expected) =>
		Assert.Equal(expected, TizenScreenshotResult.BytesPerPixel(format));

	[Fact]
	public void RejectsScreenshotPixelFormatsWithoutAMatchingColorSpace()
	{
		Assert.Throws<FeatureNotSupportedException>(
			static () => TizenScreenshotResult.MapColorSpace(TizenPixelFormat.A8));
		Assert.Throws<FeatureNotSupportedException>(
			static () => TizenScreenshotResult.BytesPerPixel(TizenPixelFormat.A8));
	}

	[Fact]
	public void BoundsTheWaitForACaptureThatNeverFinishes()
	{
		// Capture.Finished is the only completion signal. Without a bound, a capture the native side
		// never finishes would hang the caller forever and leak the Capture handle.
		Assert.True(TizenScreenshot.CaptureTimeout > TimeSpan.Zero);
		Assert.True(TizenScreenshot.CaptureTimeout <= TimeSpan.FromMinutes(1));
	}

	// -------------------------------------------------------------------------------------------
	// Connectivity without a subscription.
	// -------------------------------------------------------------------------------------------

	[Fact]
	public void ConnectionProfilesDoesNotDependOnEventSubscription()
	{
		// The regression: profiles were only populated by the refresh kicked off when the first
		// ConnectivityChanged handler was added, so a plain query always saw an empty list.
		var property = typeof(TizenConnectivity).GetProperty(nameof(TizenConnectivity.ConnectionProfiles))!;
		var getter = property.GetGetMethod()!;

		var il = getter.GetMethodBody()!.GetILAsByteArray()!;

		// The getter must call the shared synchronous query rather than reading a cached field.
		var callsQuery = false;
		for (var i = 0; i + 4 < il.Length; i++)
		{
			if (il[i] != 0x28 && il[i] != 0x6F)
				continue;

			try
			{
				if (getter.Module.ResolveMember(BitConverter.ToInt32(il, i + 1)) is System.Reflection.MethodInfo m &&
					m.Name == nameof(TizenConnectivity.GetConnectionProfiles))
				{
					callsQuery = true;
					break;
				}
			}
			catch (Exception)
			{
				// Operand bytes misread as opcodes.
			}
		}

		Assert.True(callsQuery, "ConnectionProfiles must query connection state directly.");
	}

	[Fact]
	public void TreatsOnlyConnectedTransportsAsActive()
	{
		Assert.True(TizenConnectivity.IsConnected(TizenConnectionState.Connected));
		Assert.False(TizenConnectivity.IsConnected(TizenConnectionState.Disconnected));
		Assert.False(TizenConnectivity.IsConnected(TizenConnectionState.Deactivated));
	}
}
