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
		readonly ITizenDeviceDisplayNative _native;
		bool _keepScreenOn;
		DisplayRotation _displayRotation = DisplayRotation.Rotation0;
		DisplayOrientation _displayOrientation = DisplayOrientation.Unknown;

		/// <summary>Creates the Tizen display service.</summary>
		public TizenDeviceDisplay() : this(TizenDeviceDisplayNative.Instance)
		{
		}

		internal TizenDeviceDisplay(
			ITizenDeviceDisplayNative native,
			TizenNativeCallbackCoordinator? callbacks = null)
		{
			_native = native;
			_events = new(this, StartListeners, callbacks);
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
				var metrics = _native.GetMetrics();

				return new DisplayInfo(
					width: metrics.Width,
					height: metrics.Height,
					density: metrics.Dpi / BaseLogicalDpi,
					orientation: _displayOrientation == DisplayOrientation.Unknown
						? GetNaturalOrientation(metrics.Width, metrics.Height)
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

		Action StartListeners(TizenEventGeneration<DisplayInfoChangedEventArgs> generation)
		{
			return _native.Subscribe(deviceOrientation =>
			{
				generation.Commit(() =>
				{
					var metrics = _native.GetMetrics();
					var natural = GetNaturalOrientation(metrics.Width, metrics.Height);
					(_displayRotation, _displayOrientation) = MapOrientation(deviceOrientation, natural);
					return new DisplayInfoChangedEventArgs(MainDisplayInfo);
				});
			});
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

		/// <inheritdoc/>
		public void Dispose() => _events.Dispose();
	}

	internal readonly record struct TizenDisplayMetrics(int Width, int Height, int Dpi);

	internal interface ITizenDeviceDisplayNative
	{
		TizenDisplayMetrics GetMetrics();

		Action Subscribe(Action<TizenDeviceOrientation> callback);
	}

	sealed class TizenDeviceDisplayNative : ITizenDeviceDisplayNative
	{
		public static TizenDeviceDisplayNative Instance { get; } = new();

		public TizenDisplayMetrics GetMetrics() =>
			new(
				TizenSystemInformation.GetFeatureInfo<int>("screen.width"),
				TizenSystemInformation.GetFeatureInfo<int>("screen.height"),
				TizenSystemInformation.CurrentProfile == TizenDeviceProfile.TV
					? 72
					: TizenSystemInformation.GetFeatureInfo<int>("screen.dpi"));

		public Action Subscribe(Action<TizenDeviceOrientation> callback)
		{
			if (TizenApplication.Current is not TizenCoreApplication app)
				return static () => { };

			EventHandler<TizenDeviceOrientationEventArgs> handler =
				(_, e) => callback(e.DeviceOrientation);
			app.DeviceOrientationChanged += handler;
			return () => app.DeviceOrientationChanged -= handler;
		}
	}
}
