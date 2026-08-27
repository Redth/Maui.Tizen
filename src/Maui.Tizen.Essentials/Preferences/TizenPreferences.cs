using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Storage;
using TizenPreference = Tizen.Applications.Preference;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPreferences"/>, backed by <c>Tizen.Applications.Preference</c>.
	/// </summary>
	/// <remarks>
	/// Tizen has a single flat per-application preference store, so shared names are emulated with a
	/// key prefix. See <see cref="TizenStorageKeyEncoding"/> for why the components are escaped
	/// rather than simply concatenated.
	/// </remarks>
	public sealed class TizenPreferences : IPreferences
	{
		readonly ITizenPreferencesStore _store;

		/// <summary>
		/// Creates a preferences service backed by Tizen's application preference store.
		/// </summary>
		public TizenPreferences()
			: this(TizenPreferencesStore.Instance)
		{
		}

		internal TizenPreferences(ITizenPreferencesStore store)
		{
			_store = store;
		}

		/// <inheritdoc/>
		public bool ContainsKey(string key, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (_store.SyncRoot)
			{
				if (_store.Contains(GetFullKey(key, sharedName)))
					return true;
				if (LegacyFallbackSuppressed(key, sharedName))
					return false;

				return TizenStorageKeyEncoding.GetLegacyKeys(key, sharedName).Any(_store.Contains);
			}
		}

		/// <inheritdoc/>
		public void Remove(string key, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (_store.SyncRoot)
			{
				var fullKey = GetFullKey(key, sharedName);
				if (_store.Contains(fullKey))
					_store.Remove(fullKey);

				WriteTombstone(TizenStorageKeyEncoding.GetKeyTombstone(key, sharedName));
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Clearing a shared name removes only that store's entries. Because the shared name is
		/// escaped before being used as a prefix, clearing <c>a</c> cannot remove entries belonging
		/// to a different shared name such as <c>a~b</c>.
		/// </remarks>
		public void Clear(string? sharedName = null)
		{
			lock (_store.SyncRoot)
			{
				var prefix = TizenStorageKeyEncoding.GetSharedNamePrefix(sharedName ?? string.Empty);

				foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
					_store.Remove(key);

				WriteTombstone(TizenStorageKeyEncoding.GetStoreTombstone(sharedName));
			}
		}

		/// <inheritdoc/>
		public void Set<T>(string key, T value, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (_store.SyncRoot)
			{
				SetCore(GetFullKey(key, sharedName), value);
				if (value is null)
					WriteTombstone(TizenStorageKeyEncoding.GetKeyTombstone(key, sharedName));
				else
					RemoveTombstone(TizenStorageKeyEncoding.GetKeyTombstone(key, sharedName));
			}
		}

		/// <inheritdoc/>
		public T Get<T>(string key, T defaultValue, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (_store.SyncRoot)
			{
				var fullKey = GetFullKey(key, sharedName);

				if (_store.Contains(fullKey))
					return GetCore(fullKey, defaultValue);
				if (LegacyFallbackSuppressed(key, sharedName))
					return defaultValue;

				foreach (var legacyKey in TizenStorageKeyEncoding.GetLegacyKeys(key, sharedName))
				{
					if (!_store.Contains(legacyKey))
						continue;

					var value = GetCore(legacyKey, defaultValue);
					SetCore(fullKey, value);
					return value;
				}

				return defaultValue;
			}
		}

		void SetCore<T>(string fullKey, T value)
		{
			switch (value)
			{
				case null:
					if (_store.Contains(fullKey))
						_store.Remove(fullKey);
					break;
				case DateTime dateTime:
					_store.Set(fullKey, dateTime.ToBinary());
					break;
				case DateTimeOffset dateTimeOffset:
					_store.Set(fullKey, dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
					break;
				default:
					_store.Set(fullKey, value);
					break;
			}
		}

		T GetCore<T>(string fullKey, T defaultValue)
		{
			if (typeof(T) == typeof(DateTime))
				return (T)(object)DateTime.FromBinary(_store.Get<long>(fullKey));

			if (typeof(T) == typeof(DateTimeOffset))
			{
				var saved = _store.Get<string>(fullKey);
				return DateTimeOffset.TryParse(
					saved,
					CultureInfo.InvariantCulture,
					DateTimeStyles.RoundtripKind,
					out var parsed)
					? (T)(object)parsed
					: defaultValue;
			}

			return _store.Get<T>(fullKey);
		}

		bool LegacyFallbackSuppressed(string key, string? sharedName) =>
			_store.Contains(TizenStorageKeyEncoding.GetKeyTombstone(key, sharedName)) ||
			_store.Contains(TizenStorageKeyEncoding.GetStoreTombstone(sharedName));

		void WriteTombstone(string key)
		{
			if (!_store.Contains(key))
				_store.Set(key, true);
		}

		void RemoveTombstone(string key)
		{
			if (_store.Contains(key))
				_store.Remove(key);
		}

		internal static string GetFullKey(string key, string? sharedName) =>
			TizenStorageKeyEncoding.GetFullKey(key, sharedName);
	}

	internal interface ITizenPreferencesStore
	{
		object SyncRoot { get; }

		IEnumerable<string> Keys { get; }

		bool Contains(string key);

		void Remove(string key);

		void Set<T>(string key, T value);

		T Get<T>(string key);
	}

	sealed class TizenPreferencesStore : ITizenPreferencesStore
	{
		readonly object _syncRoot = new();

		public static TizenPreferencesStore Instance { get; } = new();

		TizenPreferencesStore()
		{
		}

		public object SyncRoot => _syncRoot;

		public IEnumerable<string> Keys
		{
			get
			{
				lock (SyncRoot)
					return TizenPreference.Keys.ToArray();
			}
		}

		public bool Contains(string key)
		{
			lock (SyncRoot)
				return TizenPreference.Contains(key);
		}

		public void Remove(string key)
		{
			lock (SyncRoot)
				TizenPreference.Remove(key);
		}

		public void Set<T>(string key, T value)
		{
			lock (SyncRoot)
				TizenPreference.Set(key, value);
		}

		public T Get<T>(string key)
		{
			lock (SyncRoot)
				return TizenPreference.Get<T>(key);
		}
	}
}
