using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using TizenSensor = Tizen.Sensor.Sensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Shared start/stop bookkeeping for the Tizen sensor implementations.
	/// </summary>
	/// <typeparam name="TSensor">The native Tizen sensor type.</typeparam>
	/// <remarks>
	/// <para>
	/// dotnet/maui expressed this logic once per sensor in the <c>*.shared.cs</c> half of a partial
	/// class. Since this backend ships independently named classes, the common behaviour lives here
	/// instead of being copied into each implementation.
	/// </para>
	/// <para>
	/// Start and Stop are transactional. A native failure part way through leaves no subscription
	/// behind and no state claiming the sensor is monitoring when it is not: an earlier version
	/// restored <see cref="IsMonitoring"/> but left <c>DataUpdated</c> attached if
	/// <c>sensor.Start()</c> threw, so the next Start would double-subscribe and every reading was
	/// then raised twice.
	/// </para>
	/// </remarks>
	public abstract class TizenSensorBase<TSensor>
		where TSensor : TizenSensor
	{
		readonly object _locker = new();

		/// <summary>
		/// Gets a value indicating whether readings should be marshalled to the main thread.
		/// </summary>
		protected bool UseSyncContext { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the sensor is currently being monitored.
		/// </summary>
		public bool IsMonitoring { get; private set; }

		/// <summary>
		/// Gets a value indicating whether this device provides the sensor.
		/// </summary>
		public abstract bool IsSupported { get; }

		/// <summary>
		/// Gets the display name used in diagnostics and exception messages.
		/// </summary>
		protected abstract string SensorName { get; }

		/// <summary>
		/// Gets the shared native sensor instance.
		/// </summary>
		protected abstract TSensor Sensor { get; }

		/// <summary>Attaches the native data-updated handler.</summary>
		/// <param name="sensor">The native sensor.</param>
		protected abstract void Subscribe(TSensor sensor);

		/// <summary>Detaches the native data-updated handler.</summary>
		/// <param name="sensor">The native sensor.</param>
		protected abstract void Unsubscribe(TSensor sensor);

		/// <summary>
		/// Called after the sensor has started, so derived types can reset per-session state.
		/// </summary>
		protected virtual void OnStarted()
		{
		}

		/// <summary>
		/// Called after the sensor has stopped, so derived types can release per-session state.
		/// </summary>
		protected virtual void OnStopped()
		{
		}

		/// <summary>
		/// Starts monitoring the sensor.
		/// </summary>
		/// <param name="sensorSpeed">The requested reporting speed.</param>
		/// <exception cref="FeatureNotSupportedException">The device has no such sensor.</exception>
		/// <exception cref="InvalidOperationException">Monitoring is already in progress.</exception>
		public void Start(SensorSpeed sensorSpeed)
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{SensorName}.Start", $"This device has no {SensorName} sensor.");

			lock (_locker)
			{
				if (IsMonitoring)
					throw new InvalidOperationException($"{SensorName} has already been started.");

				var sensor = Sensor;
				var subscribed = false;
				var started = false;

				try
				{
					sensor.Interval = sensorSpeed.ToPlatform();

					Subscribe(sensor);
					subscribed = true;

					sensor.Start();
					started = true;

					OnStarted();

					UseSyncContext = sensorSpeed is SensorSpeed.Default or SensorSpeed.UI;
					IsMonitoring = true;
				}
				catch
				{
					RollbackFailedStart(
						started,
						subscribed,
						() => sensor.Stop(),
						() => Unsubscribe(sensor),
						() => TizenSensors.ResetDefaultSensor(sensor));
					throw;
				}
			}
		}

		internal static void RollbackFailedStart(
			bool started,
			bool subscribed,
			Action stop,
			Action unsubscribe,
			Action reset)
		{
			if (subscribed)
				TryRollback(unsubscribe);

			if (started)
				TryRollback(stop);

			TryRollback(reset);
		}

		static void TryRollback(Action action)
		{
			try
			{
				action();
			}
			catch (Exception)
			{
				// Preserve the failure that caused the rollback.
			}
		}

		/// <summary>
		/// Stops monitoring the sensor.
		/// </summary>
		/// <exception cref="FeatureNotSupportedException">The device has no such sensor.</exception>
		public void Stop()
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{SensorName}.Stop", $"This device has no {SensorName} sensor.");

			lock (_locker)
			{
				if (!IsMonitoring)
					return;

				var sensor = Sensor;

				// Unsubscribe first: once the handler is detached no further readings can be
				// delivered, so even if Stop() throws the sensor is no longer raising events.
				Unsubscribe(sensor);

				try
				{
					sensor.Stop();
				}
				catch
				{
					// The native sensor is still running, so restore the subscription to keep the
					// object's state and the platform's state consistent.
					try
					{
						Subscribe(sensor);
					}
					catch
					{
						// Nothing further can be done; surface the original failure.
					}

					throw;
				}

				IsMonitoring = false;
			}

			OnStopped();
		}

		/// <summary>
		/// Raises a reading changed event, honouring the requested sensor speed.
		/// </summary>
		/// <typeparam name="TArgs">The event argument type.</typeparam>
		/// <param name="handler">The event handler to invoke.</param>
		/// <param name="args">The event arguments.</param>
		protected void Raise<TArgs>(EventHandler<TArgs>? handler, TArgs args)
			where TArgs : EventArgs
		{
			if (handler is null)
				return;

			if (UseSyncContext)
				MainThread.BeginInvokeOnMainThread(() => handler.Invoke(this, args));
			else
				handler.Invoke(this, args);
		}
	}
}
