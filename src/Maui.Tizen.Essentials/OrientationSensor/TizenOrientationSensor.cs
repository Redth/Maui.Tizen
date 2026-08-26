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
		protected override void Subscribe(TizenRotationVectorSensor sensor) => sensor.DataUpdated += OnDataUpdated;

		/// <inheritdoc/>
		protected override void Unsubscribe(TizenRotationVectorSensor sensor) => sensor.DataUpdated -= OnDataUpdated;

		void OnDataUpdated(object? sender, global::Tizen.Sensor.RotationVectorSensorDataUpdatedEventArgs e) =>
			Raise(ReadingChanged, new OrientationSensorChangedEventArgs(new OrientationSensorData(e.X, e.Y, e.Z, e.W)));
	}
}
