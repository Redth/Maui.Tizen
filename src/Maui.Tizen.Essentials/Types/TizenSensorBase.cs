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
	/// dotnet/maui expressed this logic once per sensor in a <c>*.shared.cs</c> half of a partial
	/// class. Since this backend ships independently named classes, the common behaviour lives here
	/// instead of being copied into each implementation.
	/// </remarks>
	public abstract class TizenSensorBase<TSensor>
		where TSensor : TizenSensor
	{
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
		/// Starts monitoring the sensor.
		/// </summary>
		/// <param name="sensorSpeed">The requested reporting speed.</param>
		/// <exception cref="FeatureNotSupportedException">The device has no such sensor.</exception>
		/// <exception cref="InvalidOperationException">Monitoring is already in progress.</exception>
		public void Start(SensorSpeed sensorSpeed)
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{SensorName}.Start", $"This device has no {SensorName} sensor.");

			if (IsMonitoring)
				throw new InvalidOperationException($"{SensorName} has already been started.");

			IsMonitoring = true;
			UseSyncContext = sensorSpeed is SensorSpeed.Default or SensorSpeed.UI;

			try
			{
				var sensor = Sensor;
				sensor.Interval = sensorSpeed.ToPlatform();
				Subscribe(sensor);
				sensor.Start();
			}
			catch
			{
				IsMonitoring = false;
				throw;
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

			if (!IsMonitoring)
				return;

			IsMonitoring = false;

			try
			{
				var sensor = Sensor;
				Unsubscribe(sensor);
				sensor.Stop();
			}
			catch
			{
				IsMonitoring = true;
				throw;
			}
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
