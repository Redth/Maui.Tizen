using System;
using System.Collections.Generic;
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

			var payload = CreateFilePayload(request.Files);
			var appControl = new TizenAppControl
			{
				Operation = payload.Operation,
				Mime = payload.Mime,
			};

			if (!string.IsNullOrEmpty(request.Title))
				appControl.ExtraData.Add(TizenAppControlData.Title, request.Title);

			if (payload.Paths.Count > 0)
				appControl.ExtraData.Add(TizenAppControlData.Path, payload.Paths);

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.CompletedTask;
		}

		internal static TizenFilePayload CreateFilePayload(IEnumerable<ShareFile>? files)
		{
			var validFiles = (files ?? Enumerable.Empty<ShareFile>())
				.Where(file => !string.IsNullOrEmpty(file?.FullPath))
				.ToArray();
			var paths = validFiles.Select(file => file.FullPath).ToArray();

			return new(
				paths.Length > 1 ? TizenAppControlOperations.MultiShare : TizenAppControlOperations.Share,
				ResolveMime(validFiles.Select(file => file.ContentType)),
				paths);
		}

		internal static string ResolveMime(IEnumerable<string?> contentTypes)
		{
			var types = contentTypes
				.Where(type => !string.IsNullOrWhiteSpace(type))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return types.Length == 1 ? types[0]! : TizenFileMimeTypes.All;
		}
	}

	internal sealed record TizenFilePayload(
		string Operation,
		string Mime,
		IReadOnlyList<string> Paths);
}
