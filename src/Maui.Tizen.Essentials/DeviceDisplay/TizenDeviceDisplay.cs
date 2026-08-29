using System;
using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenApplication = Tizen.Applications.Application;
using TizenCoreApplication = Tizen.Applications.CoreApplication;
using TizenDeviceOrientation = Tizen.Applications.DeviceOrientation;
using TizenDeviceOrientationEventArgs = Tizen.Applications.DeviceOrientationEventArgs;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IDeviceDisplay"/>.
	/// </summary>
	/// <remarks>
	/// Screen metrics come from the <c>screen.width</c>, <c>screen.height</c> and <c>screen.dpi</c>
	/// feature keys; rotation is tracked through <c>CoreApplication.DeviceOrientationChanged</c>.
	/// Keep-screen-on is implemented with <c>device_power_request_lock</c>.
	/// </remarks>
	public sealed partial class TizenDeviceDisplay : IDeviceDisplay, IDisposable
	{
		// Matches Microsoft.Maui.Devices.DeviceDisplay.BaseLogicalDpi for Android/Tizen, which is internal.
		internal const float BaseLogicalDpi = 160.0f;

		[LibraryImport("libcapi-system-device.so.0", EntryPoint = "device_power_request_lock")]
		private static partial int RequestPowerLock(int type, int timeout);

		[LibraryImport("libcapi-system-device.so.0", EntryPoint = "device_power_release_lock")]
		private static partial int ReleasePowerLock(int type);

		const int PowerLockDisplay = 1;

		readonly TizenEventSubscriptionCoordinator<DisplayInfoChangedEventArgs> _events;
		bool _keepScreenOn;
		DisplayRotation _displayRotation = DisplayRotation.Rotation0;
		DisplayOrientation _displayOrientation = DisplayOrientation.Unknown;

		static TizenCoreApplication? CoreApplication => TizenApplication.Current as TizenCoreApplication;

		static int DisplayWidth => TizenSystemInformation.GetFeatureInfo<int>("screen.width");

		static int DisplayHeight => TizenSystemInformation.GetFeatureInfo<int>("screen.height");

		static int DisplayDpi =>
			TizenSystemInformation.CurrentProfile == TizenDeviceProfile.TV
				? 72
				: TizenSystemInformation.GetFeatureInfo<int>("screen.dpi");

		/// <summary>Creates the Tizen display service.</summary>
		public TizenDeviceDisplay()
		{
			_events = new(this, StartListeners, StopListeners);
		}

		/// <inheritdoc/>
		public bool KeepScreenOn
		{
			get => _keepScreenOn;
			set
			{
				var result = value
					? RequestPowerLock(PowerLockDisplay, 0)
					: ReleasePowerLock(PowerLockDisplay);

				if (result != 0)
				{
					throw new InvalidOperationException(
						$"Unable to {(value ? "acquire" : "release")} the Tizen display power lock (error {result}). " +
						"Declare 'http://tizen.org/privilege/display' in tizen-manifest.xml.");
				}

				_keepScreenOn = value;
			}
		}

		/// <inheritdoc/>
		public DisplayInfo MainDisplayInfo
		{
			get
			{
				var width = DisplayWidth;
				var height = DisplayHeight;

				return new DisplayInfo(
					width: width,
					height: height,
					density: DisplayDpi / BaseLogicalDpi,
					orientation: _displayOrientation == DisplayOrientation.Unknown
						? GetNaturalOrientation(width, height)
						: _displayOrientation,
					rotation: _displayRotation);
			}
		}

		/// <inheritdoc/>
		public event EventHandler<DisplayInfoChangedEventArgs> MainDisplayInfoChanged
		{
			add => _events.Add(value);
			remove => _events.Remove(value);
		}

		void StartListeners()
		{
			if (CoreApplication is not { } app)
				return;

			app.DeviceOrientationChanged += OnRotationChanged;
		}

		void StopListeners()
		{
			if (CoreApplication is not { } app)
				return;

			app.DeviceOrientationChanged -= OnRotationChanged;
		}

		static DisplayOrientation GetNaturalOrientation(int width, int height) =>
			height >= width ? DisplayOrientation.Portrait : DisplayOrientation.Landscape;

		internal static (DisplayRotation Rotation, DisplayOrientation Orientation) MapOrientation(
			TizenDeviceOrientation deviceOrientation,
			DisplayOrientation naturalOrientation)
		{
			var rotated = naturalOrientation == DisplayOrientation.Portrait
				? DisplayOrientation.Landscape
				: DisplayOrientation.Portrait;

			return deviceOrientation switch
			{
				TizenDeviceOrientation.Orientation_0 => (DisplayRotation.Rotation0, naturalOrientation),
				TizenDeviceOrientation.Orientation_90 => (DisplayRotation.Rotation90, rotated),
				TizenDeviceOrientation.Orientation_180 => (DisplayRotation.Rotation180, naturalOrientation),
				TizenDeviceOrientation.Orientation_270 => (DisplayRotation.Rotation270, rotated),
				_ => (DisplayRotation.Unknown, DisplayOrientation.Unknown),
			};
		}

		void OnRotationChanged(object? sender, TizenDeviceOrientationEventArgs e)
		{
			var natural = GetNaturalOrientation(DisplayWidth, DisplayHeight);
			(_displayRotation, _displayOrientation) = MapOrientation(e.DeviceOrientation, natural);

			var args = new DisplayInfoChangedEventArgs(MainDisplayInfo);
			_events.Publish(args);
		}

		/// <inheritdoc/>
		public void Dispose() => _events.Dispose();
	}
}
