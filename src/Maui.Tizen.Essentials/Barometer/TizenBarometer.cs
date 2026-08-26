using System;
using Microsoft.Maui.Devices.Sensors;
using TizenPressureSensor = Tizen.Sensor.PressureSensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IBarometer"/>, backed by <c>Tizen.Sensor.PressureSensor</c>.
	/// </summary>
	public sealed class TizenBarometer : TizenSensorBase<TizenPressureSensor>, IBarometer
	{
		/// <inheritdoc/>
		public event EventHandler<BarometerChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public override bool IsSupported => TizenPressureSensor.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(IBarometer);

		/// <inheritdoc/>
		protected override TizenPressureSensor Sensor =>
			(TizenPressureSensor)TizenSensors.GetDefaultSensor(TizenSensorType.Barometer);

		/// <inheritdoc/>
		protected override void Subscribe(TizenPressureSensor sensor) => sensor.DataUpdated += OnDataUpdated;

		/// <inheritdoc/>
		protected override void Unsubscribe(TizenPressureSensor sensor) => sensor.DataUpdated -= OnDataUpdated;

		void OnDataUpdated(object? sender, global::Tizen.Sensor.PressureSensorDataUpdatedEventArgs e) =>
			Raise(ReadingChanged, new BarometerChangedEventArgs(new BarometerData(e.Pressure)));
	}
}
