using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using TizenSensor = Tizen.Sensor.Sensor;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Shared generation and ref-counted native lifetime for a Tizen sensor implementation.
	/// </summary>
	public abstract class TizenSensorBase<TSensor>
		where TSensor : TizenSensor
	{
		static readonly TizenSensorLifetimeCoordinator<TSensor> Lifetime = new();
		readonly TizenSensorGenerationGate _generation = new();
		readonly object _operationLock = new();
		readonly TizenNativeCallbackCoordinator _callbacks = new();

		/// <summary>Gets whether readings should be marshalled to the main thread.</summary>
		protected bool UseSyncContext => _generation.UseSyncContext;

		/// <summary>Gets whether this wrapper is currently monitoring.</summary>
		public bool IsMonitoring => _generation.IsMonitoring;

		/// <summary>Gets whether this device provides the sensor.</summary>
		public abstract bool IsSupported { get; }

		/// <summary>Gets the display name used in diagnostics.</summary>
		protected abstract string SensorName { get; }

		/// <summary>Gets the shared native sensor instance.</summary>
		protected abstract TSensor Sensor { get; }

		/// <summary>
		/// Subscribes a generation-specific native callback and returns its exact unsubscribe action.
		/// </summary>
		protected abstract Action Subscribe(TSensor sensor, long generation);

		/// <summary>Resets per-wrapper state after a successful start.</summary>
		protected virtual void OnStarted()
		{
		}

		/// <summary>Releases per-wrapper state after stop or failed start.</summary>
		protected virtual void OnStopped()
		{
		}

		/// <summary>Starts monitoring.</summary>
		public void Start(SensorSpeed sensorSpeed)
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{SensorName}.Start", $"This device has no {SensorName} sensor.");
			lock (_operationLock)
			{
				var generation = _generation.BeginStart(
					sensorSpeed is SensorSpeed.Default or SensorSpeed.UI,
					SensorName);

				try
				{
					Lifetime.Start(
						this,
						() => Sensor,
						sensorSpeed.ToPlatform(),
						static (sensor, interval) => sensor.Interval = interval,
						sensor => Subscribe(sensor, generation),
						static sensor => sensor.Start(),
						static sensor => sensor.Stop(),
						static sensor => TizenSensors.ResetDefaultSensor(sensor),
						OnStarted,
						OnStopped);
				}
				catch
				{
					_generation.Invalidate();
					throw;
				}
			}
		}

		/// <summary>Stops monitoring.</summary>
		public void Stop()
		{
			if (!IsSupported)
				throw TizenEssentialsSupport.NotSupported($"{SensorName}.Stop", $"This device has no {SensorName} sensor.");
			lock (_operationLock)
			{
				if (!IsMonitoring)
					return;

				Lifetime.Stop(
					this,
					() => _generation.Invalidate(),
					static sensor => sensor.Stop(),
					static sensor => TizenSensors.ResetDefaultSensor(sensor),
					static (sensor, interval) => sensor.Interval = interval,
					OnStopped);
			}
		}

		/// <summary>Returns whether a native callback belongs to the active start generation.</summary>
		protected bool IsCurrentGeneration(long generation) =>
			_generation.IsCurrent(generation);

		/// <summary>Raises a reading only while its start generation is still active.</summary>
		protected void Raise<TArgs>(
			long generation,
			EventHandler<TArgs>? handler,
			TArgs args)
			where TArgs : EventArgs
		{
			if (handler is null || !IsCurrentGeneration(generation))
				return;

			if (UseSyncContext)
			{
				_callbacks.Post(
					() => IsCurrentGeneration(generation),
					() => handler.Invoke(this, args));
			}
			else if (IsCurrentGeneration(generation))
			{
				handler.Invoke(this, args);
			}
		}
	}

	internal sealed class TizenSensorGenerationGate
	{
		long _generation;
		int _monitoring;
		int _useSyncContext;

		public bool IsMonitoring => Volatile.Read(ref _monitoring) != 0;

		public bool UseSyncContext => Volatile.Read(ref _useSyncContext) != 0;

		public long BeginStart(bool useSyncContext, string sensorName)
		{
			if (Interlocked.CompareExchange(ref _monitoring, 1, 0) != 0)
				throw new InvalidOperationException($"{sensorName} has already been started.");

			Volatile.Write(ref _useSyncContext, useSyncContext ? 1 : 0);
			return Interlocked.Increment(ref _generation);
		}

		public bool Invalidate()
		{
			if (Interlocked.Exchange(ref _monitoring, 0) == 0)
				return false;

			Interlocked.Increment(ref _generation);
			return true;
		}

		public bool IsCurrent(long generation) =>
			IsMonitoring && Volatile.Read(ref _generation) == generation;
	}

	internal sealed class TizenSensorLifetimeCoordinator<TSensor>
		where TSensor : class
	{
		readonly object _locker = new();
		readonly Dictionary<object, Registration> _registrations =
			new(ReferenceEqualityComparer.Instance);
		TSensor? _sensor;

		public int ActiveCount
		{
			get
			{
				lock (_locker)
					return _registrations.Count;
			}
		}

		public void Start(
			object owner,
			Func<TSensor> acquire,
			uint interval,
			Action<TSensor, uint> setInterval,
			Func<TSensor, Action> subscribe,
			Action<TSensor> start,
			Action<TSensor> stop,
			Action<TSensor> reset,
			Action started,
			Action stopped)
		{
			lock (_locker)
			{
				if (_registrations.ContainsKey(owner))
					throw new InvalidOperationException("This sensor wrapper has already started.");

				// Acquire while holding the same lock that clears the final cached sensor. A new
				// owner can never capture the instance another thread is still stopping/resetting.
				_sensor ??= acquire();
				var first = _registrations.Count == 0;
				Action? unsubscribe = null;
				var registered = false;
				var nativeStartAttempted = false;

				try
				{
					unsubscribe = subscribe(_sensor);
					_registrations.Add(owner, new(interval, unsubscribe));
					registered = true;
					ApplyFastestInterval(_sensor, setInterval);
					started();

					if (first)
					{
						nativeStartAttempted = true;
						start(_sensor);
					}

				}
				catch
				{
					if (registered)
						_registrations.Remove(owner);
					TryCleanup(unsubscribe);
					if (first && nativeStartAttempted)
						TryCleanup(() => stop(_sensor));

					if (_registrations.Count == 0)
					{
						TryCleanup(() => reset(_sensor));
						_sensor = null;
					}
					else
					{
						TryCleanup(() => ApplyFastestInterval(_sensor, setInterval));
					}

					TryCleanup(stopped);
					throw;
				}
			}
		}

		public void Stop(
			object owner,
			Action invalidate,
			Action<TSensor> stop,
			Action<TSensor> reset,
			Action<TSensor, uint> setInterval,
			Action stopped)
		{
			lock (_locker)
			{
				if (!_registrations.TryGetValue(owner, out var registration) || _sensor is null)
					return;

				invalidate();
				_registrations.Remove(owner);
				var failures = new List<Exception>();
				TryCleanup(registration.Unsubscribe, failures);

				if (_registrations.Count == 0)
				{
					TryCleanup(() => stop(_sensor), failures);
					TryCleanup(() => reset(_sensor), failures);
					_sensor = null;
				}
				else
				{
					TryCleanup(() => ApplyFastestInterval(_sensor, setInterval), failures);
				}

				TryCleanup(stopped, failures);

				if (failures.Count == 1)
					throw failures[0];
				if (failures.Count > 1)
					throw new AggregateException("Sensor teardown failed.", failures);
			}
		}

		void ApplyFastestInterval(TSensor sensor, Action<TSensor, uint> setInterval)
		{
			if (_registrations.Count == 0)
				return;

			setInterval(sensor, _registrations.Values.Min(registration => registration.Interval));
		}

		static void TryCleanup(Action? action)
		{
			try
			{
				action?.Invoke();
			}
			catch (Exception)
			{
				// Preserve the failure that triggered rollback.
			}
		}

		static void TryCleanup(Action action, ICollection<Exception> failures)
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}

		sealed record Registration(uint Interval, Action Unsubscribe);
	}
}
