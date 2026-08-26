using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class DispatcherTests
	{
		/// <summary>
		/// Minimal single-threaded <see cref="SynchronizationContext"/> that stands in for the
		/// Tizen main loop. <c>Post</c> queues; <c>Drain</c> pumps.
		/// </summary>
		sealed class LoopContext : SynchronizationContext
		{
			readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

			public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

			public int Drain(TimeSpan timeout)
			{
				var count = 0;
				var deadline = DateTime.UtcNow + timeout;

				while (DateTime.UtcNow < deadline)
				{
					if (_queue.TryTake(out var item, TimeSpan.FromMilliseconds(10)))
					{
						var previous = Current;
						SetSynchronizationContext(this);
						try
						{
							item.Item1(item.Item2);
						}
						finally
						{
							SetSynchronizationContext(previous);
						}

						count++;
					}
				}

				return count;
			}

			public bool DrainOne(TimeSpan timeout)
			{
				if (!_queue.TryTake(out var item, timeout))
					return false;

				var previous = Current;
				SetSynchronizationContext(this);
				try
				{
					item.Item1(item.Item2);
				}
				finally
				{
					SetSynchronizationContext(previous);
				}

				return true;
			}
		}

		[Fact]
		public void ConstructorRejectsNullContext() =>
			Assert.Throws<ArgumentNullException>(() => new TizenDispatcher(null!));

		[Fact]
		public void IsDispatchRequiredIsTrueOffTheLoopThread()
		{
			var dispatcher = new TizenDispatcher(new LoopContext());

			Assert.True(dispatcher.IsDispatchRequired);
		}

		[Fact]
		public void IsDispatchRequiredIsFalseOnTheLoopThread()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var observed = true;

			dispatcher.Dispatch(() => observed = dispatcher.IsDispatchRequired);
			Assert.True(context.DrainOne(TimeSpan.FromSeconds(2)));

			Assert.False(observed);
		}

		[Fact]
		public void DispatchQueuesOntoTheContext()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var ran = false;

			Assert.True(dispatcher.Dispatch(() => ran = true));
			Assert.False(ran); // Not executed until the loop is pumped.

			Assert.True(context.DrainOne(TimeSpan.FromSeconds(2)));
			Assert.True(ran);
		}

		[Fact]
		public void DispatchRejectsNullAction()
		{
			var dispatcher = new TizenDispatcher(new LoopContext());

			Assert.Throws<ArgumentNullException>(() => dispatcher.Dispatch(null!));
		}

		[Fact]
		public void DispatchDelayedEventuallyQueuesOntoTheContext()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var ran = false;

			Assert.True(dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => ran = true));
			Assert.True(context.DrainOne(TimeSpan.FromSeconds(5)));

			Assert.True(ran);
		}

		/// <summary>
		/// A context whose <c>Post</c> blocks, modelling a main loop that is momentarily busy.
		/// </summary>
		/// <remarks>
		/// This is what makes the one-shot regression deterministic rather than probabilistic.
		/// The original implementation posted to the context and only THEN disposed its timer:
		///
		///     _context.Post(...);   // if this blocks...
		///     timer?.Dispose();     // ...the repeating timer keeps ticking meanwhile
		///
		/// so with a repeating period, a slow loop queues the caller's action several times. A
		/// plain non-blocking context loses that race only occasionally, which is precisely why
		/// the defect survived review.
		/// </remarks>
		sealed class SlowPostContext : SynchronizationContext
		{
			readonly TimeSpan _postDelay;
			readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback, object?)> _queue = new();

			public SlowPostContext(TimeSpan postDelay) => _postDelay = postDelay;

			public override void Post(SendOrPostCallback d, object? state)
			{
				Thread.Sleep(_postDelay);
				_queue.Enqueue((d, state));
			}

			public int DrainCount()
			{
				var count = 0;
				while (_queue.TryDequeue(out var item))
				{
					item.Item1(item.Item2);
					count++;
				}

				return count;
			}
		}

		[Fact]
		public void DispatchDelayedFiresExactlyOnceEvenWhenTheLoopIsSlow()
		{
			// Regression, deterministic. The original port passed `delay` as both due time AND
			// period, making the timer repeating, and cancelled it by disposing from inside its
			// own callback - after posting. With a loop that is briefly busy, the repeating timer
			// re-fires while Post is still blocked and the caller's action is queued several
			// times. Because the action runs on the main loop, that is a user-visible double
			// execution, not a harmless extra tick.
			//
			// Post is made to block for well over the timer period, so a repeating timer queues
			// multiple callbacks with certainty rather than by luck.
			var context = new SlowPostContext(TimeSpan.FromMilliseconds(250));
			var dispatcher = new TizenDispatcher(context);
			var count = 0;

			Assert.True(dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref count)));

			// Long enough that a 20 ms repeating timer would have fired many times.
			Thread.Sleep(900);
			context.DrainCount();

			Assert.Equal(1, Volatile.Read(ref count));
		}

		[Fact]
		public void DispatchDelayedFiresExactlyOnce()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var count = 0;

			Assert.True(dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => Interlocked.Increment(ref count)));

			context.Drain(TimeSpan.FromMilliseconds(600));

			Assert.Equal(1, Volatile.Read(ref count));
		}

		[Fact]
		public void DispatchDelayedDoesNotRunBeforeTheLoopIsPumped()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var ran = false;

			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => ran = true);
			Thread.Sleep(80);

			// The timer has elapsed by now, but the action is queued on the loop, not executed.
			Assert.False(ran);

			Assert.True(context.DrainOne(TimeSpan.FromSeconds(2)));
			Assert.True(ran);
		}

		[Fact]
		public void ConcurrentDelayedDispatchesEachFireExactlyOnce()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var counts = new int[8];

			for (var i = 0; i < counts.Length; i++)
			{
				var index = i;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(5 + index), () => Interlocked.Increment(ref counts[index]));
			}

			context.Drain(TimeSpan.FromMilliseconds(600));

			Assert.All(counts, c => Assert.Equal(1, c));
		}

		[Fact]
		public void CreateTimerReturnsATizenDispatcherTimer()
		{
			var dispatcher = new TizenDispatcher(new LoopContext());

			Assert.IsType<TizenDispatcherTimer>(dispatcher.CreateTimer());
		}

		[Fact]
		public void TimerDoesNotTickBeforeStart()
		{
			var context = new LoopContext();
			using var timer = new TizenDispatcherTimer(context)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			context.Drain(TimeSpan.FromMilliseconds(100));

			Assert.False(timer.IsRunning);
			Assert.Equal(0, ticks);
		}

		[Fact]
		public void NonRepeatingTimerTicksExactlyOnce()
		{
			var context = new LoopContext();
			using var timer = new TizenDispatcherTimer(context)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = false,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			Assert.True(timer.IsRunning);

			context.Drain(TimeSpan.FromMilliseconds(400));

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void RepeatingTimerKeepsTicking()
		{
			var context = new LoopContext();
			using var timer = new TizenDispatcherTimer(context)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			context.Drain(TimeSpan.FromMilliseconds(400));
			timer.Stop();

			Assert.True(ticks > 1, $"Expected more than one tick, observed {ticks}.");
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void StoppedTimerDoesNotRaiseQueuedTicks()
		{
			var context = new LoopContext();
			using var timer = new TizenDispatcherTimer(context)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			Thread.Sleep(50);
			timer.Stop();

			// Callbacks queued before Stop must be swallowed when the loop finally pumps.
			context.Drain(TimeSpan.FromMilliseconds(150));

			Assert.Equal(0, ticks);
		}

		[Fact]
		public void StartIsIdempotent()
		{
			var context = new LoopContext();
			using var timer = new TizenDispatcherTimer(context) { Interval = TimeSpan.FromMilliseconds(5) };

			timer.Start();
			timer.Start();

			Assert.True(timer.IsRunning);
		}

		[Fact]
		public void ProviderReturnsNullWithoutASynchronizationContext()
		{
			var provider = new TizenDispatcherProvider();
			IDispatcher? dispatcher = null;

			var thread = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(null);
				dispatcher = provider.GetForCurrentThread();
			});

			thread.Start();
			thread.Join();

			Assert.Null(dispatcher);
		}

		[Fact]
		public void ProviderReturnsADispatcherWhenAContextExists()
		{
			var provider = new TizenDispatcherProvider();
			IDispatcher? first = null;
			IDispatcher? second = null;

			var thread = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(new LoopContext());
				first = provider.GetForCurrentThread();
				second = provider.GetForCurrentThread();
			});

			thread.Start();
			thread.Join();

			Assert.NotNull(first);
			Assert.IsType<TizenDispatcher>(first);

			// Cached per thread, so repeated resolution must not allocate a new dispatcher.
			Assert.Same(first, second);
		}

		[Fact]
		public void ProviderIsThreadStatic()
		{
			var provider = new TizenDispatcherProvider();
			IDispatcher? a = null;
			IDispatcher? b = null;

			var t1 = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(new LoopContext());
				a = provider.GetForCurrentThread();
			});
			var t2 = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(new LoopContext());
				b = provider.GetForCurrentThread();
			});

			t1.Start();
			t1.Join();
			t2.Start();
			t2.Join();

			Assert.NotNull(a);
			Assert.NotNull(b);
			Assert.NotSame(a, b);
		}
	}
}
