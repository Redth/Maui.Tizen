using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenEventSubscriptionCoordinatorTests
{
	[Fact]
	public void NativeStartRunsWithoutHoldingTheStateLock()
	{
		using var dispatcher = new StrictCallbackDispatcher();
		TizenEventSubscriptionCoordinator<EventArgs>? coordinator = null;
		var started = false;
		coordinator = new(
			this,
			publish =>
			{
				publish(EventArgs.Empty);
				started = true;
				return static () => { };
			},
			new TizenNativeCallbackCoordinator(dispatcher));

		coordinator.Add(static (_, _) => { });

		Assert.True(started);
	}

	[Fact]
	public async Task StaleQueuedEventNeverReachesReplacementSubscriber()
	{
		using var dispatcher = new StrictCallbackDispatcher(blockFirst: true);
		var oldCalls = 0;
		var replacementCalls = 0;
		Action<EventArgs>? currentNative = null;
		var retained = new List<Action<EventArgs>>();
		EventHandler<EventArgs> old = (_, _) => oldCalls++;
		var replacementDelivered = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler<EventArgs> replacement = (_, _) =>
		{
			replacementCalls++;
			replacementDelivered.TrySetResult();
		};
		var coordinator = new TizenEventSubscriptionCoordinator<EventArgs>(
			this,
			publish =>
			{
				currentNative = publish;
				retained.Add(publish);
				return () => currentNative = null;
			},
			new TizenNativeCallbackCoordinator(dispatcher));

		coordinator.Add(old);
		var oldNative = Assert.Single(retained);
		oldNative(EventArgs.Empty);
		await dispatcher.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		coordinator.Remove(old);
		coordinator.Add(replacement);
		Assert.NotSame(oldNative, currentNative);
		oldNative(EventArgs.Empty);
		dispatcher.Release();
		await dispatcher.Drained.Task.WaitAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, oldCalls);
		Assert.Equal(0, replacementCalls);

		currentNative!(EventArgs.Empty);
		await replacementDelivered.Task.WaitAsync(TestContext.Current.CancellationToken);
		Assert.Equal(1, replacementCalls);
	}

	[Fact]
	public void FailedFirstSubscriptionRollsBackAndCanBeRetried()
	{
		using var dispatcher = new StrictCallbackDispatcher();
		var native = false;
		var fail = true;
		var coordinator = new TizenEventSubscriptionCoordinator<EventArgs>(
			this,
			publish =>
			{
				try
				{
					native = true;
					if (fail)
						throw new InvalidOperationException("subscribe failed");
					return () => native = false;
				}
				catch
				{
					native = false;
					throw;
				}
			},
			new TizenNativeCallbackCoordinator(dispatcher));
		EventHandler<EventArgs> handler = static (_, _) => { };

		Assert.Throws<InvalidOperationException>(() => coordinator.Add(handler));
		Assert.False(native);

		fail = false;
		coordinator.Add(handler);
		Assert.True(native);
	}

	[Fact]
	public async Task DisposeInvalidatesAQueuedEventAndStopsNativeListening()
	{
		using var dispatcher = new StrictCallbackDispatcher(blockFirst: true);
		var native = false;
		var calls = 0;
		Action<EventArgs>? nativeCallback = null;
		var coordinator = new TizenEventSubscriptionCoordinator<EventArgs>(
			this,
			publish =>
			{
				native = true;
				nativeCallback = publish;
				return () => native = false;
			},
			new TizenNativeCallbackCoordinator(dispatcher));
		coordinator.Add((_, _) => calls++);
		coordinator.Add((_, _) => calls++);
		nativeCallback!(EventArgs.Empty);
		await dispatcher.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		coordinator.Dispose();
		dispatcher.Release();
		await dispatcher.Drained.Task.WaitAsync(TestContext.Current.CancellationToken);

		Assert.False(native);
		Assert.Equal(0, calls);
	}

	sealed class StrictCallbackDispatcher : ITizenNativeCallbackDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly ManualResetEventSlim _release = new();
		readonly Thread _thread;
		readonly bool _blockFirst;

		public StrictCallbackDispatcher(bool blockFirst = false)
		{
			_blockFirst = blockFirst;
			_thread = new Thread(Run) { IsBackground = true };
			_thread.Start();
		}

		public TaskCompletionSource Queued { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Drained { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void PostDeferred(Action action) => _work.Add(action);

		public void Release() => _release.Set();

		public void Dispose()
		{
			_release.Set();
			_work.CompleteAdding();
			_thread.Join();
			_release.Dispose();
			_work.Dispose();
		}

		void Run()
		{
			foreach (var action in _work.GetConsumingEnumerable())
			{
				Queued.TrySetResult();
				if (_blockFirst)
					_release.Wait();
				action();
				Drained.TrySetResult();
			}
		}
	}
}
