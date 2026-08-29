using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IBrowser"/>, backed by the <c>AppControl</c> view operation.
	/// </summary>
	/// <remarks>
	/// Tizen has no in-app browser control equivalent to Chrome Custom Tabs or <c>SFSafariViewController</c>,
	/// so <see cref="BrowserLaunchOptions"/> is only used to validate the request: every launch mode
	/// resolves to the system browser.
	/// </remarks>
	public sealed class TizenBrowser : IBrowser
	{
		/// <inheritdoc/>
		public Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
		{
			ArgumentNullException.ThrowIfNull(uri);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.View,
				Uri = uri.AbsoluteUri,
			};

			var hasMatches = TizenLauncher.HasHandler(appControl);

			if (hasMatches)
				TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(hasMatches);
		}
	}
}
