using System;
using System.Linq;
using Microsoft.Maui.Storage;
using TizenPreference = Tizen.Applications.Preference;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPreferences"/>, backed by <c>Tizen.Applications.Preference</c>.
	/// </summary>
	/// <remarks>
	/// Tizen has a single per-application preference store, so shared names are emulated with a
	/// <c>{sharedName}~{key}</c> key prefix, matching the in-box dotnet/maui Tizen backend.
	/// </remarks>
	public sealed class TizenPreferences : IPreferences
	{
		const string Separator = "~";

		static readonly object Locker = new();

		/// <inheritdoc/>
		public bool ContainsKey(string key, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
				return TizenPreference.Contains(GetFullKey(key, sharedName));
		}

		/// <inheritdoc/>
		public void Remove(string key, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				var fullKey = GetFullKey(key, sharedName);
				if (TizenPreference.Contains(fullKey))
					TizenPreference.Remove(fullKey);
			}
		}

		/// <inheritdoc/>
		public void Clear(string? sharedName = null)
		{
			lock (Locker)
			{
				if (string.IsNullOrEmpty(sharedName))
				{
					TizenPreference.RemoveAll();
				}
				else
				{
					var prefix = $"{sharedName}{Separator}";
					foreach (var key in TizenPreference.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
						TizenPreference.Remove(key);
				}
			}
		}

		/// <inheritdoc/>
		public void Set<T>(string key, T value, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				var fullKey = GetFullKey(key, sharedName);

				switch (value)
				{
					case null:
						if (TizenPreference.Contains(fullKey))
							TizenPreference.Remove(fullKey);
						break;
					case DateTime dateTime:
						TizenPreference.Set(fullKey, dateTime.ToBinary());
						break;
					case DateTimeOffset dateTimeOffset:
						TizenPreference.Set(fullKey, dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
						break;
					default:
						TizenPreference.Set(fullKey, value);
						break;
				}
			}
		}

		/// <inheritdoc/>
		public T Get<T>(string key, T defaultValue, string? sharedName = null)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				var fullKey = GetFullKey(key, sharedName);

				if (!TizenPreference.Contains(fullKey))
					return defaultValue;

				if (typeof(T) == typeof(DateTime))
					return (T)(object)DateTime.FromBinary(TizenPreference.Get<long>(fullKey));

				if (typeof(T) == typeof(DateTimeOffset))
				{
					var saved = TizenPreference.Get<string>(fullKey);
					return DateTimeOffset.TryParse(
						saved,
						System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.RoundtripKind,
						out var parsed)
						? (T)(object)parsed
						: defaultValue;
				}

				return TizenPreference.Get<T>(fullKey);
			}
		}

		internal static string GetFullKey(string key, string? sharedName) =>
			string.IsNullOrEmpty(sharedName) ? key : $"{sharedName}{Separator}{key}";
	}
}
