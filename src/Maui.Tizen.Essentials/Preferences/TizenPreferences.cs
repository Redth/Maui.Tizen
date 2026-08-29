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
		const string LongPrefix = "maui-pref:long:v1:";
		const string DateTimePrefix = "maui-pref:datetime:v1:";
		const string DateTimeOffsetPrefix = "maui-pref:datetimeoffset:v1:";

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
					_store.Set(
						fullKey,
						DateTimePrefix + dateTime.ToBinary().ToString(CultureInfo.InvariantCulture));
					break;
				case DateTimeOffset dateTimeOffset:
					_store.Set(
						fullKey,
						DateTimeOffsetPrefix + dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
					break;
				case long longValue:
					_store.Set(fullKey, LongPrefix + longValue.ToString(CultureInfo.InvariantCulture));
					break;
				case float floatValue:
					_store.Set(fullKey, (double)floatValue);
					break;
				case bool or int or double or string:
					_store.Set(fullKey, value);
					break;
				default:
					throw new ArgumentException(
						$"Preferences does not support values of type '{typeof(T).FullName}'.",
						nameof(value));
			}
		}

		T GetCore<T>(string fullKey, T defaultValue)
		{
			if (typeof(T) == typeof(DateTime))
			{
				if (TryGetString(fullKey, out var saved) &&
					saved.StartsWith(DateTimePrefix, StringComparison.Ordinal) &&
					long.TryParse(
						saved.AsSpan(DateTimePrefix.Length),
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out var binary))
				{
					return (T)(object)DateTime.FromBinary(binary);
				}

				// First standalone builds attempted to use a native-unsupported Int64 directly.
				// Keep the read path so test/development stores can migrate that representation.
				try
				{
					var legacy = DateTime.FromBinary(_store.Get<long>(fullKey));
					SetCore(fullKey, legacy);
					return (T)(object)legacy;
				}
				catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
				{
					return defaultValue;
				}
			}

			if (typeof(T) == typeof(DateTimeOffset))
			{
				if (!TryGetString(fullKey, out var saved))
					return defaultValue;

				var versioned = saved.StartsWith(DateTimeOffsetPrefix, StringComparison.Ordinal);
				if (versioned)
					saved = saved[DateTimeOffsetPrefix.Length..];

				if (!DateTimeOffset.TryParse(
					saved,
					CultureInfo.InvariantCulture,
					DateTimeStyles.RoundtripKind,
					out var parsed))
				{
					return defaultValue;
				}

				if (!versioned)
					SetCore(fullKey, parsed);
				return (T)(object)parsed;
			}

			if (typeof(T) == typeof(long))
			{
				if (TryGetString(fullKey, out var saved) &&
					saved.StartsWith(LongPrefix, StringComparison.Ordinal) &&
					long.TryParse(
						saved.AsSpan(LongPrefix.Length),
						NumberStyles.Integer,
						CultureInfo.InvariantCulture,
						out var value))
				{
					return (T)(object)value;
				}

				try
				{
					var legacy = _store.Get<long>(fullKey);
					SetCore(fullKey, legacy);
					return (T)(object)legacy;
				}
				catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
				{
					return defaultValue;
				}
			}

			if (typeof(T) == typeof(float))
			{
				double saved;
				try
				{
					saved = _store.Get<double>(fullKey);
				}
				catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
				{
					// Compatibility with the pre-fix in-memory/development representation.
					try
					{
						var legacy = _store.Get<float>(fullKey);
						SetCore(fullKey, legacy);
						return (T)(object)legacy;
					}
					catch (Exception fallback) when (fallback is InvalidCastException or ArgumentException)
					{
						return defaultValue;
					}
				}

				return (T)(object)ToSingleExact(saved);
			}

			return _store.Get<T>(fullKey);
		}

		bool TryGetString(string key, out string value)
		{
			try
			{
				value = _store.Get<string>(key);
				return true;
			}
			catch (Exception exception) when (exception is InvalidCastException or ArgumentException)
			{
				value = string.Empty;
				return false;
			}
		}

		internal static float ToSingleExact(double value)
		{
			if (double.IsNaN(value))
				return float.NaN;
			if (double.IsPositiveInfinity(value))
				return float.PositiveInfinity;
			if (double.IsNegativeInfinity(value))
				return float.NegativeInfinity;
			if (value > float.MaxValue || value < float.MinValue)
				throw new OverflowException($"Stored preference value '{value}' is outside the Single range.");

			var converted = (float)value;
			if ((double)converted != value)
			{
				throw new InvalidOperationException(
					$"Stored preference value '{value.ToString("R", CultureInfo.InvariantCulture)}' " +
					"cannot be represented exactly as Single.");
			}

			return converted;
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
