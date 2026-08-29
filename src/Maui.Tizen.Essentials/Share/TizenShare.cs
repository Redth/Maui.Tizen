using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
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

			AddPaths(
				payload,
				(key, value) => appControl.ExtraData.Add(key, value),
				(key, values) => appControl.ExtraData.Add(key, values));

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
				ResolveMime(validFiles),
				paths);
		}

		internal static void AddPaths(
			TizenFilePayload payload,
			Action<string, string> addSingle,
			Action<string, IEnumerable<string>> addMultiple)
		{
			if (payload.Paths.Count == 1)
				addSingle(TizenAppControlData.Path, payload.Paths[0]);
			else if (payload.Paths.Count > 1)
				addMultiple(TizenAppControlData.Path, payload.Paths);
		}

		internal static string ResolveMime(IEnumerable<FileBase> files)
		{
			var types = files
				.Select(ResolveMime)
				.Where(type => !string.IsNullOrWhiteSpace(type))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return types.Length == 1 ? types[0]! : TizenFileMimeTypes.All;
		}

		internal static string? ResolveMime(FileBase file)
		{
			try
			{
				var contentType = file.ContentType;
				if (!string.IsNullOrWhiteSpace(contentType))
					return contentType;
			}
			catch (NotImplementedException)
			{
			}

			var extension = Path.GetExtension(file.FullPath);
			if (string.IsNullOrEmpty(extension))
				return null;

			try
			{
				var contentType = TizenFileSystem.GetContentType(extension);
				if (!string.IsNullOrWhiteSpace(contentType))
					return contentType;
			}
			catch (ArgumentException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (NotSupportedException)
			{
			}

			return extension.ToLowerInvariant() switch
			{
				".png" => TizenFileMimeTypes.ImagePng,
				".jpg" or ".jpeg" => TizenFileMimeTypes.ImageJpg,
				".pdf" => TizenFileMimeTypes.Pdf,
				".gif" => "image/gif",
				".webp" => "image/webp",
				".mp4" => "video/mp4",
				".mp3" => "audio/mpeg",
				".txt" => "text/plain",
				".html" or ".htm" => "text/html",
				".json" => "application/json",
				".xml" => "application/xml",
				".zip" => "application/zip",
				_ => null,
			};
		}
	}

	internal sealed record TizenFilePayload(
		string Operation,
		string Mime,
		IReadOnlyList<string> Paths);
}
