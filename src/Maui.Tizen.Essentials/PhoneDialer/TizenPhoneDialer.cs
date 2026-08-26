using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IPhoneDialer"/>, backed by the <c>tel:</c> dial <c>AppControl</c>.
	/// </summary>
	public sealed class TizenPhoneDialer : IPhoneDialer
	{
		/// <inheritdoc/>
		public bool IsSupported =>
			TizenSystemInformation.GetFeatureInfo<bool>("contact");

		/// <inheritdoc/>
		public void Open(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				throw new ArgumentNullException(nameof(number));

			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupportedOnProfile($"{nameof(IPhoneDialer)}.{nameof(Open)}", TizenDeviceProfile.Mobile);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			TizenAppControl.SendLaunchRequest(new TizenAppControl
			{
				Operation = TizenAppControlOperations.Dial,
				Uri = "tel:" + number,
			});
		}
	}
}
