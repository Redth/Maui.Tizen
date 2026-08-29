using System;
using System.Linq;
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
		/// <remarks>
		/// Asks Tizen whether any installed application actually handles the URI, rather than only
		/// checking that the string is well formed. The previous implementation returned
		/// <see langword="true"/> for any syntactically valid URI, so <see cref="TryOpenAsync"/>
		/// would go on to call <see cref="OpenAsync(Uri)"/> and throw when nothing could handle it -
		/// which defeats the point of a Try method.
		/// </remarks>
		public Task<bool> CanOpenAsync(Uri uri)
		{
			ArgumentNullException.ThrowIfNull(uri);

			if (!uri.IsAbsoluteUri || !uri.IsWellFormedOriginalString())
				return Task.FromResult(false);

			return Task.FromResult(HasHandler(CreateAppControl(uri)));
		}

		/// <inheritdoc/>
		public Task<bool> OpenAsync(Uri uri)
		{
			ArgumentNullException.ThrowIfNull(uri);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			TizenAppControl.SendLaunchRequest(CreateAppControl(uri));

			return Task.FromResult(true);
		}

		/// <inheritdoc/>
		public Task<bool> OpenAsync(OpenFileRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = CreateOpenFileAppControl(request);

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(true);
		}

		internal static TizenAppControl CreateOpenFileAppControl(OpenFileRequest request)
		{
			var payload = CreateOpenFilePayload(request);

			return new TizenAppControl
			{
				Operation = payload.Operation,
				Mime = payload.Mime,
				Uri = payload.Uri,
			};
		}

		internal static TizenOpenFilePayload CreateOpenFilePayload(OpenFileRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			if (string.IsNullOrEmpty(request.File?.FullPath))
				throw new ArgumentException("An OpenFileRequest requires a file with a full path.", nameof(request));

			return new(
				TizenAppControlOperations.View,
				TizenShare.ResolveMime(request.File) ?? TizenFileMimeTypes.All,
				"file://" + request.File.FullPath);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Returns <see langword="false"/> when nothing on the device handles the URI, rather than
		/// letting the launch attempt throw.
		/// </remarks>
		public Task<bool> TryOpenAsync(Uri uri)
		{
			ArgumentNullException.ThrowIfNull(uri);

			if (!uri.IsAbsoluteUri || !uri.IsWellFormedOriginalString())
				return Task.FromResult(false);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = CreateAppControl(uri);

			if (!HasHandler(appControl))
				return Task.FromResult(false);

			TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(true);
		}

		static TizenAppControl CreateAppControl(Uri uri) =>
			new()
			{
				Operation = GetOperation(uri),
				Uri = uri.AbsoluteUri,
			};

		internal static bool HasHandler(TizenAppControl appControl) =>
			HasHandler(() => TizenAppControl.GetMatchedApplicationIds(appControl));

		internal static bool HasHandler(Func<System.Collections.Generic.IEnumerable<string>?> getMatches)
		{
			try
			{
				return getMatches()?.Any() == true;
			}
			catch (Exception)
			{
				// Tizen throws rather than returning an empty set when nothing matches the control.
				// For a capability probe that is the same answer as "no handler".
				return false;
			}
		}

		internal static string GetOperation(Uri uri)
		{
			var absoluteUri = uri.AbsoluteUri;

			if (absoluteUri.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.View;
			if (absoluteUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.View;
			if (absoluteUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Compose;
			if (absoluteUri.StartsWith("sms:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Compose;
			if (absoluteUri.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
				return TizenAppControlOperations.Dial;

			return TizenAppControlOperations.View;
		}

		internal sealed record TizenOpenFilePayload(string Operation, string Mime, string Uri);
	}
}
