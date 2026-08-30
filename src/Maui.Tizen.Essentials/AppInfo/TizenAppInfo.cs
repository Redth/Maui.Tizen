using System;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;
using TizenApplication = Tizen.Applications.Application;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IAppInfo"/>.
	/// </summary>
	public sealed class TizenAppInfo : IAppInfo
	{
		/// <inheritdoc/>
		public string PackageName => TizenApplication.Current.ApplicationInfo.PackageId;

		/// <inheritdoc/>
		public string Name => TizenApplication.Current.ApplicationInfo.Label;

		/// <inheritdoc/>
		public Version Version => TizenPlatform.ParseVersion(VersionString);

		/// <inheritdoc/>
		public string VersionString => TizenPlatform.CurrentPackage.Version;

		/// <inheritdoc/>
		public string BuildString => Version.Build.ToString(CultureInfo.InvariantCulture);

		/// <inheritdoc/>
		public void ShowSettingsUI()
		{
			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			TizenAppControl.SendLaunchRequest(new TizenAppControl
			{
				Operation = TizenAppControlOperations.Setting,
			});
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Tizen exposes no cross-profile system theme query, so the theme is reported as
		/// <see cref="AppTheme.Unspecified"/> rather than guessing light or dark.
		/// </remarks>
		public AppTheme RequestedTheme => AppTheme.Unspecified;

		/// <inheritdoc/>
		public AppPackagingModel PackagingModel => AppPackagingModel.Packaged;

		/// <inheritdoc/>
		/// <remarks>Tizen does not expose a system-level RTL layout flag to applications.</remarks>
		public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
	}
}
