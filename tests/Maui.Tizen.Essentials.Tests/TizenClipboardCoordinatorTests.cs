using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenClipboardCoordinatorTests
{
	[Fact]
	public async Task TextOperationsAndCallbacksStayOnTheDispatcher()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher);
		using var clipboard = new TizenClipboard(dispatcher, native);

		await clipboard.SetTextAsync("hello");
		var read = clipboard.GetTextAsync(TestContext.Current.CancellationToken);
		await native.WaitForReadAsync();
		await native.CompleteReadAsync(success: true, "hello");

		Assert.Equal("hello", await read);
		Assert.Empty(native.WrongThreadOperations);
	}

	[Fact]
	public async Task CancellationSettlesOnceAndIgnoresALateCallback()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher);
		using var clipboard = new TizenClipboard(dispatcher, native);
		using var cancellation = new CancellationTokenSource();

		var read = clipboard.GetTextAsync(cancellation.Token);
		await native.WaitForReadAsync();
		cancellation.Cancel();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
		Assert.Equal(cancellation.Token, exception.CancellationToken);

		await native.CompleteReadAsync(success: true, "late");
		Assert.True(read.IsCanceled);
		Assert.Empty(native.WrongThreadOperations);
	}

	[Fact]
	public async Task CancellationBeforeQueuedNativeReadSkipsTheRequest()
	{
		using var dispatcher = new StrictDispatcher(blockFirst: true);
		var native = new FakeClipboardNative(dispatcher);
		using var clipboard = new TizenClipboard(dispatcher, native);
		using var cancellation = new CancellationTokenSource();

		var read = clipboard.GetTextAsync(cancellation.Token);
		await dispatcher.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		cancellation.Cancel();
		dispatcher.Release();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
		Assert.Equal(0, native.ReadCalls);
	}

	[Fact]
	public async Task DisposalBeforeQueuedNativeReadSettlesCallerAndSkipsTheRequest()
	{
		using var dispatcher = new StrictDispatcher(blockFirst: true);
		var native = new FakeClipboardNative(dispatcher);
		var clipboard = new TizenClipboard(dispatcher, native);

		var read = clipboard.GetTextAsync(TestContext.Current.CancellationToken);
		await dispatcher.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		clipboard.Dispose();
		dispatcher.Release();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => read);
		Assert.Equal(0, native.ReadCalls);
	}

	[Fact]
	public async Task DisposalCancelsPendingReadAndUnsubscribesChangeEvent()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher);
		var clipboard = new TizenClipboard(dispatcher, native);
		var changed = 0;
		clipboard.ClipboardContentChanged += (_, _) => changed++;

		var read = clipboard.GetTextAsync(TestContext.Current.CancellationToken);
		await native.WaitForReadAsync();
		clipboard.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => read);
		await native.RaiseChangedAsync();
		Assert.Equal(0, changed);
		Assert.Equal(0, native.ChangeSubscriberCount);
		Assert.Equal(1, native.StopNotificationsCalls);
		Assert.Empty(native.WrongThreadOperations);
	}

	[Fact]
	public async Task ChangeNotificationIsPostedAndIgnoredAfterUnsubscribe()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher);
		using var clipboard = new TizenClipboard(dispatcher, native);
		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler<EventArgs> handler = (_, _) => changed.TrySetResult();

		clipboard.ClipboardContentChanged += handler;
		await native.RaiseChangedAsync();
		await changed.Task.WaitAsync(
			TimeSpan.FromMilliseconds(250),
			TestContext.Current.CancellationToken);
		clipboard.ClipboardContentChanged -= handler;

		Assert.Equal(0, native.ChangeSubscriberCount);
		Assert.Equal(1, native.StartNotificationsCalls);
		Assert.Equal(1, native.StopNotificationsCalls);
		Assert.Empty(native.WrongThreadOperations);
	}

	[Fact]
	public void FailedSecondarySelectionTransitionRollsBackSubscriber()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher) { FailStartNotifications = true };
		using var clipboard = new TizenClipboard(dispatcher, native);
		EventHandler<EventArgs> handler = static (_, _) => { };

		Assert.Throws<InvalidOperationException>(() =>
			clipboard.ClipboardContentChanged += handler);
		Assert.Equal(1, native.StartNotificationsCalls);
		Assert.Equal(1, native.StopNotificationsCalls);
		Assert.Equal(0, native.ChangeSubscriberCount);

		native.FailStartNotifications = false;
		clipboard.ClipboardContentChanged += handler;
		Assert.Equal(2, native.StartNotificationsCalls);
	}

	[Fact]
	public async Task SynchronousSecondarySelectionCallbackDoesNotDeadlockSubscription()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher) { RaiseOnStart = true };
		using var clipboard = new TizenClipboard(dispatcher, native);
		EventHandler<EventArgs> handler = static (_, _) => { };

		var subscribe = Task.Run(
			() => clipboard.ClipboardContentChanged += handler,
			TestContext.Current.CancellationToken);

		await subscribe.WaitAsync(TestContext.Current.CancellationToken);
		Assert.Equal(1, native.StartNotificationsCalls);
	}

	[Fact]
	public async Task QueuedOldNotificationDoesNotReachReplacementSubscriber()
	{
		using var dispatcher = new StrictDispatcher();
		var native = new FakeClipboardNative(dispatcher);
		using var clipboard = new TizenClipboard(dispatcher, native);
		var oldCalls = 0;
		var replacementCalls = 0;
		EventHandler<EventArgs> old = (_, _) => oldCalls++;
		EventHandler<EventArgs> replacement = (_, _) => replacementCalls++;

		clipboard.ClipboardContentChanged += old;
		var blocked = dispatcher.BlockNextDeferred();
		await native.RaiseChangedAsync();
		await blocked.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);
		clipboard.ClipboardContentChanged -= old;
		clipboard.ClipboardContentChanged += replacement;
		blocked.Release();
		await blocked.Drained.Task.WaitAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, oldCalls);
		Assert.Equal(0, replacementCalls);
	}

	sealed class StrictDispatcher : ITizenClipboardDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly ManualResetEventSlim _started = new();
		readonly ManualResetEventSlim _release = new();
		readonly Thread _thread;
		readonly bool _blockFirst;
		readonly object _deferredLock = new();
		BlockedDeferred? _nextDeferred;
		int _actions;

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

		public void Invoke(Action action) => InvokeAsync(action).GetAwaiter().GetResult();

		public Task InvokeAsync(Action action) =>
			InvokeAsync(() =>
			{
				action();
				return true;
			});

		public Task<T> InvokeAsync<T>(Func<T> action)
		{
			if (Thread.CurrentThread.ManagedThreadId == ThreadId)
				return Task.FromResult(action());

			var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			_work.Add(() =>
			{
				try
				{
					completion.TrySetResult(action());
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			});
			return completion.Task;
		}

		public void PostDeferred(Action action)
		{
			BlockedDeferred? blocked;
			lock (_deferredLock)
			{
				blocked = _nextDeferred;
				_nextDeferred = null;
			}

			if (blocked is not null)
			{
				blocked.SetRelease(() => _work.Add(() =>
				{
					action();
					blocked.Drained.TrySetResult();
				}));
				blocked.Queued.TrySetResult();
				return;
			}

			_work.Add(action);
		}

		public BlockedDeferred BlockNextDeferred()
		{
			lock (_deferredLock)
			{
				if (_nextDeferred is not null)
					throw new InvalidOperationException("A deferred callback is already blocked.");

				return _nextDeferred = new BlockedDeferred();
			}
		}

		public void Release() => _release.Set();

		public void Dispose()
		{
			_work.CompleteAdding();
			_release.Set();
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
				if (_blockFirst && Interlocked.Increment(ref _actions) == 1)
				{
					Queued.TrySetResult();
					_release.Wait();
				}
				action();
			}
		}

		public sealed class BlockedDeferred
		{
			Action? _release;

			public TaskCompletionSource Queued { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource Drained { get; } =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public void Release() =>
				Interlocked.Exchange(ref _release, null)?.Invoke();

			internal void SetRelease(Action release) => _release = release;
		}
	}

	sealed class FakeClipboardNative : ITizenClipboardNative
	{
		readonly StrictDispatcher _dispatcher;
		readonly TaskCompletionSource _readRequested =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		Action<bool, string?>? _readCallback;
		Action? _dataSelected;

		public FakeClipboardNative(StrictDispatcher dispatcher)
		{
			_dispatcher = dispatcher;
		}

		public ConcurrentQueue<string> WrongThreadOperations { get; } = [];

		public int ReadCalls { get; private set; }

		public int ChangeSubscriberCount => _dataSelected?.GetInvocationList().Length ?? 0;

		public int StartNotificationsCalls { get; private set; }

		public int StopNotificationsCalls { get; private set; }

		public bool FailStartNotifications { get; set; }

		public bool RaiseOnStart { get; set; }

		public void StartChangeNotifications(Action changed)
		{
			AssertThread("start notifications");
			StartNotificationsCalls++;
			_dataSelected = changed;
			if (RaiseOnStart)
				changed();
			if (FailStartNotifications)
			{
				_dataSelected = null;
				StopNotificationsCalls++;
				throw new InvalidOperationException("secondary selection failed");
			}
		}

		public void StopChangeNotifications()
		{
			AssertThread("stop notifications");
			StopNotificationsCalls++;
			_dataSelected = null;
		}

		public bool SetText(string text)
		{
			AssertThread(nameof(SetText));
			return true;
		}

		public void GetText(Action<bool, string?> callback)
		{
			AssertThread(nameof(GetText));
			ReadCalls++;
			_readCallback = callback;
			_readRequested.TrySetResult();
		}

		public Task WaitForReadAsync() =>
			_readRequested.Task.WaitAsync(TestContext.Current.CancellationToken);

		public Task CompleteReadAsync(bool success, string? text) =>
			_dispatcher.InvokeAsync(() => _readCallback?.Invoke(success, text));

		public Task RaiseChangedAsync() =>
			_dispatcher.InvokeAsync(() => _dataSelected?.Invoke());

		void AssertThread(string operation)
		{
			if (Thread.CurrentThread.ManagedThreadId != _dispatcher.ThreadId)
				WrongThreadOperations.Enqueue(operation);
		}
	}
}
