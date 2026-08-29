using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ISms"/>, backed by the <c>sms:</c> compose <c>AppControl</c>.
	/// </summary>
	public sealed class TizenSms : ISms
	{
		/// <inheritdoc/>
		public bool IsComposeSupported =>
			TizenSystemInformation.GetFeatureInfo<bool>("network.telephony.sms");

		/// <inheritdoc/>
		public Task ComposeAsync(SmsMessage? message)
		{
			if (!IsComposeSupported)
				throw TizenEssentialsSupport.NotSupportedOnProfile($"{nameof(ISms)}.{nameof(ComposeAsync)}", TizenDeviceProfile.Mobile);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.Compose,
				Uri = "sms:",
			};

			if (message is not null)
			{
				if (!string.IsNullOrEmpty(message.Body))
					appControl.ExtraData.Add(TizenAppControlData.Text, message.Body);

				if (message.Recipients?.Count > 0)
					appControl.Uri += string.Join(" ", message.Recipients);
			}

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.CompletedTask;
		}
	}
}
