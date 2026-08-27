using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
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
	public void DefaultAndNamedStoreKeysCannotCollide()
	{
		var defaultKey = TizenStorageKeyEncoding.GetFullKey("a~b", null);
		var namedKey = TizenStorageKeyEncoding.GetFullKey("b", "a");

		Assert.NotEqual(defaultKey, namedKey);
		Assert.StartsWith("maui.tizen.preferences:v2:d:", defaultKey, StringComparison.Ordinal);
		Assert.StartsWith("maui.tizen.preferences:v2:n:", namedKey, StringComparison.Ordinal);
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

	[Fact]
	public void ClearingDefaultPreferencesLeavesNamedAndLegacyEntriesAlone()
	{
		var store = new FakePreferencesStore
		{
			[TizenPreferences.GetFullKey("default", null)] = "delete-me",
			[TizenPreferences.GetFullKey("named", "shared")] = "keep-me",
			["legacy-default"] = "keep-conservatively",
			["shared~legacy"] = "keep-conservatively",
		};
		var preferences = new TizenPreferences(store);

		preferences.Clear();

		Assert.False(store.Contains(TizenPreferences.GetFullKey("default", null)));
		Assert.Equal("keep-me", store[TizenPreferences.GetFullKey("named", "shared")]);
		Assert.Equal("keep-conservatively", store["legacy-default"]);
		Assert.Equal("keep-conservatively", store["shared~legacy"]);
	}

	[Fact]
	public void PreferencesReadAndMigrateLegacyDefaultAndNamedEntries()
	{
		var store = new FakePreferencesStore
		{
			["legacy-default"] = "default-value",
			["shared~legacy-named"] = "named-value",
		};
		var preferences = new TizenPreferences(store);

		Assert.Equal("default-value", preferences.Get("legacy-default", "missing"));
		Assert.Equal("named-value", preferences.Get("legacy-named", "missing", "shared"));
		Assert.Equal("default-value", store["legacy-default"]);
		Assert.Equal("named-value", store["shared~legacy-named"]);
		Assert.Equal("default-value", store[TizenPreferences.GetFullKey("legacy-default", null)]);
		Assert.Equal("named-value", store[TizenPreferences.GetFullKey("legacy-named", "shared")]);
	}

	[Fact]
	public void PreferencesPreferVersionedEntriesOverLegacyAliases()
	{
		var store = new FakePreferencesStore
		{
			["token"] = "legacy-value",
			[TizenPreferences.GetFullKey("token", null)] = "new-value",
		};
		var preferences = new TizenPreferences(store);

		Assert.Equal("new-value", preferences.Get("token", "missing"));
		Assert.Equal("legacy-value", store["token"]);
	}

	[Fact]
	public void PreferencesRemoveTombstonePreventsAmbiguousLegacyResurrection()
	{
		var store = new FakePreferencesStore
		{
			["a~b"] = "ambiguous-legacy-value",
		};
		var preferences = new TizenPreferences(store);

		preferences.Remove("a~b");

		Assert.Equal("missing", preferences.Get("a~b", "missing"));
		Assert.Equal("ambiguous-legacy-value", store["a~b"]);
		Assert.Equal(
			"ambiguous-legacy-value",
			new TizenPreferences(store).Get("b", "missing", "a"));
	}

	[Fact]
	public void PreferencesClearTombstonePreventsStoreLegacyResurrection()
	{
		var store = new FakePreferencesStore
		{
			["legacy-default"] = "default-value",
			["shared~legacy"] = "named-value",
		};
		var preferences = new TizenPreferences(store);

		preferences.Clear();

		Assert.Equal("missing", preferences.Get("legacy-default", "missing"));
		Assert.Equal("named-value", preferences.Get("legacy", "missing", "shared"));
		Assert.Equal("default-value", store["legacy-default"]);
	}

	[Fact]
	public void PreferencesNewValueWinsAfterStoreClearTombstone()
	{
		var store = new FakePreferencesStore
		{
			["token"] = "legacy-value",
		};
		var preferences = new TizenPreferences(store);

		preferences.Clear();
		preferences.Set("token", "new-value");

		Assert.Equal("new-value", preferences.Get("token", "missing"));
		Assert.Equal("legacy-value", store["token"]);
	}

	[Fact]
	public async Task ConcurrentPreferencesMigrationCreatesOneVersionedCopy()
	{
		var store = new FakePreferencesStore
		{
			["token"] = "legacy-value",
		};
		var preferences = new TizenPreferences(store);

		var values = await Task.WhenAll(
			Task.Run(() => preferences.Get("token", "missing")),
			Task.Run(() => preferences.Get("token", "missing")));

		Assert.Equal(["legacy-value", "legacy-value"], values);
		Assert.Equal(1, store.GetSetCount(TizenPreferences.GetFullKey("token", null)));
		Assert.Equal("legacy-value", store["token"]);
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

	[Fact]
	public async Task SecureStorageReadsAndMigratesAnExactLegacyAlias()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "legacy-value",
			["unrelated"] = "leave-me",
		};
		var storage = new TizenSecureStorage(repository);

		var value = await storage.GetAsync("token");

		Assert.Equal("legacy-value", value);
		Assert.Equal("legacy-value", repository["token"]);
		Assert.Equal("legacy-value", repository[TizenSecureStorage.ToAlias("token")]);
		Assert.Equal("leave-me", repository["unrelated"]);
	}

	[Fact]
	public async Task SecureStoragePrefersTheNamespacedAliasOverLegacyData()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "legacy-value",
			[TizenSecureStorage.ToAlias("token")] = "new-value",
		};
		var storage = new TizenSecureStorage(repository);

		Assert.Equal("new-value", await storage.GetAsync("token"));
		Assert.Equal("legacy-value", repository["token"]);
	}

	[Fact]
	public void SecureStorageRemoveAllDeletesOnlyNamespacedAliases()
	{
		var repository = new FakeSecureRepository
		{
			["legacy-token"] = "legacy-value",
			["unrelated"] = "leave-me",
			[TizenSecureStorage.ToAlias("owned")] = "delete-me",
		};
		var storage = new TizenSecureStorage(repository);

		storage.RemoveAll();

		Assert.False(repository.Contains(TizenSecureStorage.ToAlias("owned")));
		Assert.Equal("legacy-value", repository["legacy-token"]);
		Assert.Equal("leave-me", repository["unrelated"]);
	}

	[Fact]
	public async Task SecureStorageMigrationFailureLeavesLegacyDataIntact()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "legacy-value",
			SaveException = new InvalidOperationException("save failed"),
		};
		var storage = new TizenSecureStorage(repository);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("token"));

		Assert.Equal("save failed", exception.Message);
		Assert.Equal("legacy-value", repository["token"]);
		Assert.False(repository.Contains(TizenSecureStorage.ToAlias("token")));
	}

	[Fact]
	public async Task SecureStorageRemoveTombstoneSuppressesLegacyFallback()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "unowned-raw-value",
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.False(storage.Remove("token"));

		Assert.Null(await storage.GetAsync("token"));
		Assert.Equal("unowned-raw-value", repository["token"]);
		Assert.True(tombstones.Contains("token"));
	}

	[Fact]
	public async Task SecureStorageRemoveAllSuppressesAllLegacyFallback()
	{
		var repository = new FakeSecureRepository
		{
			["legacy-token"] = "unowned-raw-value",
			[TizenSecureStorage.ToAlias("owned")] = "owned-value",
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		storage.RemoveAll();

		Assert.Null(await storage.GetAsync("legacy-token"));
		Assert.Equal("unowned-raw-value", repository["legacy-token"]);
		Assert.False(repository.Contains(TizenSecureStorage.ToAlias("owned")));
		Assert.True(tombstones.ContainsAll);
	}

	[Fact]
	public async Task SecureStorageSetWinsAfterRemoveTombstone()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "unowned-raw-value",
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		storage.Remove("token");
		await storage.SetAsync("token", "new-value");

		Assert.Equal("new-value", await storage.GetAsync("token"));
		Assert.Equal("unowned-raw-value", repository["token"]);
		Assert.False(tombstones.Contains("token"));
	}

	[Fact]
	public async Task ConcurrentSecureStorageMigrationCreatesOneOwnedCopy()
	{
		var repository = new FakeSecureRepository
		{
			["token"] = "legacy-value",
		};
		var storage = new TizenSecureStorage(repository, new FakeSecureStorageTombstones());

		var values = await Task.WhenAll(
			Task.Run(() => storage.GetAsync("token")),
			Task.Run(() => storage.GetAsync("token")));

		Assert.Equal(["legacy-value", "legacy-value"], values);
		Assert.Equal(1, repository.SuccessfulSaves);
		Assert.Equal("legacy-value", repository["token"]);
		Assert.Equal("legacy-value", repository[TizenSecureStorage.ToAlias("token")]);
	}

	// -------------------------------------------------------------------------------------------
	// TextToSpeech cancellation.
	// -------------------------------------------------------------------------------------------

	[Fact]
	public async Task TextToSpeechPreCancellationUsesTheCallerToken()
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		using var textToSpeech = new TizenTextToSpeech();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => textToSpeech.SpeakAsync("cancelled", cancelToken: source.Token));

		Assert.Equal(source.Token, exception.CancellationToken);
	}

	[Fact]
	public async Task TextToSpeechInFlightCancellationUsesTheCallerToken()
	{
		using var source = new CancellationTokenSource();
		var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var stopped = false;
		var cancellationWonBeforeStop = false;

		TizenTextToSpeech.CancelUtterance(utterance, source.Token, () =>
		{
			cancellationWonBeforeStop = utterance.Task.IsCanceled;
			stopped = true;
		});

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => utterance.Task);

		Assert.True(stopped);
		Assert.True(cancellationWonBeforeStop);
		Assert.Equal(source.Token, exception.CancellationToken);
	}

	[Fact]
	public async Task TextToSpeechAsyncErrorsFaultReadinessAndPlayback()
	{
		var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var failure = new InvalidOperationException("native failure");

		TizenTextToSpeech.FailPendingTasks(readiness, utterance, failure);

		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => readiness.Task));
		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => utterance.Task));
	}

	[Fact]
	public async Task TextToSpeechDisposalSettlesAllPendingTasks()
	{
		var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var utterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var textToSpeech = new TizenTextToSpeech();

		typeof(TizenTextToSpeech).GetField("_readiness", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.SetValue(textToSpeech, readiness);
		typeof(TizenTextToSpeech).GetField("_utterance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.SetValue(textToSpeech, utterance);

		textToSpeech.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => readiness.Task);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => utterance.Task);
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

	[Fact]
	public void ConnectivityProbesUnsupportedTransportsIndependently()
	{
		var profiles = TizenConnectivity.GetConnectionProfiles(
			static () => throw new PlatformNotSupportedException("No Wi-Fi"),
			static () => true,
			static () => throw new InvalidOperationException("No Ethernet"),
			static () => true);

		Assert.Equal([ConnectionProfile.Cellular, ConnectionProfile.Bluetooth], profiles);
	}

	[Fact]
	public void ConnectivityReturnsNoneWhenCurrentTransportCannotBeQueried() =>
		Assert.Equal(
			NetworkAccess.None,
			TizenConnectivity.GetNetworkAccess(
				static () => throw new PlatformNotSupportedException("Unsupported device profile")));

	[Fact]
	public async Task ContactLookupDisposesTheOwningRecord()
	{
		var record = new FakeContactRecord();
		var completion = new TaskCompletionSource<Contact?>(TaskCreationOptions.RunContinuationsAsynchronously);

		TizenContacts.CompleteLookup(completion, () => record, static _ => new Contact());

		Assert.NotNull(await completion.Task);
		Assert.True(record.Disposed);
	}

	[Fact]
	public async Task ContactProjectionFailureDisposesTheRecordAndFaultsTheTask()
	{
		var record = new FakeContactRecord();
		var completion = new TaskCompletionSource<Contact?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var failure = new InvalidOperationException("projection failed");

		TizenContacts.CompleteLookup<FakeContactRecord>(
			completion,
			() => record,
			_ => throw failure);

		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => completion.Task));
		Assert.True(record.Disposed);
	}

	[Fact]
	public void SensorStartRollbackResetsEvenWhenCleanupFails()
	{
		var stopped = false;
		var reset = false;

		TizenSensorBase<global::Tizen.Sensor.Sensor>.RollbackFailedStart(
			started: true,
			subscribed: true,
			stop: () => stopped = true,
			unsubscribe: static () => throw new InvalidOperationException("unsubscribe failed"),
			reset: () => reset = true);

		Assert.True(stopped);
		Assert.True(reset);
	}

	sealed class FakeSecureRepository : ITizenSecureRepository
	{
		readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
		readonly object _locker = new();

		public Exception? SaveException { get; init; }
		public int SuccessfulSaves { get; private set; }

		public string this[string alias]
		{
			get
			{
				lock (_locker)
					return Encoding.UTF8.GetString(_values[alias]);
			}
			set
			{
				lock (_locker)
					_values[alias] = Encoding.UTF8.GetBytes(value);
			}
		}

		public void Add(string alias, string value) =>
			_values.Add(alias, Encoding.UTF8.GetBytes(value));

		public bool Contains(string alias)
		{
			lock (_locker)
				return _values.ContainsKey(alias);
		}

		public byte[] Get(string alias)
		{
			lock (_locker)
				return _values.TryGetValue(alias, out var value)
					? value
					: throw new InvalidOperationException("alias not found");
		}

		public void Save(string alias, byte[] value)
		{
			lock (_locker)
			{
				if (SaveException is not null)
					throw SaveException;

				if (_values.ContainsKey(alias))
					throw new InvalidOperationException("alias already exists");

				_values.Add(alias, value);
				SuccessfulSaves++;
			}
		}

		public void RemoveAlias(string alias)
		{
			lock (_locker)
			{
				if (!_values.Remove(alias))
					throw new InvalidOperationException("alias not found");
			}
		}

		public IEnumerable<string> GetAliases()
		{
			lock (_locker)
				return _values.Keys.ToArray();
		}
	}

	sealed class FakeSecureStorageTombstones : ITizenSecureStorageTombstones
	{
		readonly HashSet<string> _keys = new(StringComparer.Ordinal);

		public bool ContainsAll { get; private set; }

		public bool Contains(string key) => _keys.Contains(key);

		public void Add(string key) => _keys.Add(key);

		public void Remove(string key) => _keys.Remove(key);

		public void AddAll() => ContainsAll = true;
	}

	sealed class FakePreferencesStore : ITizenPreferencesStore
	{
		readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
		readonly Dictionary<string, int> _setCounts = new(StringComparer.Ordinal);

		public IEnumerable<string> Keys => _values.Keys.ToArray();

		public object? this[string key]
		{
			get => _values[key];
			set => _values[key] = value;
		}

		public bool Contains(string key) =>
			_values.ContainsKey(key);

		public void Remove(string key) =>
			_values.Remove(key);

		public void Set<T>(string key, T value)
		{
			_values[key] = value;
			_setCounts[key] = GetSetCount(key) + 1;
		}

		public T Get<T>(string key) =>
			(T)_values[key]!;

		public int GetSetCount(string key) =>
			_setCounts.TryGetValue(key, out var count) ? count : 0;
	}

	sealed class FakeContactRecord : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() =>
			Disposed = true;
	}
}
