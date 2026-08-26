using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IEmail"/>, backed by the <c>mailto:</c> compose <c>AppControl</c>.
	/// </summary>
	/// <remarks>
	/// Tizen's compose operation has no attachment slot, so <see cref="EmailMessage.Attachments"/>
	/// is rejected rather than silently dropped, and HTML bodies are sent as plain text.
	/// </remarks>
	public sealed class TizenEmail : IEmail
	{
		/// <inheritdoc/>
		public bool IsComposeSupported =>
			TizenSystemInformation.GetFeatureInfo<bool>("email");

		/// <inheritdoc/>
		public Task ComposeAsync(EmailMessage? message)
		{
			if (!IsComposeSupported)
				throw TizenEssentialsSupport.NotSupportedOnProfile($"{nameof(IEmail)}.{nameof(ComposeAsync)}", TizenDeviceProfile.Mobile);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.Compose,
				Uri = "mailto:",
			};

			if (message is not null)
			{
				if (message.Attachments?.Count > 0)
				{
					throw TizenEssentialsSupport.NotSupported(
						$"{nameof(IEmail)}.{nameof(ComposeAsync)} with attachments",
						"The Tizen 'compose' AppControl operation accepts no attachment payload.");
				}

				if (message.Bcc?.Count > 0)
					appControl.ExtraData.Add(TizenAppControlData.Bcc, message.Bcc);
				if (!string.IsNullOrEmpty(message.Body))
					appControl.ExtraData.Add(TizenAppControlData.Text, message.Body);
				if (message.Cc?.Count > 0)
					appControl.ExtraData.Add(TizenAppControlData.Cc, message.Cc);
				if (!string.IsNullOrEmpty(message.Subject))
					appControl.ExtraData.Add(TizenAppControlData.Subject, message.Subject);
				if (message.To?.Count > 0)
					appControl.ExtraData.Add(TizenAppControlData.To, message.To);
			}

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.CompletedTask;
		}

		/// <summary>
		/// Composes an email with the supplied subject, body and recipients.
		/// </summary>
		/// <param name="subject">The email subject.</param>
		/// <param name="body">The email body.</param>
		/// <param name="to">The recipients.</param>
		/// <returns>A task that completes once the compose request has been sent.</returns>
		public Task ComposeAsync(string subject, string body, params string[] to) =>
			ComposeAsync(new EmailMessage
			{
				Subject = subject,
				Body = body,
				To = to?.ToList() ?? new System.Collections.Generic.List<string>(),
			});

		/// <summary>
		/// Opens the platform email composer with an empty message.
		/// </summary>
		/// <returns>A task that completes once the compose request has been sent.</returns>
		public Task ComposeAsync() => ComposeAsync(null);
	}
}
