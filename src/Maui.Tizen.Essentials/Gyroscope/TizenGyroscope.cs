using System;
using Microsoft.Maui.Devices.Sensors;
using TizenGyroscopeSensor = Tizen.Sensor.Gyroscope;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IGyroscope"/>, backed by <c>Tizen.Sensor.Gyroscope</c>.
	/// </summary>
	public sealed class TizenGyroscope : TizenSensorBase<TizenGyroscopeSensor>, IGyroscope
	{
		/// <inheritdoc/>
		public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public override bool IsSupported => TizenGyroscopeSensor.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(IGyroscope);

		/// <inheritdoc/>
		protected override TizenGyroscopeSensor Sensor =>
			(TizenGyroscopeSensor)TizenSensors.GetDefaultSensor(TizenSensorType.Gyroscope);

		/// <inheritdoc/>
		protected override Action Subscribe(TizenGyroscopeSensor sensor, long generation)
		{
			EventHandler<global::Tizen.Sensor.GyroscopeDataUpdatedEventArgs> handler =
				(sender, e) => Raise(
					generation,
					ReadingChanged,
					new GyroscopeChangedEventArgs(new GyroscopeData(e.X, e.Y, e.Z)));
			sensor.DataUpdated += handler;
			return () => sensor.DataUpdated -= handler;
		}
	}
}
