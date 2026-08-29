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
		await changed.Task.WaitAsync(TestContext.Current.CancellationToken);
		clipboard.ClipboardContentChanged -= handler;

		Assert.Equal(0, native.ChangeSubscriberCount);
		Assert.Empty(native.WrongThreadOperations);
	}

	sealed class StrictDispatcher : ITizenClipboardDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly ManualResetEventSlim _started = new();
		readonly ManualResetEventSlim _release = new();
		readonly Thread _thread;
		readonly bool _blockFirst;
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

		public void Post(Action action) => _work.Add(action);

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

		public event Action? DataSelected
		{
			add
			{
				AssertThread("subscribe");
				_dataSelected += value;
			}
			remove
			{
				AssertThread("unsubscribe");
				_dataSelected -= value;
			}
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
