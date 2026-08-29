using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenNativeCallbackCoordinatorTests
{
	[Fact]
	public async Task CallbackIsAsynchronouslyPostedToTheDispatcherThread()
	{
		using var dispatcher = new StrictDispatcher();
		var coordinator = new TizenNativeCallbackCoordinator(dispatcher);
		var callbackThread = new TaskCompletionSource<int>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var returned = false;

		coordinator.Post(
			static () => true,
			() => callbackThread.TrySetResult(Thread.CurrentThread.ManagedThreadId));
		returned = true;

		Assert.True(returned);
		Assert.Equal(
			dispatcher.ThreadId,
			await callbackThread.Task.WaitAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task QueuedCallbackRechecksGenerationOrSubscriptionState()
	{
		using var dispatcher = new StrictDispatcher(blockFirst: true);
		var coordinator = new TizenNativeCallbackCoordinator(dispatcher);
		var current = true;
		var called = false;

		coordinator.Post(() => current, () => called = true);
		await dispatcher.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		current = false;
		dispatcher.Release();
		await dispatcher.Drained.Task.WaitAsync(TestContext.Current.CancellationToken);

		Assert.False(called);
	}

	[Fact]
	public void ProductionDispatcherAddsASecondHopWhenMainThreadDispatchIsInline()
	{
		var context = new QueuedSynchronizationContext();
		var called = false;

		TizenNativeCallbackDispatcher.PostDeferred(
			beginInvoke: callback => callback(),
			getContext: () => context,
			action: () => called = true);

		Assert.False(called);
		Assert.Equal(1, context.Pending);
		context.RunOne();
		Assert.True(called);
	}

	sealed class StrictDispatcher : ITizenNativeCallbackDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly ManualResetEventSlim _started = new();
		readonly ManualResetEventSlim _release = new();
		readonly Thread _thread;
		readonly bool _blockFirst;

		public StrictDispatcher(bool blockFirst = false)
		{
			_blockFirst = blockFirst;
			_thread = new Thread(Run) { IsBackground = true };
			_thread.Start();
			_started.Wait();
		}

		public int ThreadId { get; private set; }

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
			_started.Dispose();
			_work.Dispose();
		}

		void Run()
		{
			ThreadId = Thread.CurrentThread.ManagedThreadId;
			_started.Set();
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

	sealed class QueuedSynchronizationContext : SynchronizationContext
	{
		readonly Queue<(SendOrPostCallback Callback, object? State)> _work = [];

		public int Pending => _work.Count;

		public override void Post(SendOrPostCallback d, object? state) =>
			_work.Enqueue((d, state));

		public void RunOne()
		{
			var (callback, state) = _work.Dequeue();
			callback(state);
		}
	}
}
