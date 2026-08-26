using System;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IDeviceInfo"/>, backed by Tizen system and feature information keys.
	/// </summary>
	public sealed class TizenDeviceInfo : IDeviceInfo
	{
		/// <inheritdoc/>
		public string Model => TizenSystemInformation.GetSystemInfo("model_name") ?? string.Empty;

		/// <inheritdoc/>
		public string Manufacturer => TizenSystemInformation.GetSystemInfo("manufacturer") ?? string.Empty;

		/// <inheritdoc/>
		public string Name => TizenSystemInformation.GetSystemInfo("device_name") ?? string.Empty;

		/// <inheritdoc/>
		public string VersionString => TizenSystemInformation.GetFeatureInfo("platform.version") ?? string.Empty;

		/// <inheritdoc/>
		public Version Version => TizenPlatform.ParseVersion(VersionString);

		/// <inheritdoc/>
		public DevicePlatform Platform => DevicePlatform.Tizen;

		/// <inheritdoc/>
		public DeviceIdiom Idiom => TizenSystemInformation.CurrentProfile switch
		{
			TizenDeviceProfile.Mobile => DeviceIdiom.Phone,
			TizenDeviceProfile.Wearable => DeviceIdiom.Watch,
			TizenDeviceProfile.TV => DeviceIdiom.TV,
			_ => DeviceIdiom.Unknown,
		};

		/// <inheritdoc/>
		public DeviceType DeviceType
		{
			get
			{
				var arch = TizenSystemInformation.GetFeatureInfo("platform.core.cpu.arch");
				var armv7 = TizenSystemInformation.GetFeatureInfo<bool>("platform.core.cpu.arch.armv7");
				var x86 = TizenSystemInformation.GetFeatureInfo<bool>("platform.core.cpu.arch.x86");

				return ClassifyDeviceType(arch, armv7, x86);
			}
		}

		internal static DeviceType ClassifyDeviceType(string? arch, bool armv7, bool x86)
		{
			if (string.Equals(arch, "armv7", StringComparison.Ordinal) && armv7 && !x86)
				return DeviceType.Physical;

			if (string.Equals(arch, "x86", StringComparison.Ordinal) && !armv7 && x86)
				return DeviceType.Virtual;

			return DeviceType.Unknown;
		}
	}
}
