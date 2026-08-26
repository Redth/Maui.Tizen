using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ILauncher"/>, backed by <c>AppControl</c> launch requests.
	/// </summary>
	public sealed class TizenLauncher : ILauncher
	{
		/// <inheritdoc/>
		public Task<bool> CanOpenAsync(Uri uri)
		{
			ArgumentNullException.ThrowIfNull(uri);

			return Task.FromResult(uri.IsWellFormedOriginalString());
		}

		/// <inheritdoc/>
		public Task<bool> OpenAsync(Uri uri)
		{
			ArgumentNullException.ThrowIfNull(uri);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = GetOperation(uri),
				Uri = uri.AbsoluteUri,
			};

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(true);
		}

		/// <inheritdoc/>
		public Task<bool> OpenAsync(OpenFileRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			if (string.IsNullOrEmpty(request.File?.FullPath))
				throw new ArgumentException("An OpenFileRequest requires a file with a full path.", nameof(request));

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.View,
				Mime = TizenFileMimeTypes.All,
				Uri = "file://" + request.File.FullPath,
			};

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(true);
		}

		/// <inheritdoc/>
		public async Task<bool> TryOpenAsync(Uri uri)
		{
			var canOpen = await CanOpenAsync(uri).ConfigureAwait(false);

			if (canOpen)
				await OpenAsync(uri).ConfigureAwait(false);

			return canOpen;
		}

		static string GetOperation(Uri uri)
		{
			var absoluteUri = uri.AbsoluteUri;

			if (absoluteUri.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Pick;
			if (absoluteUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.View;
			if (absoluteUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Compose;
			if (absoluteUri.StartsWith("sms:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Compose;
			if (absoluteUri.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Dial;

			return TizenAppControlOperations.ShareText;
		}
	}
}
