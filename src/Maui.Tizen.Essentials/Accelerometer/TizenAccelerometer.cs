using System;
using System.Numerics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using TizenAccelerometerSensor = Tizen.Sensor.Accelerometer;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IAccelerometer"/>, backed by <c>Tizen.Sensor.Accelerometer</c>.
	/// </summary>
	public sealed class TizenAccelerometer : IAccelerometer
	{
		const double AccelerationThreshold = 169;
		const double Gravity = 9.81;

		readonly TizenAccelerometerQueue _queue = new();

		bool _useSyncContext;

		/// <inheritdoc/>
		public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public event EventHandler? ShakeDetected;

		/// <inheritdoc/>
		public bool IsSupported => TizenAccelerometerSensor.IsSupported;

		/// <inheritdoc/>
		public bool IsMonitoring { get; private set; }

		static TizenAccelerometerSensor DefaultSensor =>
			(TizenAccelerometerSensor)TizenSensors.GetDefaultSensor(TizenSensorType.Accelerometer);

		/// <inheritdoc/>
		public void Start(SensorSpeed sensorSpeed)
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{nameof(IAccelerometer)}.{nameof(Start)}", "This device has no accelerometer.");

			if (IsMonitoring)
				throw new InvalidOperationException("Accelerometer has already been started.");

			IsMonitoring = true;
			_useSyncContext = sensorSpeed is SensorSpeed.Default or SensorSpeed.UI;

			try
			{
				var sensor = DefaultSensor;
				sensor.Interval = sensorSpeed.ToPlatform();
				sensor.DataUpdated += OnDataUpdated;
				sensor.Start();
			}
			catch
			{
				IsMonitoring = false;
				throw;
			}
		}

		/// <inheritdoc/>
		public void Stop()
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{nameof(IAccelerometer)}.{nameof(Stop)}", "This device has no accelerometer.");

			if (!IsMonitoring)
				return;

			IsMonitoring = false;

			try
			{
				var sensor = DefaultSensor;
				sensor.DataUpdated -= OnDataUpdated;
				sensor.Stop();
			}
			catch
			{
				IsMonitoring = true;
				throw;
			}
		}

		void OnDataUpdated(object? sender, global::Tizen.Sensor.AccelerometerDataUpdatedEventArgs e) =>
			RaiseReadingChanged(new AccelerometerData(e.X, e.Y, e.Z));

		void RaiseReadingChanged(AccelerometerData reading)
		{
			var args = new AccelerometerChangedEventArgs(reading);

			if (_useSyncContext)
				MainThread.BeginInvokeOnMainThread(() => ReadingChanged?.Invoke(this, args));
			else
				ReadingChanged?.Invoke(this, args);

			if (ShakeDetected is not null)
				ProcessShakeEvent(reading.Acceleration);
		}

		void ProcessShakeEvent(Vector3 acceleration)
		{
			var now = TizenAccelerometerQueue.ToNanoseconds(DateTime.UtcNow);

			var x = acceleration.X * Gravity;
			var y = acceleration.Y * Gravity;
			var z = acceleration.Z * Gravity;

			_queue.Add(now, (x * x) + (y * y) + (z * z) > AccelerationThreshold);

			if (!_queue.IsShaking)
				return;

			_queue.Clear();

			var args = EventArgs.Empty;

			if (_useSyncContext)
				MainThread.BeginInvokeOnMainThread(() => ShakeDetected?.Invoke(this, args));
			else
				ShakeDetected?.Invoke(this, args);
		}
	}
}
