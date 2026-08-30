using System;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// The Tizen device profile the application is currently running on.
	/// </summary>
	/// <remarks>
	/// Tizen ships several device profiles that expose materially different capability sets.
	/// This enumeration is used by <see cref="TizenEssentialsSupport"/> to classify which
	/// Essentials services are usable on the current device.
	/// </remarks>
	public enum TizenDeviceProfile
	{
		/// <summary>The profile could not be determined.</summary>
		Unknown,

		/// <summary>The mobile (handset) profile.</summary>
		Mobile,

		/// <summary>The wearable profile.</summary>
		Wearable,

		/// <summary>The TV profile.</summary>
		TV,

		/// <summary>The IoT / common headed profile.</summary>
		Common,
	}

	/// <summary>
	/// Reads <c>http://tizen.org/feature/*</c> and <c>http://tizen.org/system/*</c> information keys.
	/// </summary>
	/// <remarks>
	/// This is the standalone replacement for the internal <c>Microsoft.Maui.ApplicationModel.PlatformUtils</c>
	/// helper that dotnet/maui used for its in-box Tizen backend. All lookups are lazy so that simply
	/// loading this assembly (for example when inspecting DI registrations on a non-Tizen host) never
	/// calls into native Tizen libraries.
	/// </remarks>
	public static class TizenSystemInformation
	{
		/// <summary>
		/// Reads a <c>http://tizen.org/system/{item}</c> value as a <see cref="string"/>.
		/// </summary>
		/// <param name="item">The system information key, without the <c>http://tizen.org/system/</c> prefix.</param>
		/// <returns>The value, or <see langword="null"/> when the key is not present.</returns>
		public static string? GetSystemInfo(string item) =>
			GetSystemInfo<string>(item);

		/// <summary>
		/// Reads a <c>http://tizen.org/system/{item}</c> value.
		/// </summary>
		/// <typeparam name="T">The value type to read.</typeparam>
		/// <param name="item">The system information key, without the <c>http://tizen.org/system/</c> prefix.</param>
		/// <returns>The value, or <see langword="default"/> when the key is not present.</returns>
		public static T? GetSystemInfo<T>(string item) =>
			TryRead<T>($"http://tizen.org/system/{item}");

		/// <summary>
		/// Reads a <c>http://tizen.org/feature/{item}</c> value as a <see cref="string"/>.
		/// </summary>
		/// <param name="item">The feature key, without the <c>http://tizen.org/feature/</c> prefix.</param>
		/// <returns>The value, or <see langword="null"/> when the key is not present.</returns>
		public static string? GetFeatureInfo(string item) =>
			GetFeatureInfo<string>(item);

		/// <summary>
		/// Reads a <c>http://tizen.org/feature/{item}</c> value.
		/// </summary>
		/// <typeparam name="T">The value type to read.</typeparam>
		/// <param name="item">The feature key, without the <c>http://tizen.org/feature/</c> prefix.</param>
		/// <returns>The value, or <see langword="default"/> when the key is not present.</returns>
		public static T? GetFeatureInfo<T>(string item) =>
			TryRead<T>($"http://tizen.org/feature/{item}");

		// Tizen.System.Information.TryGetValue throws instead of returning false when the key is not
		// understood by the running platform, which for a capability probe means "not present".
		static T? TryRead<T>(string key)
		{
			try
			{
				global::Tizen.System.Information.TryGetValue<T>(key, out var value);
				return value;
			}
			catch (NotSupportedException)
			{
				return default;
			}
			catch (ArgumentException)
			{
				return default;
			}
		}

		/// <summary>
		/// Gets the Tizen device profile reported by <c>http://tizen.org/feature/profile</c>.
		/// </summary>
		public static TizenDeviceProfile CurrentProfile =>
			ParseProfile(GetFeatureInfo("profile"));

		internal static TizenDeviceProfile ParseProfile(string? profile)
		{
			if (string.IsNullOrEmpty(profile))
				return TizenDeviceProfile.Unknown;

			return char.ToUpperInvariant(profile![0]) switch
			{
				'M' => TizenDeviceProfile.Mobile,
				'W' => TizenDeviceProfile.Wearable,
				'T' => TizenDeviceProfile.TV,
				'C' => TizenDeviceProfile.Common,
				'I' => TizenDeviceProfile.Common,
				_ => TizenDeviceProfile.Unknown,
			};
		}
	}
}
