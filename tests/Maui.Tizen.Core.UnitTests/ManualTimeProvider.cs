using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The delayed-dispatch tests used to arm a real timer for a few milliseconds and then pump the
	/// loop for a fixed wall-clock window, asserting on whatever had arrived. That is a race
	/// against the machine rather than a test of the dispatcher, and it duly failed in CI with
	/// <c>ConcurrentDelayedDispatchesEachFireExactlyOnce</c> reporting zero of its callbacks fired.
	/// Nothing was wrong with the dispatcher; the runner was simply too busy to deliver the timers
	/// inside the window.
	/// </para>
	/// <para>
	/// Driving the clock explicitly removes the timing dimension entirely: <see cref="Advance"/>
	/// fires exactly the timers that are due and returns once their callbacks have run, so the
	/// assertions that follow are about ordering and multiplicity - which is what they were always
	/// meant to be about.
	/// </para>
	/// <para>
	/// Hand-rolled rather than taking a dependency on Microsoft.Extensions.TimeProvider.Testing:
	/// this is a handful of lines, and the package version list is owned by another workstream.
	/// </para>
	/// </remarks>
	public sealed class ManualTimeProvider : TimeProvider
	{
		readonly object _gate = new();
		readonly List<FakeTimer> _timers = new();

		/// <summary>
		/// Makes every timer invoke its callback twice per due time, simulating a timer
		/// implementation that queues a second tick before the first has finished.
		/// </summary>
		/// <remarks>
		/// Fault injection, because that race is exactly what the upstream Tizen dispatcher
		/// suffered from and it cannot be produced on demand with a real timer. Without it the
		/// exactly-once guard in DelayedDispatch is unfalsifiable: removing the guard passes every
		/// test, which is indistinguishable from the guard being pointless.
		/// </remarks>
		public bool DoubleFireTimers { get; set; }

		/// <summary>
		/// Captures fired callbacks instead of invoking them, so a test can release one AFTER the
		/// timer has been stopped and restarted.
		/// </summary>
		/// <remarks>
		/// Synchronous firing cannot reproduce the bug this exists for. A real underlying timer
		/// callback can be held before it reaches the synchronization context; if the arming
		/// identity is read from a mutable field when the callback finally runs, the stale callback
		/// inherits the CURRENT generation and consumes the restarted timer. Holding the callback
		/// here reproduces that ordering deterministically.
		/// </remarks>
		public bool HoldCallbacks { get; set; }

		readonly List<Action> _held = new();

		/// <summary>Runs every callback captured while <see cref="HoldCallbacks"/> was set.</summary>
		public int ReleaseHeldCallbacks()
		{
			Action[] pending;

			lock (_gate)
			{
				pending = _held.ToArray();
				_held.Clear();
			}

			foreach (var callback in pending)
				callback();

			return pending.Length;
		}

		DateTimeOffset _now = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow()
		{
			lock (_gate)
				return _now;
		}

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			ArgumentNullException.ThrowIfNull(callback);

			var timer = new FakeTimer(this, callback, state);
			timer.Change(dueTime, period);

			lock (_gate)
				_timers.Add(timer);

			return timer;
		}

		/// <summary>Moves the clock forward, firing every timer that becomes due.</summary>
		/// <remarks>
		/// Advances in due-time order rather than in one jump, so a repeating timer fires once per
		/// elapsed period and callbacks observe a monotonically increasing clock.
		/// </remarks>
		public void Advance(TimeSpan by)
		{
			if (by < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(by));

			var target = GetUtcNow() + by;

			while (true)
			{
				FakeTimer? next;

				lock (_gate)
				{
					next = _timers
						.Where(t => t.DueAt is { } due && due <= target)
						.OrderBy(t => t.DueAt!.Value)
						.FirstOrDefault();

					if (next is null)
					{
						_now = target;
						return;
					}

					_now = next.DueAt!.Value;
				}

				next.Fire();
			}
		}

		internal void Hold(Action callback)
		{
			lock (_gate)
				_held.Add(callback);
		}

		internal void Remove(FakeTimer timer)
		{
			lock (_gate)
				_timers.Remove(timer);
		}

		internal sealed class FakeTimer : ITimer
		{
			readonly ManualTimeProvider _provider;
			readonly TimerCallback _callback;
			readonly object? _state;

			TimeSpan _period = Timeout.InfiniteTimeSpan;
			bool _disposed;

			public FakeTimer(ManualTimeProvider provider, TimerCallback callback, object? state)
			{
				_provider = provider;
				_callback = callback;
				_state = state;
			}

			public DateTimeOffset? DueAt { get; private set; }

			public bool Change(TimeSpan dueTime, TimeSpan period)
			{
				if (_disposed)
					return false;

				_period = period;
				DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _provider.GetUtcNow() + dueTime;

				return true;
			}

			public void Fire()
			{
				if (_disposed)
					return;

				// Re-arm (or disarm) BEFORE invoking, so a callback that disposes or re-changes the
				// timer wins - which is the behaviour a real timer has and the thing the dispatcher
				// relies on.
				DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero
					? null
					: _provider.GetUtcNow() + _period;

				if (_provider.HoldCallbacks)
				{
					// Captured, not invoked. The arming that queued it is recorded by the closure
					// the dispatcher created; releasing later must not let it adopt a newer one.
					_provider.Hold(() => _callback(_state));
					return;
				}

				_callback(_state);

				if (_provider.DoubleFireTimers)
					_callback(_state);
			}

			public void Dispose()
			{
				_disposed = true;
				DueAt = null;
				_provider.Remove(this);
			}

			public ValueTask DisposeAsync()
			{
				Dispose();
				return ValueTask.CompletedTask;
			}
		}
	}
}
