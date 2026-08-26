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
