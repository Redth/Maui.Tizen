using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlLaunchMode = Tizen.Applications.AppControlLaunchMode;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;
using TizenAppControlReplyResult = Tizen.Applications.AppControlReplyResult;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IFilePicker"/>, backed by the <c>pick</c> <c>AppControl</c> operation.
	/// </summary>
	public sealed class TizenFilePicker : IFilePicker
	{
		/// <inheritdoc/>
		public async Task<FileResult?> PickAsync(PickOptions? options = null) =>
			(await PlatformPickAsync(options, allowMultiple: false).ConfigureAwait(false)).FirstOrDefault();

		/// <inheritdoc/>
		public async Task<IEnumerable<FileResult>?> PickMultipleAsync(PickOptions? options = null) =>
			await PlatformPickAsync(options, allowMultiple: true).ConfigureAwait(false);

		async Task<IReadOnlyList<FileResult>> PlatformPickAsync(PickOptions? options, bool allowMultiple)
		{
			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();
			await TizenPermissions.EnsureGrantedAsync<Permissions.StorageRead>().ConfigureAwait(false);

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.Pick,
				LaunchMode = TizenAppControlLaunchMode.Single,
				Mime = ResolveMimeType(options),
			};
			appControl.ExtraData.Add(TizenAppControlData.SectionMode, allowMultiple ? "multiple" : "single");

			return await TizenAppControlReply.RunAsync<TizenAppControl, TizenAppControlReplyResult, IReadOnlyList<FileResult>>(
				callback => TizenAppControl.SendLaunchRequest(
					appControl,
					TizenAppControlReply.NativeTimeoutMilliseconds,
					(_, reply, result) => callback(reply, result)),
				static (reply, result) =>
				{
					if (result != TizenAppControlReplyResult.Succeeded ||
						!reply.ExtraData.TryGet(
							TizenAppControlData.Selected,
							out IEnumerable<string> selected) ||
						selected is null)
					{
						return [];
					}

					return selected
						.Where(static path => !string.IsNullOrWhiteSpace(path))
						.Select(static path => new FileResult(path))
						.ToArray();
				}).ConfigureAwait(false);
		}

		static string ResolveMimeType(PickOptions? options)
		{
			if (options?.FileTypes?.Value is not { } values)
				return TizenFileMimeTypes.All;

			// Tizen's pick operation accepts exactly one MIME filter.
			return values.FirstOrDefault() ?? TizenFileMimeTypes.All;
		}

		/// <summary>
		/// Creates a <see cref="FilePickerFileType"/> for the supplied Tizen MIME types.
		/// </summary>
		/// <param name="mimeTypes">The MIME types to accept.</param>
		/// <returns>A Tizen-scoped <see cref="FilePickerFileType"/>.</returns>
		/// <remarks>
		/// The built-in <see cref="FilePickerFileType.Images"/>-style helpers resolve their Tizen entry
		/// from the in-box dotnet/maui Tizen backend, which is not present in the neutral
		/// <c>Microsoft.Maui.Essentials</c> assembly. Use these helpers instead when targeting Tizen.
		/// </remarks>
		public static FilePickerFileType CreateFileType(params string[] mimeTypes) =>
			new(new Dictionary<DevicePlatform, IEnumerable<string>>
			{
				[DevicePlatform.Tizen] = mimeTypes,
			});

		/// <summary>Gets a Tizen <see cref="FilePickerFileType"/> that matches all image files.</summary>
		public static FilePickerFileType ImageFileType { get; } = CreateFileType(TizenFileMimeTypes.ImageAll);

		/// <summary>Gets a Tizen <see cref="FilePickerFileType"/> that matches PNG files.</summary>
		public static FilePickerFileType PngFileType { get; } = CreateFileType(TizenFileMimeTypes.ImagePng);

		/// <summary>Gets a Tizen <see cref="FilePickerFileType"/> that matches JPEG files.</summary>
		public static FilePickerFileType JpegFileType { get; } = CreateFileType(TizenFileMimeTypes.ImageJpg);

		/// <summary>Gets a Tizen <see cref="FilePickerFileType"/> that matches all video files.</summary>
		public static FilePickerFileType VideoFileType { get; } = CreateFileType(TizenFileMimeTypes.VideoAll);

		/// <summary>Gets a Tizen <see cref="FilePickerFileType"/> that matches PDF files.</summary>
		public static FilePickerFileType PdfFileType { get; } = CreateFileType(TizenFileMimeTypes.Pdf);
	}
}
