using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Sliding window of accelerometer samples used to detect a shake gesture.
	/// </summary>
	/// <remarks>
	/// Behavioural port of the internal <c>Microsoft.Maui.Devices.Sensors.AccelerometerQueue</c>
	/// used by dotnet/maui, reimplemented here because that type is not public. A shake is reported
	/// when the window covers at least 250ms and more than three quarters of the samples in it
	/// exceeded the acceleration threshold.
	/// </remarks>
	internal sealed class TizenAccelerometerQueue
	{
		const long MaxWindowSizeNanoseconds = 500_000_000;
		const long MinWindowSizeNanoseconds = 250_000_000;
		const int MinQueueSize = 4;

		readonly Queue<(long Timestamp, bool IsAccelerating)> _samples = new();

		int _acceleratingCount;

		internal void Add(long timestamp, bool accelerating)
		{
			Purge(timestamp - MaxWindowSizeNanoseconds);

			_samples.Enqueue((timestamp, accelerating));

			if (accelerating)
				_acceleratingCount++;
		}

		internal void Clear()
		{
			_samples.Clear();
			_acceleratingCount = 0;
		}

		internal bool IsShaking
		{
			get
			{
				if (_samples.Count == 0)
					return false;

				var oldest = _samples.Peek().Timestamp;
				var newest = NewestTimestamp;
				var count = _samples.Count;

				return newest - oldest >= MinWindowSizeNanoseconds &&
					_acceleratingCount >= (count >> 1) + (count >> 2);
			}
		}

		long NewestTimestamp
		{
			get
			{
				var newest = 0L;
				foreach (var sample in _samples)
					newest = sample.Timestamp;
				return newest;
			}
		}

		void Purge(long cutoff)
		{
			while (_samples.Count >= MinQueueSize && cutoff - _samples.Peek().Timestamp > 0)
			{
				var removed = _samples.Dequeue();
				if (removed.IsAccelerating)
					_acceleratingCount--;
			}
		}

		internal static long ToNanoseconds(DateTime time) =>
			time.Ticks / TimeSpan.TicksPerMillisecond * 1_000_000;
	}
}
