using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;
using TizenPixelFormat = Tizen.NUI.PixelFormat;

namespace Maui.Tizen.Essentials.Tests;

public class TizenScreenshotCoordinatorTests
{
	[Fact]
	public async Task EveryCaptureOperationRunsOnTheCapturedDispatcher()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher)
		{
			Buffer = new([1, 2, 3, 4], 1, 1, 4, TizenPixelFormat.RGBA8888),
		};
		using var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());

		var capture = screenshot.CaptureCoreAsync(
			() =>
			{
				session.AssertThread("create");
				return session;
			},
			TestContext.Current.CancellationToken);
		await session.Started.Task;
		await session.CompleteAsync(success: true);
		var result = await capture;

		Assert.NotNull(result);
		Assert.Equal(
			["create", "subscribe", "start", "copy", "unsubscribe", "dispose"],
			session.Operations);
		Assert.Empty(session.WrongThreadOperations);
	}

	[Fact]
	public async Task FirstTerminalSignalWinsExactlyOnce()
	{
		var terminal = new TizenScreenshotTerminalCoordinator();

		Assert.True(terminal.TryComplete(TizenScreenshotTerminal.NativeSucceeded));
		Assert.False(terminal.TryComplete(TizenScreenshotTerminal.TimedOut));
		Assert.False(terminal.TryComplete(TizenScreenshotTerminal.Canceled));
		Assert.Equal(
			TizenScreenshotTerminal.NativeSucceeded,
			await terminal.Completion);
	}

	[Fact]
	public async Task CancellationDisposesOnDispatcherAndIgnoresLateCompletion()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher);
		using var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());
		using var cancellation = new CancellationTokenSource();

		var capture = screenshot.CaptureCoreAsync(
			() => session,
			cancellation.Token);
		await session.Started.Task;
		cancellation.Cancel();

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
		Assert.Equal(cancellation.Token, exception.CancellationToken);
		await session.Disposed.Task;
		await session.CompleteAsync(success: true);
		Assert.True(capture.IsCanceled);
		Assert.Empty(session.WrongThreadOperations);
	}

	[Fact]
	public async Task TimeoutDisposesAndLateCallbackCannotResettle()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher);
		using var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());

		var capture = screenshot.CaptureCoreAsync(
			() => session,
			CancellationToken.None,
			TimeSpan.FromMilliseconds(25));
		await session.Started.Task;

		await Assert.ThrowsAsync<TimeoutException>(() => capture);
		await session.Disposed.Task;
		await session.CompleteAsync(success: true);
		Assert.True(capture.IsFaulted);
		Assert.Empty(session.WrongThreadOperations);
	}

	[Fact]
	public async Task SuccessfulCompletionAtDeadlineOwnsTerminalWhileBufferCopyRuns()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher)
		{
			Buffer = new([1, 2, 3, 4], 1, 1, 4, TizenPixelFormat.RGBA8888),
			BlockCopy = true,
		};
		using var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());

		var capture = screenshot.CaptureCoreAsync(
			() => session,
			CancellationToken.None,
			TimeSpan.FromMilliseconds(40));
		await session.Started.Task;
		await session.CompleteAsync(success: true);
		await session.CopyEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
		await Task.Delay(80, TestContext.Current.CancellationToken);
		session.ReleaseCopy();

		Assert.NotNull(await capture);
		Assert.Equal(1, session.CopyCalls);
		Assert.Empty(session.WrongThreadOperations);
	}

	[Fact]
	public async Task DuplicateAndLateNativeCompletionCannotResettle()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher)
		{
			Buffer = new([1, 2, 3, 4], 1, 1, 4, TizenPixelFormat.RGBA8888),
		};
		using var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());

		var capture = screenshot.CaptureCoreAsync(
			() => session,
			TestContext.Current.CancellationToken);
		await session.Started.Task;
		await session.CompleteAsync(success: true);
		await session.CompleteAsync(success: false);

		Assert.NotNull(await capture);
		Assert.Equal(1, session.CopyCalls);
	}

	[Fact]
	public async Task DisposalWinsBeforeNativeCompletionAndCleansUpOnDispatcher()
	{
		using var dispatcher = new StrictScreenshotDispatcher();
		var session = new FakeCaptureSession(dispatcher);
		var screenshot = new TizenScreenshot(dispatcher, new UnusedFactory());

		var capture = screenshot.CaptureCoreAsync(
			() => session,
			TestContext.Current.CancellationToken);
		await session.Started.Task;
		screenshot.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => capture);
		await session.Disposed.Task;
		await session.CompleteAsync(success: true);
		Assert.Empty(session.WrongThreadOperations);
	}

	[Fact]
	public void CopiesPaddedPixelRowsWithoutIncludingStrideBytes()
	{
		byte[] padded =
		[
			1, 2, 3, 4, 0xEE, 0xEE, 0xEE, 0xEE,
			5, 6, 7, 8, 0xDD, 0xDD, 0xDD, 0xDD,
		];

		var captured = TizenScreenshotResult.CopyRows(
			padded,
			width: 1,
			height: 2,
			stride: 8,
			format: TizenPixelFormat.RGBA8888);

		Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], captured.Pixels);
	}

	[Fact]
	public void TreatsZeroStrideAsTightlyPacked()
	{
		var captured = TizenScreenshotResult.CopyRows(
			[1, 2, 3, 4, 5, 6, 7, 8],
			width: 1,
			height: 2,
			stride: 0,
			format: TizenPixelFormat.RGBA8888);

		Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], captured.Pixels);
	}

	sealed class StrictScreenshotDispatcher : ITizenScreenshotDispatcher, IDisposable
	{
		readonly BlockingCollection<Action> _work = [];
		readonly ManualResetEventSlim _started = new();
		readonly Thread _thread;

		public StrictScreenshotDispatcher()
		{
			_thread = new Thread(Run) { IsBackground = true };
			_thread.Start();
			_started.Wait();
		}

		public int ThreadId { get; private set; }

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

		public void Dispose()
		{
			_work.CompleteAdding();
			_thread.Join();
			_started.Dispose();
			_work.Dispose();
		}

		void Run()
		{
			ThreadId = Thread.CurrentThread.ManagedThreadId;
			_started.Set();
			foreach (var action in _work.GetConsumingEnumerable())
				action();
		}
	}

	sealed class FakeCaptureSession : ITizenScreenshotCaptureSession
	{
		readonly StrictScreenshotDispatcher _dispatcher;
		Action<bool>? _finished;
		readonly ManualResetEventSlim _releaseCopy = new();

		public FakeCaptureSession(StrictScreenshotDispatcher dispatcher)
		{
			_dispatcher = dispatcher;
		}

		public TizenCapturedBuffer? Buffer { get; init; }

		public bool BlockCopy { get; init; }

		public int CopyCalls { get; private set; }

		public ConcurrentQueue<string> WrongThreadOperations { get; } = [];

		public List<string> Operations { get; } = [];

		public TaskCompletionSource Started { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Disposed { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource CopyEntered { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public event Action<bool>? Finished
		{
			add
			{
				AssertThread("subscribe");
				_finished += value;
			}
			remove
			{
				AssertThread("unsubscribe");
				_finished -= value;
			}
		}

		public void Start()
		{
			AssertThread("start");
			Started.TrySetResult();
		}

		public TizenCapturedBuffer? CopyBuffer()
		{
			AssertThread("copy");
			CopyCalls++;
			CopyEntered.TrySetResult();
			if (BlockCopy)
				_releaseCopy.Wait(TestContext.Current.CancellationToken);
			return Buffer;
		}

		public void Dispose()
		{
			AssertThread("dispose");
			_releaseCopy.Set();
			_releaseCopy.Dispose();
			Disposed.TrySetResult();
		}

		public Task CompleteAsync(bool success) =>
			_dispatcher.InvokeAsync(() => _finished?.Invoke(success));

		public void ReleaseCopy() => _releaseCopy.Set();

		public void AssertThread(string operation)
		{
			Operations.Add(operation);
			if (Thread.CurrentThread.ManagedThreadId != _dispatcher.ThreadId)
				WrongThreadOperations.Enqueue(operation);
		}
	}

	sealed class UnusedFactory : ITizenScreenshotCaptureFactory
	{
		public ITizenScreenshotCaptureSession CreateDefaultWindowCapture() =>
			throw new NotSupportedException();

		public ITizenScreenshotCaptureSession CreateWindowCapture(global::Tizen.NUI.Window window) =>
			throw new NotSupportedException();

		public ITizenScreenshotCaptureSession CreateViewCapture(global::Tizen.NUI.BaseComponents.View view) =>
			throw new NotSupportedException();
	}
}
