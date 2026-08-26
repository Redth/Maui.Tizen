using System;
using System.Globalization;
using System.Text.RegularExpressions;
using TizenApplication = Tizen.Applications.Application;
using TizenPackage = Tizen.Applications.Package;
using TizenPackageManager = Tizen.Applications.PackageManager;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen specific helpers for the Essentials platform backend.
	/// </summary>
	/// <remarks>
	/// dotnet/maui exposed these members on <c>Microsoft.Maui.ApplicationModel.Platform</c> under
	/// <c>#if TIZEN</c>. That type also exists in the neutral (non platform specific)
	/// <c>Microsoft.Maui.Essentials</c> assembly this package builds against, so the Tizen members
	/// are surfaced here instead of colliding with it.
	/// </remarks>
	public static class TizenPlatform
	{
		/// <summary>
		/// Gets a <see cref="TizenPackage"/> with information about the current application package.
		/// </summary>
		public static TizenPackage CurrentPackage
		{
			get
			{
				var packageId = TizenApplication.Current.ApplicationInfo.PackageId;
				return TizenPackageManager.GetPackage(packageId);
			}
		}
		/// <summary>
		/// Gets the label of the current application package, or the package id when no label is set.
		/// </summary>
		/// <remarks>Used as the Tizen log tag by this backend.</remarks>
		internal static string CurrentPackageLogTag
		{
			get
			{
				try
				{
					var package = CurrentPackage;
					return string.IsNullOrEmpty(package.Label) ? package.Id : package.Label;
				}
				catch
				{
					return "Maui.Tizen.Essentials";
				}
			}
		}

		/// <summary>
		/// Parses a Tizen package version string into a <see cref="Version"/>.
		/// </summary>
		/// <remarks>
		/// Replacement for the internal <c>Microsoft.Maui.Utils.ParseVersion</c> helper. Tizen package
		/// versions are not guaranteed to be well formed <see cref="Version"/> strings, so a leading
		/// numeric prefix is used when a full parse fails.
		/// </remarks>
		/// <param name="version">The version string to parse.</param>
		/// <returns>The parsed <see cref="Version"/>, or <c>0.0</c> when nothing could be parsed.</returns>
		public static Version ParseVersion(string? version)
		{
			if (Version.TryParse(version, out var number))
				return number;

			if (!string.IsNullOrWhiteSpace(version))
			{
				var match = Regex.Match(version!, @"\d+(\.\d+)*", RegexOptions.None, TimeSpan.FromSeconds(1));
				if (match.Success && Version.TryParse(match.Value, out number))
					return number;

				if (int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
					return new Version(major, 0);
			}

			return new Version(0, 0);
		}
	}
}
