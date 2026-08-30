using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenLed = Tizen.System.Led;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IFlashlight"/>, backed by <c>Tizen.System.Led</c>.
	/// </summary>
	public sealed class TizenFlashlight : IFlashlight
	{
		static bool IsSupported =>
			TizenSystemInformation.GetFeatureInfo<bool>("camera.back.flash");

		/// <inheritdoc/>
		public Task<bool> IsSupportedAsync() => Task.FromResult(IsSupported);

		/// <inheritdoc/>
		public Task TurnOnAsync() => SwitchAsync(true);

		/// <inheritdoc/>
		public Task TurnOffAsync() => SwitchAsync(false);

		static Task SwitchAsync(bool on)
		{
			TizenPermissions.EnsureDeclared<Permissions.Flashlight>();

			return Task.Run(() =>
			{
				if (!IsSupported)
				{
					throw TizenEssentialsSupport.NotSupportedOnProfile(
						$"{nameof(IFlashlight)}.{(on ? nameof(TurnOnAsync) : nameof(TurnOffAsync))}",
						TizenDeviceProfile.Mobile);
				}

				TizenLed.Brightness = on ? TizenLed.MaxBrightness : 0;
			});
		}
	}
}
