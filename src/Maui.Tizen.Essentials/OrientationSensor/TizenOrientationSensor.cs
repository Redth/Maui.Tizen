using System;
using Microsoft.Maui.Devices.Sensors;
using TizenRotationVectorSensor = Tizen.Sensor.RotationVectorSensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IOrientationSensor"/>, backed by <c>Tizen.Sensor.RotationVectorSensor</c>.
	/// </summary>
	public sealed class TizenOrientationSensor : TizenSensorBase<TizenRotationVectorSensor>, IOrientationSensor
	{
		/// <inheritdoc/>
		public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public override bool IsSupported => TizenRotationVectorSensor.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(IOrientationSensor);

		/// <inheritdoc/>
		protected override TizenRotationVectorSensor Sensor =>
			(TizenRotationVectorSensor)TizenSensors.GetDefaultSensor(TizenSensorType.OrientationSensor);

		/// <inheritdoc/>
		protected override Action Subscribe(TizenRotationVectorSensor sensor, long generation)
		{
			EventHandler<global::Tizen.Sensor.RotationVectorSensorDataUpdatedEventArgs> handler =
				(sender, e) => Raise(
					generation,
					ReadingChanged,
					new OrientationSensorChangedEventArgs(new OrientationSensorData(e.X, e.Y, e.Z, e.W)));
			sensor.DataUpdated += handler;
			return () => sensor.DataUpdated -= handler;
		}
	}
}
