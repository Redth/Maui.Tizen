using System;
using Microsoft.Maui.Devices.Sensors;
using TizenMagnetometerSensor = Tizen.Sensor.Magnetometer;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IMagnetometer"/>, backed by <c>Tizen.Sensor.Magnetometer</c>.
	/// </summary>
	public sealed class TizenMagnetometer : TizenSensorBase<TizenMagnetometerSensor>, IMagnetometer
	{
		/// <inheritdoc/>
		public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public override bool IsSupported => TizenMagnetometerSensor.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(IMagnetometer);

		/// <inheritdoc/>
		protected override TizenMagnetometerSensor Sensor =>
			(TizenMagnetometerSensor)TizenSensors.GetDefaultSensor(TizenSensorType.Magnetometer);

		/// <inheritdoc/>
		protected override Action Subscribe(TizenMagnetometerSensor sensor, long generation)
		{
			EventHandler<global::Tizen.Sensor.MagnetometerDataUpdatedEventArgs> handler =
				(sender, e) => Raise(
					generation,
					ReadingChanged,
					new MagnetometerChangedEventArgs(new MagnetometerData(e.X, e.Y, e.Z)));
			sensor.DataUpdated += handler;
			return () => sensor.DataUpdated -= handler;
		}
	}
}
