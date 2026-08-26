using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlLaunchMode = Tizen.Applications.AppControlLaunchMode;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;
using TizenAppControlReplyResult = Tizen.Applications.AppControlReplyResult;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IMediaPicker"/>.
	/// </summary>
	/// <remarks>
	/// Picking delegates to the Tizen file picker with an image or video MIME filter; capturing uses
	/// the <c>image_capture</c> and <c>video_capture</c> <c>AppControl</c> operations.
	/// </remarks>
	public sealed class TizenMediaPicker : IMediaPicker
	{
		readonly TizenFilePicker _filePicker = new();

		/// <inheritdoc/>
		public bool IsCaptureSupported =>
			TizenSystemInformation.GetFeatureInfo<bool>("camera") ||
			TizenSystemInformation.GetFeatureInfo<bool>("camera.back") ||
			TizenSystemInformation.GetFeatureInfo<bool>("camera.front");

		/// <inheritdoc/>
		[Obsolete("Switch to PickPhotosAsync which also allows multiple selections.")]
		public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) =>
			_filePicker.PickAsync(CreatePickOptions(options, TizenFilePicker.ImageFileType));

		/// <inheritdoc/>
		public async Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) =>
			(await _filePicker.PickMultipleAsync(CreatePickOptions(options, TizenFilePicker.ImageFileType)).ConfigureAwait(false))
				?.ToList() ?? new List<FileResult>();

		/// <inheritdoc/>
		[Obsolete("Switch to PickVideosAsync which also allows multiple selections.")]
		public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) =>
			_filePicker.PickAsync(CreatePickOptions(options, TizenFilePicker.VideoFileType));

		/// <inheritdoc/>
		public async Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) =>
			(await _filePicker.PickMultipleAsync(CreatePickOptions(options, TizenFilePicker.VideoFileType)).ConfigureAwait(false))
				?.ToList() ?? new List<FileResult>();

		/// <inheritdoc/>
		public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
			CaptureAsync(photo: true);

		/// <inheritdoc/>
		public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
			CaptureAsync(photo: false);

		static PickOptions CreatePickOptions(MediaPickerOptions? options, FilePickerFileType fileType) =>
			new()
			{
				PickerTitle = options?.Title,
				FileTypes = fileType,
			};

		async Task<FileResult?> CaptureAsync(bool photo)
		{
			if (!IsCaptureSupported)
			{
				throw TizenEssentialsSupport.NotSupportedOnProfile(
					$"{nameof(IMediaPicker)}.{(photo ? nameof(CapturePhotoAsync) : nameof(CaptureVideoAsync))}",
					TizenDeviceProfile.Mobile);
			}

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();
			await TizenPermissions.EnsureGrantedAsync<Permissions.StorageRead>().ConfigureAwait(false);

			var tcs = new TaskCompletionSource<FileResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

			var appControl = new TizenAppControl
			{
				Operation = photo ? TizenAppControlOperations.ImageCapture : TizenAppControlOperations.VideoCapture,
				LaunchMode = TizenAppControlLaunchMode.Group,
			};

			var appId = TizenAppControl.GetMatchedApplicationIds(appControl)?.FirstOrDefault();
			if (!string.IsNullOrEmpty(appId))
				appControl.ApplicationId = appId;

			TizenAppControl.SendLaunchRequest(appControl, (request, reply, result) =>
			{
				if (result == TizenAppControlReplyResult.Succeeded &&
					reply.ExtraData.TryGet(TizenAppControlData.Selected, out IEnumerable<string> selected))
				{
					var file = selected?.FirstOrDefault();
					tcs.TrySetResult(string.IsNullOrEmpty(file) ? null : new FileResult(file));
				}
				else
				{
					tcs.TrySetCanceled();
				}
			});

			return await tcs.Task.ConfigureAwait(false);
		}
	}
}
