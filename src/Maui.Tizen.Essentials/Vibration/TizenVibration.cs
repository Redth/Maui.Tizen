using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenVibrator = Tizen.System.Vibrator;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IVibration"/>, backed by <c>Tizen.System.Vibrator</c>.
	/// </summary>
	public sealed class TizenVibration : IVibration
	{
		const int DefaultIntensity = 100;

		/// <inheritdoc/>
		public bool IsSupported => TizenVibrator.NumberOfVibrators > 0;

		/// <inheritdoc/>
		public void Vibrate() => Vibrate(TimeSpan.FromMilliseconds(500));

		/// <inheritdoc/>
		public void Vibrate(TimeSpan duration)
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupportedOnProfile($"{nameof(IVibration)}.{nameof(Vibrate)}", TizenDeviceProfile.Mobile, TizenDeviceProfile.Wearable);

			TizenPermissions.EnsureDeclared<Permissions.Vibrate>();

			TizenVibrator.Vibrators.FirstOrDefault()?.Vibrate((int)duration.TotalMilliseconds, DefaultIntensity);
		}

		/// <inheritdoc/>
		public void Cancel()
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupportedOnProfile($"{nameof(IVibration)}.{nameof(Cancel)}", TizenDeviceProfile.Mobile, TizenDeviceProfile.Wearable);

			TizenPermissions.EnsureDeclared<Permissions.Vibrate>();

			TizenVibrator.Vibrators.FirstOrDefault()?.Stop();
		}
	}
}
