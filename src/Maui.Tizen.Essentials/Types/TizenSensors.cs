using System;
using Microsoft.Maui.Devices.Sensors;
using TizenAccelerometerSensor = Tizen.Sensor.Accelerometer;
using TizenGyroscopeSensor = Tizen.Sensor.Gyroscope;
using TizenMagnetometerSensor = Tizen.Sensor.Magnetometer;
using TizenNativeOrientationSensor = Tizen.Sensor.OrientationSensor;
using TizenPressureSensor = Tizen.Sensor.PressureSensor;
using TizenRotationVectorSensor = Tizen.Sensor.RotationVectorSensor;
using TizenSensor = Tizen.Sensor.Sensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// The Tizen sensors used by this backend.
	/// </summary>
	public enum TizenSensorType
	{
		/// <summary>The accelerometer.</summary>
		Accelerometer,

		/// <summary>The pressure (barometer) sensor.</summary>
		Barometer,

		/// <summary>The orientation sensor used to derive a compass heading.</summary>
		Compass,

		/// <summary>The gyroscope.</summary>
		Gyroscope,

		/// <summary>The magnetometer.</summary>
		Magnetometer,

		/// <summary>The rotation vector sensor used for device orientation.</summary>
		OrientationSensor,
	}

	/// <summary>
	/// Caches one native Tizen sensor instance per sensor type.
	/// </summary>
	/// <remarks>
	/// Replaces the internal <c>PlatformUtils.GetDefaultSensor</c> helper from the in-box dotnet/maui
	/// Tizen backend. Instances are created lazily so that loading this assembly never touches native
	/// sensor libraries.
	/// </remarks>
	public static class TizenSensors
	{
		static readonly Lazy<TizenAccelerometerSensor> AccelerometerSensor = new(static () => new TizenAccelerometerSensor());
		static readonly Lazy<TizenPressureSensor> BarometerSensor = new(static () => new TizenPressureSensor());
		static readonly Lazy<TizenNativeOrientationSensor> CompassSensor = new(static () => new TizenNativeOrientationSensor());
		static readonly Lazy<TizenGyroscopeSensor> GyroscopeSensor = new(static () => new TizenGyroscopeSensor());
		static readonly Lazy<TizenMagnetometerSensor> MagnetometerSensor = new(static () => new TizenMagnetometerSensor());
		static readonly Lazy<TizenRotationVectorSensor> RotationVectorSensor = new(static () => new TizenRotationVectorSensor());

		/// <summary>
		/// Gets the shared native sensor instance for the requested type.
		/// </summary>
		/// <param name="type">The sensor to resolve.</param>
		/// <returns>The shared native sensor instance.</returns>
		public static TizenSensor GetDefaultSensor(TizenSensorType type) =>
			type switch
			{
				TizenSensorType.Accelerometer => AccelerometerSensor.Value,
				TizenSensorType.Barometer => BarometerSensor.Value,
				TizenSensorType.Compass => CompassSensor.Value,
				TizenSensorType.Gyroscope => GyroscopeSensor.Value,
				TizenSensorType.Magnetometer => MagnetometerSensor.Value,
				TizenSensorType.OrientationSensor => RotationVectorSensor.Value,
				_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Tizen sensor type."),
			};
	}

	/// <summary>
	/// Maps <see cref="SensorSpeed"/> onto Tizen sensor update intervals in milliseconds.
	/// </summary>
	public static class TizenSensorSpeedExtensions
	{
		internal const uint SensorIntervalFastest = 0;
		internal const uint SensorIntervalGame = 20;
		internal const uint SensorIntervalUI = 60;
		internal const uint SensorIntervalDefault = 200;

		/// <summary>
		/// Converts a <see cref="SensorSpeed"/> into a Tizen sensor interval in milliseconds.
		/// </summary>
		/// <param name="sensorSpeed">The requested sensor speed.</param>
		/// <returns>The Tizen sensor interval, in milliseconds.</returns>
		public static uint ToPlatform(this SensorSpeed sensorSpeed) =>
			sensorSpeed switch
			{
				SensorSpeed.Fastest => SensorIntervalFastest,
				SensorSpeed.Game => SensorIntervalGame,
				SensorSpeed.UI => SensorIntervalUI,
				_ => SensorIntervalDefault,
			};
	}
}
