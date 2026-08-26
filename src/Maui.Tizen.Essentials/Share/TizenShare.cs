using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IShare"/>, backed by the <c>share</c> and
	/// <c>share_text</c> <c>AppControl</c> operations.
	/// </summary>
	public sealed class TizenShare : IShare
	{
		/// <inheritdoc/>
		public Task RequestAsync(ShareTextRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.ShareText,
			};

			if (!string.IsNullOrEmpty(request.Text))
				appControl.ExtraData.Add(TizenAppControlData.Text, request.Text);
			if (!string.IsNullOrEmpty(request.Uri))
				appControl.ExtraData.Add(TizenAppControlData.Url, request.Uri);
			if (!string.IsNullOrEmpty(request.Subject))
				appControl.ExtraData.Add(TizenAppControlData.Subject, request.Subject);
			if (!string.IsNullOrEmpty(request.Title))
				appControl.ExtraData.Add(TizenAppControlData.Title, request.Title);

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		public Task RequestAsync(ShareFileRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			return RequestAsync((ShareMultipleFilesRequest)request);
		}

		/// <inheritdoc/>
		public Task RequestAsync(ShareMultipleFilesRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.Share,
			};

			if (!string.IsNullOrEmpty(request.Title))
				appControl.ExtraData.Add(TizenAppControlData.Title, request.Title);

			foreach (var file in request.Files ?? Enumerable.Empty<ShareFile>())
			{
				if (!string.IsNullOrEmpty(file?.FullPath))
					appControl.ExtraData.Add(TizenAppControlData.Path, file.FullPath);
			}

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.CompletedTask;
		}
	}
}
