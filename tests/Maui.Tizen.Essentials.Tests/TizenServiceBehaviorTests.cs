using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;
using TizenBatteryPowerSource = Tizen.System.BatteryPowerSource;
using TizenColorSpace = Tizen.Multimedia.ColorSpace;
using TizenConnectionState = Tizen.Network.Connection.ConnectionState;
using TizenLocationType = Tizen.Location.LocationType;
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

	[Fact]
	public void PreferencesEncodeLongAndDateTimeThroughNativeSupportedStrings()
	{
		var store = new FakePreferencesStore { RejectUnsupportedNativeTypes = true };
		var preferences = new TizenPreferences(store);
		var dateTime = new DateTime(638901234567890123, DateTimeKind.Utc);
		var offset = new DateTimeOffset(2026, 8, 29, 13, 46, 0, TimeSpan.FromHours(-4));

		preferences.Set("long", long.MinValue);
		preferences.Set("date", dateTime);
		preferences.Set("offset", offset);

		Assert.IsType<string>(store[TizenPreferences.GetFullKey("long", null)]);
		Assert.IsType<string>(store[TizenPreferences.GetFullKey("date", null)]);
		Assert.IsType<string>(store[TizenPreferences.GetFullKey("offset", null)]);
		Assert.Equal(long.MinValue, preferences.Get("long", 0L));
		Assert.Equal(dateTime, preferences.Get("date", default(DateTime)));
		Assert.Equal(offset, preferences.Get("offset", default(DateTimeOffset)));
	}

	[Theory]
	[InlineData(0.1f)]
	[InlineData(float.MinValue)]
	[InlineData(float.MaxValue)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	public void PreferencesRoundTripFloatThroughNativeDoubleExactly(float value)
	{
		var store = new FakePreferencesStore { RejectUnsupportedNativeTypes = true };
		var preferences = new TizenPreferences(store);

		preferences.Set("float", value);

		Assert.IsType<double>(store[TizenPreferences.GetFullKey("float", null)]);
		Assert.Equal(value, preferences.Get("float", 0f));
	}

	[Fact]
	public void PreferencesRoundTripNaNThroughNativeDouble()
	{
		var store = new FakePreferencesStore { RejectUnsupportedNativeTypes = true };
		var preferences = new TizenPreferences(store);

		preferences.Set("float", float.NaN);

		Assert.True(float.IsNaN(preferences.Get("float", 0f)));
	}

	[Fact]
	public void PreferencesRejectCorruptLossyFloatRepresentation()
	{
		var store = new FakePreferencesStore
		{
			[TizenPreferences.GetFullKey("float", null)] = 0.1d,
		};
		var preferences = new TizenPreferences(store);

		Assert.Throws<InvalidOperationException>(() => preferences.Get("float", 0f));
	}

	[Fact]
	public void PreferencesReadLegacyUnsupportedRepresentationsAndMigrateThem()
	{
		var date = new DateTime(638901234567890123, DateTimeKind.Local);
		var store = new FakePreferencesStore
		{
			["legacy-long"] = long.MaxValue,
			["legacy-date"] = date.ToBinary(),
			["legacy-float"] = 1.25f,
		};
		var preferences = new TizenPreferences(store);

		Assert.Equal(long.MaxValue, preferences.Get("legacy-long", 0L));
		Assert.Equal(date, preferences.Get("legacy-date", default(DateTime)));
		Assert.Equal(1.25f, preferences.Get("legacy-float", 0f));

		Assert.IsType<string>(store[TizenPreferences.GetFullKey("legacy-long", null)]);
		Assert.IsType<string>(store[TizenPreferences.GetFullKey("legacy-date", null)]);
		Assert.IsType<double>(store[TizenPreferences.GetFullKey("legacy-float", null)]);
	}

	[Fact]
	public void PreferencesUpgradeUnsupportedValuesAlreadyStoredUnderVersionedKeys()
	{
		var longKey = TizenPreferences.GetFullKey("long", null);
		var dateKey = TizenPreferences.GetFullKey("date", null);
		var floatKey = TizenPreferences.GetFullKey("float", null);
		var date = new DateTime(638901234567890123, DateTimeKind.Utc);
		var store = new FakePreferencesStore
		{
			[longKey] = 42L,
			[dateKey] = date.ToBinary(),
			[floatKey] = 1.5f,
		};
		var preferences = new TizenPreferences(store);

		Assert.Equal(42L, preferences.Get("long", 0L));
		Assert.Equal(date, preferences.Get("date", default(DateTime)));
		Assert.Equal(1.5f, preferences.Get("float", 0f));
		Assert.IsType<string>(store[longKey]);
		Assert.IsType<string>(store[dateKey]);
		Assert.IsType<double>(store[floatKey]);
	}

	// -------------------------------------------------------------------------------------------
	// SecureStorage alias namespacing.
	// -------------------------------------------------------------------------------------------

	[Fact]
	public void SecureStorageAliasesAreNamespaced()
	{
		var alias = TizenSecureStorage.ToAlias("token");

		Assert.StartsWith(TizenSecureStorage.AliasPrefix, alias, StringComparison.Ordinal);
		Assert.StartsWith(TizenSecureStorage.AliasPrefix + "~v2~", alias, StringComparison.Ordinal);
		Assert.DoesNotContain(alias, char.IsWhiteSpace);
	}

	[Fact]
	public void SecureStorageAliasesCannotCollideWithEachOther()
	{
		string[] keys = ["a~b", "a\\~b", "a:b", "a b", "a\tb", "é", "e\u0301", "秘密"];

		Assert.Equal(keys.Length, keys.Select(TizenSecureStorage.ToAlias).Distinct().Count());
	}

	[Theory]
	[InlineData("api token")]
	[InlineData("api\ttoken")]
	[InlineData("ключ доступа")]
	[InlineData("a:b~c\\d")]
	public void SecureStorageAliasesAreWhitespaceFreeBase64Url(string key)
	{
		var encoded = TizenSecureStorage.ToAlias(key)[(TizenSecureStorage.AliasPrefix.Length + 4)..];

		Assert.NotEmpty(encoded);
		Assert.All(encoded, character => Assert.True(
			char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
	}

	[Fact]
	public void SecureStorageAliasVersionsCannotCollideInEitherDirection()
	{
		string[] keys =
		[
			"",
			"token",
			"~v2~dG9rZW4",
			"v2:dG9rZW4",
			"a~b",
			"a\\~b",
			"api token",
			"秘密",
		];

		foreach (var v2Key in keys)
		{
			foreach (var v1Key in keys)
			{
				Assert.NotEqual(
					TizenSecureStorage.ToAlias(v2Key),
					TizenSecureStorage.ToLegacyNamespacedAlias(v1Key));
			}
		}
	}

	[Fact]
	public void SecureStorageAliasVersionNamespacesAreDisjointForGeneratedKeys()
	{
		var keys = Enumerable.Range(0, 4096)
			.Select(value => (value % 4) switch
			{
				0 => value.ToString(),
				1 => "~v2~" + value,
				2 => "key~" + (char)('a' + value % 26) + value,
				_ => "ключ-" + value,
			})
			.ToArray();
		var v2Aliases = keys.Select(TizenSecureStorage.ToAlias).ToHashSet(StringComparer.Ordinal);
		var v1Aliases = keys.Select(TizenSecureStorage.ToLegacyNamespacedAlias).ToHashSet(StringComparer.Ordinal);

		Assert.Empty(v2Aliases.Intersect(v1Aliases));
	}

	[Fact]
	public void SecureStorageAliasEncodingIsInjectiveAcrossUtf16CodeUnits()
	{
		var keys = Enumerable.Range(0, 0x10000)
			.Where(value => value is < 0xD800 or > 0xDFFF)
			.Select(value => new string((char)value, 1))
			.ToArray();

		Assert.Equal(keys.Length, keys.Select(TizenSecureStorage.ToAlias).Distinct().Count());
	}

	[Fact]
	public async Task SecureStorageRejectsIllFormedUtf16BeforeMutation()
	{
		string[] keys =
		[
			new string((char)0xD800, 1),
			new string((char)0xDC00, 1),
			"prefix" + (char)0xD800 + "suffix",
		];

		foreach (var key in keys)
		{
			var repository = new FakeSecureRepository();
			var tombstones = new FakeSecureStorageTombstones();
			var storage = new TizenSecureStorage(repository, tombstones);

			await Assert.ThrowsAsync<EncoderFallbackException>(() => storage.SetAsync(key, "value"));
			Assert.Throws<EncoderFallbackException>(() => storage.Remove(key));
			await Assert.ThrowsAsync<EncoderFallbackException>(() => storage.GetAsync(key));
			Assert.Empty(repository.GetAliases());
			Assert.False(tombstones.Contains(key));
		}
	}

	[Theory]
	[InlineData("maui.tizen.securestorage:token", "org.example.app", true)]
	[InlineData("org.example.app maui.tizen.securestorage:token", "org.example.app", true)]
	[InlineData("org.other.app maui.tizen.securestorage:token", "org.example.app", false)]
	[InlineData("org.example.app maui.tizen.securestorage:token", null, false)]
	[InlineData("some.other.component:token", "org.example.app", false)]
	[InlineData("maui.tizen.securestorageX", "org.example.app", false)]
	[InlineData("", "org.example.app", false)]
	public void RecognisesOnlyItsOwnSecureStorageAliases(
		string alias,
		string? currentPackageId,
		bool owned) =>
		Assert.Equal(owned, TizenSecureStorage.IsOwnedAlias(alias, currentPackageId));

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
	public async Task SecureStorageReadsAndMigratesThePreviousNamespacedAlias()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToLegacyNamespacedAlias("token")] = "v1-value",
			["token"] = "raw-value",
		};
		var storage = new TizenSecureStorage(repository);

		Assert.Equal("v1-value", await storage.GetAsync("token"));
		Assert.Equal("v1-value", repository[TizenSecureStorage.ToAlias("token")]);
		Assert.False(repository.Contains(TizenSecureStorage.ToLegacyNamespacedAlias("token")));
		Assert.Equal("raw-value", repository["token"]);
	}

	[Fact]
	public async Task SecureStorageNewAliasWinsOverAllLegacyAliases()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToAlias("token")] = "v2-value",
			[TizenSecureStorage.ToLegacyNamespacedAlias("token")] = "v1-value",
			["token"] = "raw-value",
		};

		Assert.Equal("v2-value", await new TizenSecureStorage(repository).GetAsync("token"));
	}

	[Fact]
	public async Task SecureStorageWhitespaceKeyUsesOnlyNativeSafeAliases()
	{
		var repository = new FakeSecureRepository
		{
			RejectWhitespaceAliases = true,
		};
		var storage = new TizenSecureStorage(repository);

		await storage.SetAsync("api token", "value");

		Assert.Equal("value", await storage.GetAsync("api token"));
		Assert.False(TizenSecureStorage.CanUseLegacyNamespacedAlias("api token"));
	}

	[Fact]
	public async Task SecureStorageInvalidRawLegacyAliasIsAbsentBeforeAnyWrite()
	{
		var repository = new FakeSecureRepository
		{
			RejectWhitespaceAliases = true,
		};
		var storage = new TizenSecureStorage(repository);

		Assert.Null(await storage.GetAsync("api token"));
		Assert.Empty(repository.GetAliases());
		Assert.Equal(0, repository.SuccessfulSaves);
	}

	[Fact]
	public async Task SecureStorageDoesNotTruncateAliasesRejectedByTheNativeLimit()
	{
		var repository = new FakeSecureRepository
		{
			MaximumAliasLength = TizenSecureStorage.AliasPrefix.Length + 8,
		};
		var storage = new TizenSecureStorage(repository);

		await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync(new string('x', 64), "value"));
		Assert.Empty(repository.GetAliases());
	}

	[Fact]
	public void SecureStorageRemoveDeletesPreviousOwnedAlias()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToLegacyNamespacedAlias("token")] = "v1-value",
		};

		Assert.True(new TizenSecureStorage(repository).Remove("token"));
		Assert.False(repository.Contains(TizenSecureStorage.ToLegacyNamespacedAlias("token")));
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
	public void SecureRepositoryNormalizesOnlyDocumentedNoAliasArgumentException()
	{
		Assert.Empty(new TizenSecureRepository(
			static () => throw new ArgumentException("there's no alias to get")).GetAliases());

		var genuine = Assert.Throws<ArgumentException>(() =>
			new TizenSecureRepository(
				static () => throw new ArgumentException("invalid repository argument", "alias"))
			.GetAliases());
		Assert.Equal("alias", genuine.ParamName);

		Assert.Throws<InvalidOperationException>(() =>
			new TizenSecureRepository(
				static () => throw new InvalidOperationException("repository failed"))
			.GetAliases());
	}

	[Fact]
	public async Task SecureStorageEmptyRepositorySupportsFirstReadWriteRemoveAndRemoveAll()
	{
		var repository = new FakeSecureRepository();
		var storage = new TizenSecureStorage(repository);

		Assert.Null(await storage.GetAsync("token"));
		await storage.SetAsync("token", "value");
		Assert.Equal("value", await storage.GetAsync("token"));
		Assert.True(storage.Remove("token"));
		Assert.Null(await storage.GetAsync("token"));
		storage.RemoveAll();
	}

	[Fact]
	public void SecureStorageRemoveAllPreservesQualifiedForeignPackageAliases()
	{
		var repository = new FakeSecureRepository
		{
			["org.current.app " + TizenSecureStorage.ToAlias("mine")] = "delete-me",
			["org.foreign.app " + TizenSecureStorage.ToAlias("theirs")] = "keep-me",
		};
		var storage = new TizenSecureStorage(
			repository,
			currentPackageId: static () => "org.current.app");

		storage.RemoveAll();

		Assert.False(repository.Contains("org.current.app " + TizenSecureStorage.ToAlias("mine")));
		Assert.Equal(
			"keep-me",
			repository["org.foreign.app " + TizenSecureStorage.ToAlias("theirs")]);
	}

	[Fact]
	public async Task SecureStorageReplacementRestoresV1AndV2WhenCommitFails()
	{
		var v2 = TizenSecureStorage.ToAlias("token");
		var v1 = TizenSecureStorage.ToLegacyNamespacedAlias("token");
		var repository = new FakeSecureRepository
		{
			[v2] = "old-v2",
			[v1] = "old-v1",
		};
		repository.FailSaveCalls.Add(2);
		var storage = new TizenSecureStorage(repository);

		await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("token", "new"));

		Assert.Equal("old-v2", repository[v2]);
		Assert.Equal("old-v1", repository[v1]);
		Assert.DoesNotContain(
			repository.GetAliases(),
			alias => alias.StartsWith(TizenSecureStorage.AliasPrefix + "~tx~", StringComparison.Ordinal));
	}

	[Fact]
	public async Task SecureStorageSuccessfulReplacementRemovesPreviousVersionDuplicate()
	{
		var v2 = TizenSecureStorage.ToAlias("token");
		var v1 = TizenSecureStorage.ToLegacyNamespacedAlias("token");
		var repository = new FakeSecureRepository
		{
			[v2] = "old-v2",
			[v1] = "old-v1",
		};
		var storage = new TizenSecureStorage(repository);

		await storage.SetAsync("token", "new");

		Assert.Equal("new", repository[v2]);
		Assert.False(repository.Contains(v1));
		Assert.Single(repository.GetAliases());
	}

	[Fact]
	public async Task SecureStorageInterruptedReplacementRetainsAndRecoversStagedValue()
	{
		var v2 = TizenSecureStorage.ToAlias("token");
		var repository = new FakeSecureRepository
		{
			[v2] = "old",
		};
		repository.FailSaveCalls.UnionWith([2, 3]);
		var storage = new TizenSecureStorage(repository);

		await Assert.ThrowsAsync<AggregateException>(() => storage.SetAsync("token", "new"));
		Assert.Contains(
			repository.GetAliases(),
			alias => alias.StartsWith(TizenSecureStorage.AliasPrefix + "~tx~", StringComparison.Ordinal));

		Assert.Equal("new", await storage.GetAsync("token"));
		Assert.Equal("new", repository[v2]);
		Assert.DoesNotContain(
			repository.GetAliases(),
			alias => alias.StartsWith(TizenSecureStorage.AliasPrefix + "~tx~", StringComparison.Ordinal));
	}

	[Fact]
	public async Task SecureStorageRejectsAmbiguousDuplicateInterruptedReplacements()
	{
		var keyPrefix = TizenSecureStorage.AliasPrefix + "~tx~" +
			Convert.ToBase64String(Encoding.UTF8.GetBytes("token")).TrimEnd('=') + "~";
		var repository = new FakeSecureRepository
		{
			[keyPrefix + "one"] = "first",
			[keyPrefix + "two"] = "second",
		};

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => new TizenSecureStorage(repository).GetAsync("token"));

		Assert.Contains("Multiple interrupted", exception.Message, StringComparison.Ordinal);
		Assert.Equal(2, repository.GetAliases().Count());
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
	public async Task SecureStorageSetAfterRemoveAllSupersedesGlobalTombstone()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToAlias("token")] = "old-value",
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		storage.RemoveAll();
		Assert.Null(await storage.GetAsync("token"));

		await storage.SetAsync("token", "new-value");

		Assert.Equal("new-value", await storage.GetAsync("token"));
		Assert.True(tombstones.ContainsAll);
		Assert.False(tombstones.Contains("token"));
	}

	[Theory]
	[InlineData("current")]
	[InlineData("v1")]
	[InlineData("staged")]
	public async Task SecureStorageRemoveFailureIsSurfacedButTombstoneDominates(string failingAlias)
	{
		var current = TizenSecureStorage.ToAlias("token");
		var v1 = TizenSecureStorage.ToLegacyNamespacedAlias("token");
		var staged = TizenSecureStorage.AliasPrefix + "~tx~dG9rZW4~crash";
		var repository = new FakeSecureRepository
		{
			[current] = "current-value",
			[v1] = "v1-value",
			[staged] = "staged-value",
		};
		repository.FailRemoveAliases.Add(failingAlias switch
		{
			"current" => current,
			"v1" => v1,
			_ => staged,
		});
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.ThrowsAny<Exception>(() => storage.Remove("token"));
		Assert.Null(await storage.GetAsync("token"));
		Assert.True(tombstones.Contains("token"));
	}

	[Fact]
	public async Task SecureStorageRemoveSurfacesEnumerationFailureAndKeepsDeletionCommitted()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToAlias("token")] = "current-value",
			GetAliasesException = new IOException("enumeration failed"),
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.Throws<IOException>(() => storage.Remove("token"));
		Assert.Null(await storage.GetAsync("token"));
	}

	[Fact]
	public async Task SecureStorageRemoveAllSurfacesEnumerationFailureAndSuppressesExistingData()
	{
		var repository = new FakeSecureRepository
		{
			[TizenSecureStorage.ToAlias("token")] = "current-value",
			GetAliasesException = new IOException("enumeration failed"),
		};
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.Throws<IOException>(storage.RemoveAll);
		Assert.True(tombstones.ContainsAll);
		Assert.Null(await storage.GetAsync("token"));
	}

	[Fact]
	public async Task SecureStorageRemoveAllSurfacesDeleteFailureAndSuppressesUndeletedAlias()
	{
		var alias = TizenSecureStorage.ToAlias("token");
		var repository = new FakeSecureRepository { [alias] = "current-value" };
		repository.FailRemoveAliases.Add(alias);
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.ThrowsAny<Exception>(storage.RemoveAll);
		Assert.True(repository.Contains(alias));
		Assert.Null(await storage.GetAsync("token"));
	}

	[Fact]
	public async Task SecureStorageDeletionTombstonePreventsCrashStagingRecovery()
	{
		var staged = TizenSecureStorage.AliasPrefix + "~tx~dG9rZW4~crash";
		var repository = new FakeSecureRepository { [staged] = "staged-old-value" };
		repository.FailRemoveAliases.Add(staged);
		var tombstones = new FakeSecureStorageTombstones();
		var storage = new TizenSecureStorage(repository, tombstones);

		Assert.ThrowsAny<Exception>(() => storage.Remove("token"));
		Assert.Null(await storage.GetAsync("token"));
		Assert.True(repository.Contains(staged));
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

	[Fact]
	public async Task PreferencesAndSecureStorageShareOneSynchronizedBackingStore()
	{
		var preferenceStore = new FakePreferencesStore
		{
			RequireSynchronization = true,
			["legacy-preference"] = "legacy-value",
		};
		var preferences = new TizenPreferences(preferenceStore);
		var repository = new FakeSecureRepository
		{
			["secret"] = "unowned-raw-value",
		};
		var secureStorage = new TizenSecureStorage(
			repository,
			new TizenSecureStorageTombstones(preferenceStore));
		var cancellationToken = TestContext.Current.CancellationToken;

		await Task.WhenAll(
			Task.Run(() => preferences.Get("legacy-preference", "missing"), cancellationToken),
			Task.Run(() => preferences.Clear(), cancellationToken),
			Task.Run(() => secureStorage.Remove("secret"), cancellationToken),
			Task.Run(() => secureStorage.SetAsync("secret", "new-value"), cancellationToken));

		Assert.Equal(1, preferenceStore.MaximumConcurrentOperations);
		Assert.Equal("unowned-raw-value", repository["secret"]);
		Assert.Contains(await secureStorage.GetAsync("secret"), new string?[] { null, "new-value" });
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
	public async Task TextToSpeechRunsEveryNativeOperationOnTheDispatcherThread()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"hello",
			cancelToken: TestContext.Current.CancellationToken);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;
		await client.CompleteAsync(client.LastUtteranceId);
		await speak;

		var languages = await textToSpeech.GetSupportedVoiceLanguagesAsync();
		Assert.Equal(["en_US"], languages);

		textToSpeech.Dispose();
		await client.Disposed.Task;
		Assert.Empty(client.WrongThreadOperations);
	}

	[Fact]
	public async Task TextToSpeechCachesSpeedRangeBeforePrepareAndAppliesRateWhenReady()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"rate",
			new SpeechOptions { Rate = 1.0f },
			TestContext.Current.CancellationToken);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;

		Assert.Equal(["GetSpeedRange", "Prepare", "AddText", "Play"], client.Operations.Take(4));
		Assert.Equal(0, client.LastSpeed);

		await client.CompleteAsync(client.LastUtteranceId);
		await speak;
	}

	[Theory]
	[InlineData(0.1f, -10)]
	[InlineData(0.55f, -5)]
	[InlineData(1.0f, 0)]
	[InlineData(1.5f, 10)]
	[InlineData(2.0f, 20)]
	public void TextToSpeechMapsMauiRatePiecewiseAcrossNativeRange(float rate, int expected) =>
		Assert.Equal(
			expected,
			TizenTextToSpeech.ResolveRate(new(-10, 0, 20), rate));

	[Fact]
	public void TextToSpeechUsesNativeNormalWhenRateIsNull() =>
		Assert.Equal(
			7,
			TizenTextToSpeech.ResolveRate(new(-4, 7, 19), null));

	[Theory]
	[InlineData(0.09f)]
	[InlineData(2.01f)]
	public void TextToSpeechRejectsRatesOutsideMauiRange(float rate) =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			TizenTextToSpeech.ResolveRate(new(-10, 0, 20), rate));

	[Fact]
	public async Task TextToSpeechCancellationRetiresBeforeSettlingAndTeardownRunsOnDispatcher()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);
		using var cancellation = new CancellationTokenSource();

		var speak = textToSpeech.SpeakAsync("cancel me", cancelToken: cancellation.Token);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;
		var queued = textToSpeech.SpeakAsync(
			"fresh",
			cancelToken: TestContext.Current.CancellationToken);
		cancellation.Cancel();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speak);
		await client.Disposed.Task;
		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.True(client.StoppedBeforeDisposed);
		Assert.Empty(client.WrongThreadOperations);

		var freshClient = await factory.WaitForClientAsync(2);
		Assert.NotSame(client, freshClient);
		await freshClient.Played.Task;
		await freshClient.CompleteAsync(freshClient.LastUtteranceId);
		await queued;
	}

	[Fact]
	public async Task TextToSpeechNativeErrorDefersTeardownAndQueuedRequestUsesFreshClient()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var active = textToSpeech.SpeakAsync(
			"active",
			cancelToken: TestContext.Current.CancellationToken);
		var firstClient = await factory.WaitForClientAsync();
		await firstClient.Played.Task;
		var queued = textToSpeech.SpeakAsync(
			"queued",
			cancelToken: TestContext.Current.CancellationToken);

		await firstClient.FailAsync(firstClient.LastUtteranceId);
		await Assert.ThrowsAsync<InvalidOperationException>(() => active);
		await firstClient.Disposed.Task;

		var secondClient = await factory.WaitForClientAsync(2);
		Assert.NotSame(firstClient, secondClient);
		await secondClient.Played.Task;
		await secondClient.CompleteAsync(secondClient.LastUtteranceId);
		await queued;
		Assert.False(firstClient.DisposedInsideCallback);
		Assert.Empty(firstClient.WrongThreadOperations);
		Assert.Empty(secondClient.WrongThreadOperations);
	}

	[Fact]
	public async Task TextToSpeechErrorTeardownIsPostedWhenMainThreadDispatchWouldInline()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"active",
			cancelToken: TestContext.Current.CancellationToken);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;
		await client.FailAsync(client.LastUtteranceId);

		await Assert.ThrowsAsync<InvalidOperationException>(() => speak);
		await client.Disposed.Task;
		Assert.False(client.DisposedInsideCallback);
	}

	[Fact]
	public async Task TextToSpeechDisposeBeforeQueuedConstructionDoesNotCreateClient()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var blocked = dispatcher.BlockNextAction();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"never constructed",
			cancelToken: TestContext.Current.CancellationToken);
		await blocked.Queued;
		textToSpeech.Dispose();
		blocked.Release();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => speak);
		Assert.Equal(0, factory.ClientCount);
	}

	[Fact]
	public async Task TextToSpeechCancellationBeforeQueuedAddTextSkipsNativeCall()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);
		await textToSpeech.GetSupportedVoiceLanguagesAsync();
		var client = await factory.WaitForClientAsync();
		var blocked = dispatcher.BlockNextAction();
		using var cancellation = new CancellationTokenSource();

		var speak = textToSpeech.SpeakAsync("cancelled", cancelToken: cancellation.Token);
		await blocked.Queued;
		cancellation.Cancel();
		blocked.Release();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speak);
		await client.Disposed.Task;
		Assert.Equal(0, client.AddTextCalls);
		Assert.Equal(0, client.PlayCalls);
	}

	[Fact]
	public async Task TextToSpeechCancellationBeforeQueuedPlaySkipsPlayback()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher)
		{
			BlockAfterAddText = true,
		};
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);
		using var cancellation = new CancellationTokenSource();

		var speak = textToSpeech.SpeakAsync("cancelled", cancelToken: cancellation.Token);
		var client = await factory.WaitForClientAsync();
		var blocked = await factory.WaitForBlockedActionAsync();
		await blocked.Queued;
		cancellation.Cancel();
		blocked.Release();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speak);
		await client.Disposed.Task;
		Assert.Equal(1, client.AddTextCalls);
		Assert.Equal(0, client.PlayCalls);
		Assert.True(client.StoppedBeforeDisposed);
	}

	[Fact]
	public async Task TextToSpeechDisposeFailureDoesNotStopDispatcherLoop()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher)
		{
			ThrowOnDispose = true,
		};
		var textToSpeech = new TizenTextToSpeech(dispatcher, factory);
		await textToSpeech.GetSupportedVoiceLanguagesAsync();
		var client = await factory.WaitForClientAsync();

		textToSpeech.Dispose();
		await client.DisposeAttempted.Task;
		var dispatcherStillRunning = false;
		await dispatcher.InvokeAsync(() => dispatcherStillRunning = true);
		Assert.True(dispatcherStillRunning);
	}

	[Fact]
	public async Task TextToSpeechPlayFailureRetiresClientBeforeRetry()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher)
		{
			FailFirstPlay = true,
		};
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			textToSpeech.SpeakAsync(
				"fails",
				cancelToken: TestContext.Current.CancellationToken));
		var failedClient = await factory.WaitForClientAsync();
		await failedClient.Disposed.Task;
		Assert.True(failedClient.StoppedBeforeDisposed);

		var retry = textToSpeech.SpeakAsync(
			"retry",
			cancelToken: TestContext.Current.CancellationToken);
		var retryClient = await factory.WaitForClientAsync(2);
		await retryClient.Played.Task;
		await retryClient.CompleteAsync(retryClient.LastUtteranceId);
		await retry;
		Assert.NotSame(failedClient, retryClient);
	}

	[Fact]
	public async Task TextToSpeechIgnoresOldUtteranceCompletion()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"current",
			cancelToken: TestContext.Current.CancellationToken);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;
		await client.CompleteAsync(client.LastUtteranceId - 1);
		Assert.False(speak.IsCompleted);
		await client.CompleteAsync(client.LastUtteranceId);
		await speak;
	}

	[Fact]
	public async Task TextToSpeechVoiceQueryWaitsBehindActiveSpeech()
	{
		using var dispatcher = new FakeTextToSpeechDispatcher();
		var factory = new FakeTextToSpeechClientFactory(dispatcher);
		using var textToSpeech = new TizenTextToSpeech(dispatcher, factory);

		var speak = textToSpeech.SpeakAsync(
			"active",
			cancelToken: TestContext.Current.CancellationToken);
		var client = await factory.WaitForClientAsync();
		await client.Played.Task;
		var languages = textToSpeech.GetSupportedVoiceLanguagesAsync();

		await Task.Delay(25, TestContext.Current.CancellationToken);
		Assert.Equal(0, client.GetSupportedVoicesCalls);
		await client.CompleteAsync(client.LastUtteranceId);
		await speak;
		Assert.Equal(["en_US"], await languages);
		Assert.Equal(1, client.GetSupportedVoicesCalls);
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

	[Fact]
	public async Task QueuedTextToSpeechReinitializesAfterActiveCancellation()
	{
		using var speakLock = new SemaphoreSlim(1, 1);
		using var cancellation = new CancellationTokenSource();
		var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var activeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var currentClient = "stale";
		string? queuedClient = null;

		var active = TizenTextToSpeech.RunWithCurrentClientAsync(
			speakLock,
			cancellation.Token,
			_ => Task.FromResult(currentClient),
			async _ =>
			{
				activeStarted.SetResult();
				await activeCompletion.Task;
			});

		await activeStarted.Task;
		var queued = TizenTextToSpeech.RunWithCurrentClientAsync(
			speakLock,
			CancellationToken.None,
			_ => Task.FromResult(currentClient),
			client =>
			{
				queuedClient = client;
				return Task.CompletedTask;
			});

		currentClient = "fresh";
		cancellation.Cancel();
		activeCompletion.SetCanceled(cancellation.Token);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
		await queued;
		Assert.Equal("fresh", queuedClient);
	}

	[Fact]
	public async Task QueuedTextToSpeechReinitializesAfterActiveNativeError()
	{
		using var speakLock = new SemaphoreSlim(1, 1);
		var activeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var activeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var currentClient = "stale";
		string? queuedClient = null;

		var active = TizenTextToSpeech.RunWithCurrentClientAsync(
			speakLock,
			CancellationToken.None,
			_ => Task.FromResult(currentClient),
			async _ =>
			{
				activeStarted.SetResult();
				await activeCompletion.Task;
			});

		await activeStarted.Task;
		var queued = TizenTextToSpeech.RunWithCurrentClientAsync(
			speakLock,
			CancellationToken.None,
			_ => Task.FromResult(currentClient),
			client =>
			{
				queuedClient = client;
				return Task.CompletedTask;
			});

		currentClient = "fresh";
		activeCompletion.SetException(new InvalidOperationException("native error"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => active);
		await queued;
		Assert.Equal("fresh", queuedClient);
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
	public void ConnectivityRollsBackAPartialNativeSubscription()
	{
		var subscribed = false;

		Assert.Throws<InvalidOperationException>(() =>
			TizenConnectivity.StartTransactional(
				() =>
				{
					subscribed = true;
					throw new InvalidOperationException("native add failed");
				},
				() => subscribed = false));

		Assert.False(subscribed);
	}

	[Fact]
	public void ShareFilePayloadHandlesZeroFiles()
	{
		var payload = TizenShare.CreateFilePayload([]);

		Assert.Equal(TizenAppControlOperations.Share, payload.Operation);
		Assert.Equal(TizenFileMimeTypes.All, payload.Mime);
		Assert.Empty(payload.Paths);
	}

	[Fact]
	public void ShareFilePayloadHandlesOneFile()
	{
		var payload = TizenShare.CreateFilePayload([new ShareFile("/tmp/photo.png")]);
		string? scalarPath = null;
		IEnumerable<string>? multiplePaths = null;

		TizenShare.AddPaths(
			payload,
			(_, path) => scalarPath = path,
			(_, paths) => multiplePaths = paths);

		Assert.Equal(TizenAppControlOperations.Share, payload.Operation);
		Assert.Equal(TizenFileMimeTypes.ImagePng, payload.Mime);
		Assert.Equal(["/tmp/photo.png"], payload.Paths);
		Assert.Equal("/tmp/photo.png", scalarPath);
		Assert.Null(multiplePaths);
	}

	[Fact]
	public void ShareFilePayloadUsesMultiShareAndAllPathsOnce()
	{
		var payload = TizenShare.CreateFilePayload(
		[
			new ShareFile("/tmp/photo.png", TizenFileMimeTypes.ImagePng),
			new ShareFile("/tmp/document.pdf", TizenFileMimeTypes.Pdf),
		]);
		string? scalarPath = null;
		IEnumerable<string>? multiplePaths = null;

		TizenShare.AddPaths(
			payload,
			(_, path) => scalarPath = path,
			(_, paths) => multiplePaths = paths);

		Assert.Equal(TizenAppControlOperations.MultiShare, payload.Operation);
		Assert.Equal(TizenFileMimeTypes.All, payload.Mime);
		Assert.Equal(["/tmp/photo.png", "/tmp/document.pdf"], payload.Paths);
		Assert.Null(scalarPath);
		Assert.Equal(payload.Paths, multiplePaths);
	}

	[Fact]
	public void EmailAttachmentPayloadUsesEnumerablePathsAndCompatibleMime()
	{
		var payload = TizenEmail.CreateAttachmentPayload(
		[
			new EmailAttachment("/tmp/first.png", TizenFileMimeTypes.ImagePng),
			new EmailAttachment("/tmp/second.png", TizenFileMimeTypes.ImagePng),
		]);

		Assert.Equal(TizenAppControlOperations.Compose, payload.Operation);
		Assert.Equal(TizenFileMimeTypes.ImagePng, payload.Mime);
		Assert.Equal(["/tmp/first.png", "/tmp/second.png"], payload.Paths);
	}

	[Fact]
	public void EmailPathOnlyAttachmentResolvesMimeFromExtension()
	{
		var payload = TizenEmail.CreateAttachmentPayload(
			[new EmailAttachment("/tmp/document.pdf")]);

		Assert.Equal(TizenFileMimeTypes.Pdf, payload.Mime);
		Assert.Equal(["/tmp/document.pdf"], payload.Paths);
	}

	[Fact]
	public void MapPlacemarkUsesViewOperation()
	{
		var request = TizenMap.CreatePlacemarkRequest(
			new Placemark { Locality = "Seattle", CountryName = "USA" },
			new MapLaunchOptions());

		Assert.Equal(TizenAppControlOperations.View, request.Operation);
		Assert.StartsWith("geo:0,0?q=", request.Uri, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("geo:47.6,-122.3")]
	[InlineData("geo:0,0?q=Seattle")]
	public void LauncherGeoUrisUseViewOperation(string uri) =>
		Assert.Equal(TizenAppControlOperations.View, TizenLauncher.GetOperation(new Uri(uri)));

	[Fact]
	public void LauncherHandlerProbeTreatsNullAndExceptionsAsUnavailable()
	{
		Assert.False(TizenLauncher.HasHandler(static () => null));
		Assert.False(TizenLauncher.HasHandler(
			static () => throw new InvalidOperationException("native lookup failed")));
		Assert.True(TizenLauncher.HasHandler(static () => ["org.example.handler"]));
	}

	[Fact]
	public void GeolocationIsEnabledRequiresAnEnabledSupportedService()
	{
		Assert.False(TizenGeolocation.IsLocationEnabled(
			() => true,
			() => true,
			_ => false));
		Assert.True(TizenGeolocation.IsLocationEnabled(
			() => true,
			() => false,
			type => type == TizenLocationType.Gps));
		Assert.True(TizenGeolocation.IsLocationEnabled(
			() => false,
			() => true,
			type => type == TizenLocationType.Wps));
	}

	[Fact]
	public void GeolocationIsEnabledIgnoresUnsupportedServiceQueries()
	{
		Assert.False(TizenGeolocation.IsLocationEnabled(
			() => true,
			() => false,
			_ => throw new NotSupportedException()));
	}

	sealed class FakeTextToSpeechDispatcher : ITizenTextToSpeechDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly Thread _thread;
		readonly ManualResetEventSlim _started = new();
		readonly object _blockLock = new();
		BlockedAction? _nextBlockedAction;

		public FakeTextToSpeechDispatcher()
		{
			_thread = new Thread(Run)
			{
				IsBackground = true,
				Name = "Fake Tizen Ecore thread",
			};
			_thread.Start();
			_started.Wait();
		}

		public int ThreadId { get; private set; }

		public Task InvokeAsync(Action action)
		{
			if (Thread.CurrentThread.ManagedThreadId == ThreadId)
			{
				action();
				return Task.CompletedTask;
			}

			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_work.Add(() =>
			{
				try
				{
					action();
					completion.SetResult();
				}
				catch (Exception exception)
				{
					completion.SetException(exception);
				}
			});
			return completion.Task;
		}

		public Task<T> InvokeAsync<T>(Func<T> action)
		{
			if (Thread.CurrentThread.ManagedThreadId == ThreadId)
			{
				return Task.FromResult(action());
			}

			var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			_work.Add(() =>
			{
				try
				{
					completion.SetResult(action());
				}
				catch (Exception exception)
				{
					completion.SetException(exception);
				}
			});
			return completion.Task;
		}

		public void Post(Action action) => _work.Add(action);

		public BlockedAction BlockNextAction()
		{
			lock (_blockLock)
			{
				if (_nextBlockedAction is not null)
					throw new InvalidOperationException("An action is already blocked.");

				_nextBlockedAction = new BlockedAction();
				return _nextBlockedAction;
			}
		}

		public void Dispose()
		{
			_work.CompleteAdding();
			_thread.Join();
			_started.Dispose();
			_work.Dispose();
		}

		void Run()
		{
			ThreadId = Thread.CurrentThread.ManagedThreadId;
			_started.Set();
			foreach (var action in _work.GetConsumingEnumerable())
			{
				BlockedAction? blocked;
				lock (_blockLock)
				{
					blocked = _nextBlockedAction;
					_nextBlockedAction = null;
				}

				if (blocked is not null)
				{
					blocked.MarkQueued();
					blocked.Wait();
				}

				action();
			}
		}

		public sealed class BlockedAction
		{
			readonly TaskCompletionSource _queued =
				new(TaskCreationOptions.RunContinuationsAsynchronously);
			readonly ManualResetEventSlim _release = new();

			public Task Queued => _queued.Task;

			public void Release() => _release.Set();

			internal void MarkQueued() => _queued.TrySetResult();

			internal void Wait() => _release.Wait(TestContext.Current.CancellationToken);
		}
	}

	sealed class FakeTextToSpeechClientFactory : ITizenTextToSpeechClientFactory
	{
		readonly FakeTextToSpeechDispatcher _dispatcher;
		readonly List<FakeTextToSpeechClient> _clients = [];
		readonly TaskCompletionSource<FakeTextToSpeechDispatcher.BlockedAction> _blockedAction =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public FakeTextToSpeechClientFactory(FakeTextToSpeechDispatcher dispatcher)
		{
			_dispatcher = dispatcher;
		}

		public bool FailFirstPlay { get; init; }

		public bool BlockAfterAddText { get; init; }

		public bool ThrowOnDispose { get; init; }

		public int ClientCount
		{
			get
			{
				lock (_clients)
					return _clients.Count;
			}
		}

		public ITizenTextToSpeechClient Create()
		{
			Assert.Equal(_dispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
			var client = new FakeTextToSpeechClient(
				_dispatcher,
				throwOnPlay: FailFirstPlay && _clients.Count == 0,
				throwOnDispose: ThrowOnDispose,
				onAddText: BlockAfterAddText
					? () => _blockedAction.TrySetResult(_dispatcher.BlockNextAction())
					: null);
			lock (_clients)
				_clients.Add(client);
			return client;
		}

		public async Task<FakeTextToSpeechClient> WaitForClientAsync(int count = 1)
		{
			while (true)
			{
				lock (_clients)
				{
					if (_clients.Count >= count)
						return _clients[count - 1];
				}

				await Task.Delay(1, TestContext.Current.CancellationToken);
			}
		}

		public Task<FakeTextToSpeechDispatcher.BlockedAction> WaitForBlockedActionAsync() =>
			_blockedAction.Task.WaitAsync(TestContext.Current.CancellationToken);
	}

	sealed class FakeTextToSpeechClient : ITizenTextToSpeechClient
	{
		readonly FakeTextToSpeechDispatcher _dispatcher;
		readonly bool _throwOnPlay;
		readonly bool _throwOnDispose;
		readonly Action? _onAddText;
		bool _insideErrorCallback;
		bool _stopped;
		bool _prepared;
		int _nextUtteranceId;

		public FakeTextToSpeechClient(
			FakeTextToSpeechDispatcher dispatcher,
			bool throwOnPlay,
			bool throwOnDispose,
			Action? onAddText)
		{
			_dispatcher = dispatcher;
			_throwOnPlay = throwOnPlay;
			_throwOnDispose = throwOnDispose;
			_onAddText = onAddText;
		}

		public event Action<TizenTextToSpeechState>? StateChanged;

		public event Action<int>? UtteranceCompleted;

		public event Action<TizenTextToSpeechError>? ErrorOccurred;

		public ConcurrentQueue<string> WrongThreadOperations { get; } = [];

		public TaskCompletionSource Played { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Disposed { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource DisposeAttempted { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int LastUtteranceId { get; private set; }

		public int GetSupportedVoicesCalls { get; private set; }

		public bool StoppedBeforeDisposed { get; private set; }

		public bool DisposedInsideCallback { get; private set; }

		public int AddTextCalls { get; private set; }

		public int PlayCalls { get; private set; }

		public int LastSpeed { get; private set; }

		public List<string> Operations { get; } = [];

		public void Prepare()
		{
			AssertThread(nameof(Prepare));
			Operations.Add(nameof(Prepare));
			_prepared = true;
			StateChanged?.Invoke(TizenTextToSpeechState.Ready);
		}

		public IReadOnlyList<TizenTextToSpeechVoice> GetSupportedVoices()
		{
			AssertThread(nameof(GetSupportedVoices));
			GetSupportedVoicesCalls++;
			return [new("en_US", 0)];
		}

		public TizenTextToSpeechSpeedRange GetSpeedRange()
		{
			AssertThread(nameof(GetSpeedRange));
			if (_prepared)
				throw new InvalidOperationException("GetSpeedRange is only valid in Created.");
			Operations.Add(nameof(GetSpeedRange));
			return new(-10, 0, 20);
		}

		public int AddText(string text, string language, int voiceType, int speed)
		{
			AssertThread(nameof(AddText));
			if (!_prepared)
				throw new InvalidOperationException("AddText requires Ready.");
			Operations.Add(nameof(AddText));
			AddTextCalls++;
			LastSpeed = speed;
			LastUtteranceId = ++_nextUtteranceId;
			_onAddText?.Invoke();
			return LastUtteranceId;
		}

		public void Play()
		{
			AssertThread(nameof(Play));
			if (!_prepared)
				throw new InvalidOperationException("Play requires Ready.");
			Operations.Add(nameof(Play));
			PlayCalls++;
			if (_throwOnPlay)
				throw new InvalidOperationException("play failed");
			Played.TrySetResult();
		}

		public void Stop()
		{
			AssertThread(nameof(Stop));
			_stopped = true;
		}

		public void Dispose()
		{
			AssertThread(nameof(Dispose));
			DisposedInsideCallback = _insideErrorCallback;
			StoppedBeforeDisposed = _stopped;
			DisposeAttempted.TrySetResult();
			if (_throwOnDispose)
				throw new InvalidOperationException("dispose failed");
			Disposed.TrySetResult();
		}

		public Task CompleteAsync(int utteranceId) =>
			_dispatcher.InvokeAsync(() => UtteranceCompleted?.Invoke(utteranceId));

		public Task FailAsync(int utteranceId) =>
			_dispatcher.InvokeAsync(() =>
			{
				_insideErrorCallback = true;
				try
				{
					ErrorOccurred?.Invoke(new(utteranceId, 1, "native failure"));
				}
				finally
				{
					_insideErrorCallback = false;
				}
			});

		void AssertThread(string operation)
		{
			if (Thread.CurrentThread.ManagedThreadId != _dispatcher.ThreadId)
				WrongThreadOperations.Enqueue(operation);
		}
	}

	sealed class FakeSecureRepository : ITizenSecureRepository
	{
		readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
		readonly object _locker = new();

		public Exception? SaveException { get; init; }
		public HashSet<int> FailSaveCalls { get; } = [];
		public HashSet<string> FailRemoveAliases { get; } = new(StringComparer.Ordinal);
		public Exception? GetAliasesException { get; init; }
		public int? MaximumAliasLength { get; init; }
		public bool RejectWhitespaceAliases { get; init; }
		public int SuccessfulSaves { get; private set; }
		public int SaveCalls { get; private set; }

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
			{
				ValidateAlias(alias);
				return _values.TryGetValue(alias, out var value)
					? value
					: throw new InvalidOperationException("alias not found");
			}
		}

		public void Save(string alias, byte[] value)
		{
			lock (_locker)
			{
				ValidateAlias(alias);
				SaveCalls++;

				if (FailSaveCalls.Contains(SaveCalls))
					throw new InvalidOperationException($"save failed at call {SaveCalls}");
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
				ValidateAlias(alias);
				if (FailRemoveAliases.Contains(alias))
					throw new IOException($"remove failed for {alias}");

				if (!_values.Remove(alias))
					throw new InvalidOperationException("alias not found");
			}
		}

		public IEnumerable<string> GetAliases()
		{
			lock (_locker)
			{
				if (GetAliasesException is not null)
					throw GetAliasesException;
				return _values.Keys.ToArray();
			}
		}

		void ValidateAlias(string alias)
		{
			if (RejectWhitespaceAliases && alias.Any(char.IsWhiteSpace))
				throw new ArgumentException("alias contains whitespace", nameof(alias));
			if (MaximumAliasLength is { } maximum && alias.Length > maximum)
				throw new ArgumentException("alias exceeds native limit", nameof(alias));
		}
	}

	sealed class FakeSecureStorageTombstones : ITizenSecureStorageTombstones
	{
		readonly HashSet<string> _keys = new(StringComparer.Ordinal);
		readonly HashSet<string> _liveAfterAll = new(StringComparer.Ordinal);

		public bool ContainsAll { get; private set; }

		public bool Contains(string key) =>
			_keys.Contains(key) || (ContainsAll && !_liveAfterAll.Contains(key));

		public void Add(string key)
		{
			_liveAfterAll.Remove(key);
			_keys.Add(key);
		}

		public void Remove(string key)
		{
			_keys.Remove(key);
			if (ContainsAll)
				_liveAfterAll.Add(key);
		}

		public void AddAll()
		{
			ContainsAll = true;
			_liveAfterAll.Clear();
		}
	}

	sealed class FakePreferencesStore : ITizenPreferencesStore
	{
		readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
		readonly Dictionary<string, int> _setCounts = new(StringComparer.Ordinal);
		int _activeOperations;

		public object SyncRoot { get; } = new();
		public bool RequireSynchronization { get; init; }
		public bool RejectUnsupportedNativeTypes { get; init; }
		public int MaximumConcurrentOperations { get; private set; }

		public IEnumerable<string> Keys => Access(() => _values.Keys.ToArray());

		public object? this[string key]
		{
			get => _values[key];
			set => _values[key] = value;
		}

		public bool Contains(string key) =>
			Access(() => _values.ContainsKey(key));

		public void Remove(string key) =>
			Access(() => _values.Remove(key));

		public void Set<T>(string key, T value)
		{
			Access(() =>
			{
				if (RejectUnsupportedNativeTypes &&
					value is not (bool or int or double or string))
				{
					throw new ArgumentException(
						$"Tizen preferences cannot store '{typeof(T).FullName}'.");
				}

				_values[key] = value;
				_setCounts[key] = GetSetCount(key) + 1;
			});
		}

		public T Get<T>(string key) =>
			Access(() => (T)_values[key]!);

		public int GetSetCount(string key) =>
			_setCounts.TryGetValue(key, out var count) ? count : 0;

		T Access<T>(Func<T> action)
		{
			if (RequireSynchronization && !System.Threading.Monitor.IsEntered(SyncRoot))
				throw new InvalidOperationException("Preference access was not synchronized.");

			var active = Interlocked.Increment(ref _activeOperations);
			MaximumConcurrentOperations = Math.Max(MaximumConcurrentOperations, active);
			try
			{
				return action();
			}
			finally
			{
				Interlocked.Decrement(ref _activeOperations);
			}
		}

		void Access(Action action) =>
			Access(() =>
			{
				action();
				return true;
			});
	}

}
