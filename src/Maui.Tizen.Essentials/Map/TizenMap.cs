using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IMap"/>, backed by <c>geo:</c> <c>AppControl</c> launch requests.
	/// </summary>
	/// <remarks>
	/// Tizen's <c>geo:</c> handler accepts a coordinate or a free-form query only, so
	/// <see cref="MapLaunchOptions.NavigationMode"/> and the map launch name cannot be honoured.
	/// </remarks>
	public sealed class TizenMap : IMap
	{
		/// <inheritdoc/>
		public Task OpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
			Launch(CreateAppControl(latitude, longitude, options));

		/// <inheritdoc/>
		public Task OpenAsync(Placemark placemark, MapLaunchOptions options) =>
			Launch(CreateAppControl(placemark, options));

		/// <inheritdoc/>
		public Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
			TryLaunch(CreateAppControl(latitude, longitude, options));

		/// <inheritdoc/>
		public Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options) =>
			TryLaunch(CreateAppControl(placemark, options));

		internal static TizenAppControl CreateAppControl(double latitude, double longitude, MapLaunchOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			return new TizenAppControl
			{
				Operation = TizenAppControlOperations.View,
				Uri = FormattableString.Invariant($"geo:{latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}"),
			};
		}

		internal static TizenAppControl CreateAppControl(Placemark placemark, MapLaunchOptions options)
		{
			ArgumentNullException.ThrowIfNull(placemark);
			ArgumentNullException.ThrowIfNull(options);

			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();

			return new TizenAppControl
			{
				Operation = TizenAppControlOperations.Pick,
				Uri = $"geo:0,0?q={placemark.GetEscapedAddress()}",
			};
		}

		static Task Launch(TizenAppControl appControl)
		{
			TizenAppControl.SendLaunchRequest(appControl);
			return Task.CompletedTask;
		}

		static Task<bool> TryLaunch(TizenAppControl appControl)
		{
			var canLaunch = TizenAppControl.GetMatchedApplicationIds(appControl).Any();

			if (canLaunch)
				TizenAppControl.SendLaunchRequest(appControl);

			return Task.FromResult(canLaunch);
		}
	}
}
