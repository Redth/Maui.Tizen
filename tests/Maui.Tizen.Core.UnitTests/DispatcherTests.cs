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

			/// <summary>
			/// Pumps until <paramref name="expected"/> callbacks have run, or the timeout expires.
			/// </summary>
			/// <remarks>
			/// Drain(TimeSpan) pumps for a fixed wall-clock window, which makes any test built on
			/// it a race against the machine rather than a test of the dispatcher.
			/// ConcurrentDelayedDispatchesEachFireExactlyOnce failed in CI with 7 of its 8 delayed
			/// callbacks unfired - not because the dispatcher dropped them, but because a loaded
			/// runner did not get round to them inside 600ms.
			///
			/// Waiting for the WORK instead of for the clock removes the flake without weakening
			/// the assertion: callers still verify each callback fired exactly once, and a genuine
			/// hang still fails on the timeout.
			/// </remarks>
			public int DrainUntil(int expected, TimeSpan timeout)
			{
				var count = 0;
				var deadline = DateTime.UtcNow + timeout;

				while (count < expected && DateTime.UtcNow < deadline)
				{
					if (!_queue.TryTake(out var item, TimeSpan.FromMilliseconds(10)))
						continue;

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

				return count;
			}

			/// <summary>Runs every callback queued right now and returns how many there were.</summary>
			/// <remarks>
			/// No timeout, because with a ManualTimeProvider there is nothing to wait FOR: once
			/// Advance returns, everything it triggered has already been posted. A test that waits
			/// here is reintroducing the very flakiness the fake clock removes.
			/// </remarks>
			public int DrainAvailable()
			{
				var count = 0;

				while (_queue.TryTake(out var item, TimeSpan.Zero))
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
			// The clock is driven explicitly and the timer is made to double-fire, so the race is
			// reproduced on every run instead of depending on how busy the machine is. The
			// comment above used to claim this test was deterministic while it slept for 900ms and
			// hoped - and it duly failed on a loaded CI runner with zero callbacks delivered,
			// having asserted one.
			//
			// The slow Post is kept, because that interleaving is the point: the callback is still
			// inside Post when the timer comes round again.
			var context = new SlowPostContext(TimeSpan.FromMilliseconds(50));
			var time = new ManualTimeProvider { DoubleFireTimers = true };
			var dispatcher = new TizenDispatcher(context, time);
			var count = 0;

			Assert.True(dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(20), () => Interlocked.Increment(ref count)));

			time.Advance(TimeSpan.FromMilliseconds(20));

			// Far past the delay: a repeating timer would have come due many times over.
			time.Advance(TimeSpan.FromMinutes(5));

			Assert.Equal(1, context.DrainCount());
			Assert.Equal(1, Volatile.Read(ref count));
		}

		[Fact]
		public void DispatchDelayedFiresExactlyOnce()
		{
			var context = new LoopContext();
			var dispatcher = new TizenDispatcher(context);
			var count = 0;

			var time = new ManualTimeProvider();
			var dispatcher2 = new TizenDispatcher(context, time);

			Assert.True(dispatcher2.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => Interlocked.Increment(ref count)));

			// Nothing is due yet, so nothing may have been queued.
			time.Advance(TimeSpan.FromMilliseconds(9));
			Assert.Equal(0, context.DrainAvailable());

			time.Advance(TimeSpan.FromMilliseconds(1));
			Assert.Equal(1, context.DrainAvailable());
			Assert.Equal(1, Volatile.Read(ref count));

			// Push the clock a long way past the delay. A repeating timer would become due again
			// and post a second time; a one-shot must not, no matter how far time moves.
			time.Advance(TimeSpan.FromMinutes(5));

			Assert.Equal(0, context.DrainAvailable());
			Assert.Equal(1, Volatile.Read(ref count));

			_ = dispatcher;
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
			var time = new ManualTimeProvider();
			var dispatcher = new TizenDispatcher(context, time);
			var counts = new int[8];

			for (var i = 0; i < counts.Length; i++)
			{
				var index = i;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(5 + index), () => Interlocked.Increment(ref counts[index]));
			}

			// Every timer becomes due in one advance, so they are delivered back to back - which is
			// the interleaving most likely to expose a shared-state bug between concurrent delayed
			// dispatches, and it happens on every run rather than occasionally.
			time.Advance(TimeSpan.FromMilliseconds(100));

			Assert.Equal(counts.Length, context.DrainAvailable());

			time.Advance(TimeSpan.FromMinutes(5));

			Assert.Equal(0, context.DrainAvailable());
			Assert.All(counts, c => Assert.Equal(1, c));
		}

		[Fact]
		public void DisposingTheTimerSuppressesATickAlreadyQueuedOnTheLoop()
		{
			// The dangerous ordering, and the reason Dispose sets its flag before tearing the timer
			// down. Ticks are POSTED to the main loop, so one can already be sitting in the queue
			// when the app disposes the timer; delivering it would raise Tick on a disposed object.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10) };

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;
			timer.Start();

			// Queue the tick, but do not pump it yet.
			time.Advance(TimeSpan.FromMilliseconds(10));

			timer.Dispose();

			// The callback is still in the queue and does get delivered - it must decline to run.
			context.DrainAvailable();

			Assert.Equal(0, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void StoppingTheTimerSuppressesATickAlreadyQueuedOnTheLoop()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10), IsRepeating = true };

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;
			timer.Start();

			time.Advance(TimeSpan.FromMilliseconds(10));
			timer.Stop();
			context.DrainAvailable();

			Assert.Equal(0, ticks);
		}

		[Fact]
		public void DisposingFromInsideTheTickHandlerStopsTheTimer()
		{
			// Self-disposal from the handler is ordinary usage - a timer that runs until some
			// condition - and it is why OnTimerTick re-reads its state after invoking Tick instead
			// of trusting what it read on entry.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10), IsRepeating = true };

			var ticks = 0;
			timer.Tick += (s, _) =>
			{
				ticks++;
				((TizenDispatcherTimer)s!).Dispose();
			};

			timer.Start();

			time.Advance(TimeSpan.FromMilliseconds(10));
			context.DrainAvailable();

			Assert.Equal(1, ticks);

			// However far the clock moves afterwards, a disposed timer is finished.
			time.Advance(TimeSpan.FromMinutes(5));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void StoppingFromInsideTheTickHandlerStopsARepeatingTimer()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10), IsRepeating = true };

			var ticks = 0;
			timer.Tick += (s, _) =>
			{
				ticks++;
				((TizenDispatcherTimer)s!).Stop();
			};

			timer.Start();
			time.Advance(TimeSpan.FromMilliseconds(10));
			context.DrainAvailable();

			time.Advance(TimeSpan.FromMinutes(1));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void RepeatingTimerTicksOncePerInterval()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10), IsRepeating = true };

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;
			timer.Start();

			time.Advance(TimeSpan.FromMilliseconds(50));
			context.DrainAvailable();

			Assert.Equal(5, ticks);
		}

		[Fact]
		public void NonRepeatingTimerTicksOnceHoweverFarTheClockMoves()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10) };

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;
			timer.Start();

			time.Advance(TimeSpan.FromMinutes(5));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void StartingADisposedTimerThrows()
		{
			var timer = new TizenDispatcherTimer(new LoopContext(), new ManualTimeProvider());
			timer.Dispose();

			Assert.Throws<ObjectDisposedException>(timer.Start);
		}

		[Fact]
		public void DisposingTwiceIsHarmless()
		{
			var timer = new TizenDispatcherTimer(new LoopContext(), new ManualTimeProvider());

			timer.Dispose();
			timer.Dispose();
		}

		[Fact]
		public void StoppingAfterDisposeDoesNotTouchTheDisposedTimer()
		{
			// Stop() on a disposed timer must not call Change() on it - a real System.Threading
			// timer throws ObjectDisposedException, and app teardown order routinely produces this.
			var timer = new TizenDispatcherTimer(new LoopContext(), new ManualTimeProvider())
			{
				Interval = TimeSpan.FromMilliseconds(10),
			};

			timer.Start();
			timer.Dispose();

			timer.Stop();
		}

		[Fact]
		public void ConcurrentDelayedDispatchesFromManyThreadsEachFireExactlyOnce()
		{
			// The concurrency the ManualTimeProvider cannot express on its own: many threads
			// arming delayed dispatches at once. The clock is still deterministic, so what varies
			// is only the interleaving of the Start calls, which is the part worth stressing.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var dispatcher = new TizenDispatcher(context, time);
			var counts = new int[64];

			Parallel.For(0, counts.Length, i =>
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(5), () => Interlocked.Increment(ref counts[i])));

			time.Advance(TimeSpan.FromMilliseconds(5));

			Assert.Equal(counts.Length, context.DrainAvailable());

			time.Advance(TimeSpan.FromMinutes(5));

			Assert.Equal(0, context.DrainAvailable());
			Assert.All(counts, c => Assert.Equal(1, c));
		}

		[Fact]
		public void RestartingFromInsideTheTickHandlerReArmsANonRepeatingTimer()
		{
			// A one-shot timer whose handler calls Start() again is the standard way to build a
			// self-scheduling loop with variable delay, and MAUI's animation code does exactly this.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			var timer = new TizenDispatcherTimer(context, time) { Interval = TimeSpan.FromMilliseconds(10) };

			var ticks = 0;
			timer.Tick += (s, _) =>
			{
				if (++ticks < 3)
					((TizenDispatcherTimer)s!).Start();
			};

			timer.Start();

			for (var i = 0; i < 3; i++)
			{
				time.Advance(TimeSpan.FromMilliseconds(10));
				context.DrainAvailable();
			}

			Assert.Equal(3, ticks);
		}

		[Fact]
		public void DelayedDispatchFiresOnceEvenIfTheUnderlyingTimerDoubleFires()
		{
			// The upstream race, reproduced deterministically. dotnet/maui's Tizen dispatcher armed
			// a REPEATING timer and relied on disposing it from inside its own callback to stop it,
			// so a second tick could be queued while Dispose was still in flight and the caller's
			// action ran twice. Because the action is posted to the main loop, that is a visible
			// double execution rather than a harmless extra tick.
			var context = new LoopContext();
			var time = new ManualTimeProvider { DoubleFireTimers = true };
			var dispatcher = new TizenDispatcher(context, time);

			var count = 0;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(10), () => Interlocked.Increment(ref count));

			time.Advance(TimeSpan.FromMilliseconds(10));

			Assert.Equal(1, context.DrainAvailable());
			Assert.Equal(1, Volatile.Read(ref count));
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
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			// However far the clock moves, an unstarted timer is armed with nothing.
			time.Advance(TimeSpan.FromMinutes(5));
			context.DrainAvailable();

			Assert.False(timer.IsRunning);
			Assert.Equal(0, ticks);
		}

		[Fact]
		public void NonRepeatingTimerTicksExactlyOnce()
		{
			// Was wall-clock based: arm a 5ms timer, pump for a fixed 400ms, assert exactly one
			// tick. A loaded CI runner delivered ZERO inside that window and the test failed
			// asserting 1 - a false red that says nothing about the dispatcher. Found by the
			// Blazor lane.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = false,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			Assert.True(timer.IsRunning);

			time.Advance(TimeSpan.FromMilliseconds(5));
			context.DrainAvailable();

			Assert.Equal(1, ticks);

			// Push the clock far past the interval: a one-shot must not fire again no matter how
			// much time passes. That is the "exactly once" half, and the fixed window could only
			// ever sample it.
			time.Advance(TimeSpan.FromMinutes(5));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void RepeatingTimerKeepsTicking()
		{
			// Same wall-clock hazard as NonRepeatingTimerTicksExactlyOnce, and not reported -
			// found while sweeping for others of its kind. `ticks > 1` after a fixed 400ms window
			// can observe 0 or 1 on a busy runner. Driving the clock also lets the count be
			// asserted EXACTLY rather than as an inequality that tolerates almost anything.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();

			// Five intervals, so exactly five ticks.
			time.Advance(TimeSpan.FromMilliseconds(25));
			context.DrainAvailable();

			Assert.Equal(5, ticks);

			timer.Stop();

			// And stopping really stops it.
			time.Advance(TimeSpan.FromMinutes(5));
			context.DrainAvailable();

			Assert.Equal(5, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void StoppedTimerDoesNotRaiseQueuedTicks()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(5),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();

			// Queue several ticks on the loop WITHOUT pumping, then stop. Driving the clock makes
			// "callbacks are already queued" a certainty rather than something a sleep hopes for.
			time.Advance(TimeSpan.FromMilliseconds(25));
			timer.Stop();

			// They are delivered now, and must all decline to run.
			context.DrainAvailable();

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

		[Fact]
		public void ATickQueuedBeforeStopIsRejectedAfterRestart()
		{
			// The ordering that IsRunning alone cannot catch. Ticks are POSTED to the main loop, so
			// one can sit in the queue across Stop and a subsequent Start. By the time it is
			// delivered IsRunning is true again, so it passes that check and raises a Tick
			// belonging to the previous run - and for a one-shot it then disarms the NEW run.
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(10),
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();

			// Queue a tick without pumping.
			time.Advance(TimeSpan.FromMilliseconds(10));

			timer.Stop();
			timer.Start();

			// The stale callback is delivered now; it belongs to the previous arming.
			context.DrainAvailable();

			Assert.Equal(0, ticks);

			// And the new run still works - the stale tick must not have consumed it.
			time.Advance(TimeSpan.FromMilliseconds(10));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
		}

		[Fact]
		public void AStaleTickCannotDisarmARepeatingTimer()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(10),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			time.Advance(TimeSpan.FromMilliseconds(10));
			timer.Stop();
			timer.Start();

			context.DrainAvailable();
			Assert.Equal(0, ticks);

			// Three fresh intervals under the new arming.
			time.Advance(TimeSpan.FromMilliseconds(30));
			context.DrainAvailable();

			Assert.Equal(3, ticks);
			Assert.True(timer.IsRunning);
		}

		[Fact]
		public void ATickQueuedBeforeAPlainStopStaysRejected()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider();
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(10),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			time.Advance(TimeSpan.FromMilliseconds(10));
			timer.Stop();

			context.DrainAvailable();

			Assert.Equal(0, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void AnUnderlyingCallbackHeldAcrossRestartCannotConsumeTheNewArming()
		{
			// The ordering a synchronous fake clock cannot produce, and the reason the generation
			// must be captured per-arming rather than read from a field.
			//
			// The underlying timer callback is held BEFORE it reaches the synchronization context,
			// then released after Stop and Start. If the arming identity were read when the
			// callback finally runs, the stale callback would see the NEW generation, be honoured,
			// and - for a one-shot - disarm the run that had just started.
			var context = new LoopContext();
			var time = new ManualTimeProvider { HoldCallbacks = true };
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(10),
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();

			// Fires the underlying callback, which is captured rather than delivered.
			time.Advance(TimeSpan.FromMilliseconds(10));

			timer.Stop();
			timer.Start();

			// Release the held callback only now: it belongs to the FIRST arming.
			time.HoldCallbacks = false;
			Assert.Equal(1, time.ReleaseHeldCallbacks());
			context.DrainAvailable();

			Assert.Equal(0, ticks);

			// And the second arming is untouched - the stale callback must not have consumed it.
			time.Advance(TimeSpan.FromMilliseconds(10));
			context.DrainAvailable();

			Assert.Equal(1, ticks);
			Assert.False(timer.IsRunning);
		}

		[Fact]
		public void AHeldCallbackCannotDisarmARestartedRepeatingTimer()
		{
			var context = new LoopContext();
			var time = new ManualTimeProvider { HoldCallbacks = true };
			using var timer = new TizenDispatcherTimer(context, time)
			{
				Interval = TimeSpan.FromMilliseconds(10),
				IsRepeating = true,
			};

			var ticks = 0;
			timer.Tick += (_, _) => ticks++;

			timer.Start();
			time.Advance(TimeSpan.FromMilliseconds(10));

			timer.Stop();
			timer.Start();

			time.HoldCallbacks = false;
			time.ReleaseHeldCallbacks();
			context.DrainAvailable();

			Assert.Equal(0, ticks);

			time.Advance(TimeSpan.FromMilliseconds(20));
			context.DrainAvailable();

			Assert.Equal(2, ticks);
			Assert.True(timer.IsRunning);
		}
	}
}
