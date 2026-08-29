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
	/// <remarks>
	/// Consolidated onto <see cref="TizenSensorBase{TSensor}"/> so it shares the transactional
	/// start/stop handling with every other sensor. It previously duplicated that logic and, in
	/// doing so, missed the subscription rollback on a failed start.
	/// </remarks>
	public sealed class TizenAccelerometer : TizenSensorBase<TizenAccelerometerSensor>, IAccelerometer
	{
		const double AccelerationThreshold = 169;
		const double Gravity = 9.81;

		readonly TizenAccelerometerQueue _queue = new();
		readonly object _queueLocker = new();
		readonly TizenNativeCallbackCoordinator _callbacks = new();

		/// <inheritdoc/>
		public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;

		/// <inheritdoc/>
		public event EventHandler? ShakeDetected;

		/// <inheritdoc/>
		public override bool IsSupported => TizenAccelerometerSensor.IsSupported;

		/// <inheritdoc/>
		protected override string SensorName => nameof(IAccelerometer);

		/// <inheritdoc/>
		protected override TizenAccelerometerSensor Sensor =>
			(TizenAccelerometerSensor)TizenSensors.GetDefaultSensor(TizenSensorType.Accelerometer);

		/// <inheritdoc/>
		protected override Action Subscribe(TizenAccelerometerSensor sensor, long generation)
		{
			EventHandler<global::Tizen.Sensor.AccelerometerDataUpdatedEventArgs> handler =
				(sender, e) => OnDataUpdated(generation, e);
			sensor.DataUpdated += handler;
			return () => sensor.DataUpdated -= handler;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// The shake window is per-session. Carrying samples across a stop/start would let readings
		/// from before the gap combine with fresh ones and report a shake that never happened.
		/// </remarks>
		protected override void OnStarted() => ClearShakeWindow();

		/// <inheritdoc/>
		protected override void OnStopped() => ClearShakeWindow();

		void ClearShakeWindow()
		{
			lock (_queueLocker)
				_queue.Clear();
		}

		void OnDataUpdated(long generation, global::Tizen.Sensor.AccelerometerDataUpdatedEventArgs e)
		{
			if (!IsCurrentGeneration(generation))
				return;

			var reading = new AccelerometerData(e.X, e.Y, e.Z);

			Raise(generation, ReadingChanged, new AccelerometerChangedEventArgs(reading));

			if (ShakeDetected is not null && IsCurrentGeneration(generation))
				ProcessShakeEvent(generation, reading.Acceleration);
		}

		void ProcessShakeEvent(long generation, Vector3 acceleration)
		{
			var now = TizenAccelerometerQueue.ToNanoseconds(DateTime.UtcNow);

			var x = acceleration.X * Gravity;
			var y = acceleration.Y * Gravity;
			var z = acceleration.Z * Gravity;

			bool shaking;

			// The native sensor may deliver readings concurrently with a Stop() clearing the window.
			lock (_queueLocker)
			{
				_queue.Add(now, (x * x) + (y * y) + (z * z) > AccelerationThreshold);

				shaking = _queue.IsShaking;

				if (shaking)
					_queue.Clear();
			}

			if (!shaking || !IsCurrentGeneration(generation))
				return;

			var handler = ShakeDetected;

			if (handler is null)
				return;

			if (UseSyncContext)
				_callbacks.Post(
					() => IsCurrentGeneration(generation),
					() => handler.Invoke(this, EventArgs.Empty));
			else
			{
				if (IsCurrentGeneration(generation))
					handler.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
