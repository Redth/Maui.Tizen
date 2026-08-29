using System;
using Microsoft.Maui.Devices.Sensors;
using TizenOrientationSensorNative = Tizen.Sensor.OrientationSensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ICompass"/>, backed by the azimuth reported by
	/// <c>Tizen.Sensor.OrientationSensor</c>.
	/// </summary>
	/// <remarks>
	/// Tizen's orientation sensor is already fused and low-pass filtered in the platform, so the
	/// <c>applyLowPassFilter</c> argument has no additional effect. It is accepted for source
	/// compatibility rather than silently changing behaviour.
	/// </remarks>
	public sealed class TizenCompass : TizenSensorBase<TizenOrientationSensorNative>, ICompass
	{
		/// <inheritdoc/>
		public event EventHandler<CompassChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public override bool IsSupported => TizenOrientationSensorNative.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(ICompass);

		/// <inheritdoc/>
		protected override TizenOrientationSensorNative Sensor =>
			(TizenOrientationSensorNative)TizenSensors.GetDefaultSensor(TizenSensorType.Compass);

		/// <inheritdoc/>
		public new void Start(SensorSpeed sensorSpeed) => base.Start(sensorSpeed);

		/// <inheritdoc/>
		public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) => base.Start(sensorSpeed);

		/// <inheritdoc/>
		protected override Action Subscribe(TizenOrientationSensorNative sensor, long generation)
		{
			EventHandler<global::Tizen.Sensor.OrientationSensorDataUpdatedEventArgs> handler =
				(sender, e) => Raise(
					generation,
					ReadingChanged,
					new CompassChangedEventArgs(new CompassData(e.Azimuth)));
			sensor.DataUpdated += handler;
			return () => sensor.DataUpdated -= handler;
		}
	}
}
