using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	public class TizenBlazorDispatcherTests
	{
		[Fact]
		public async Task OperationCaptureWaitsForNestedAsyncDispatcherWork()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var started = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var release = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var capture = dispatcher.BeginOperationCapture();
			var operation = dispatcher.InvokeAsync(async () =>
			{
				started.TrySetResult(null);
				await release.Task;
			});
			capture.Dispose();
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var drain = capture.DrainAsync();
			Assert.False(drain.IsCompleted);
			release.TrySetResult(null);

			await Task.WhenAll(operation, drain).WaitAsync(TimeSpan.FromSeconds(10));
		}

		[Fact]
		public async Task OperationCapturePropagatesNestedDispatcherFailure()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var expected = new InvalidOperationException("ipc failed");
			var capture = dispatcher.BeginOperationCapture();
			_ = dispatcher.InvokeAsync(() => Task.FromException(expected));
			capture.Dispose();

			var failure = await Assert.ThrowsAsync<InvalidOperationException>(
				() => capture.DrainAsync());

			Assert.Same(expected, failure);
		}

		[Fact]
		public async Task FaultDoesNotStopDrainBeforeLaterCapturedWorkCompletes()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var expected = new InvalidOperationException("outer ipc failed");
			var nestedStarted = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseNested = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var capture = dispatcher.BeginOperationCapture();
			_ = dispatcher.InvokeAsync(async () =>
			{
				await Task.Yield();
				_ = dispatcher.InvokeAsync(async () =>
				{
					nestedStarted.TrySetResult(null);
					await releaseNested.Task;
				});
				throw expected;
			});
			capture.Dispose();
			await nestedStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var drain = capture.DrainAsync();
			Assert.False(drain.IsCompleted);
			releaseNested.TrySetResult(null);

			var failure = await Assert.ThrowsAsync<InvalidOperationException>(
				() => drain);
			Assert.Same(expected, failure);
		}

		[Fact]
		public async Task DelayedCapturedContextRunsWhileTheConnectionIsActive()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var capture = dispatcher.BeginOperationCapture();
			var capturedContext = ExecutionContext.Capture();
			capture.Dispose();
			await capture.DrainAsync();
			var invoked = false;
			Task? lateOperation = null;

			ExecutionContext.Run(
				capturedContext!,
				_ =>
				{
					lateOperation = dispatcher.InvokeAsync(() =>
					{
						invoked = true;
						return Task.CompletedTask;
					});
				},
				null);

			Assert.NotNull(lateOperation);
			await lateOperation!;
			Assert.True(invoked);
		}

		[Fact]
		public async Task DelayedCapturedContextIsRejectedAfterTheConnectionRetires()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var capture = dispatcher.BeginOperationCapture();
			var capturedContext = ExecutionContext.Capture();
			capture.Dispose();
			await capture.DrainAsync();
			await dispatcher.RetireAsync();
			var invoked = false;
			Task? lateOperation = null;

			ExecutionContext.Run(
				capturedContext!,
				_ =>
				{
					lateOperation = dispatcher.InvokeAsync(() =>
					{
						invoked = true;
						return Task.CompletedTask;
					});
				},
				null);

			Assert.NotNull(lateOperation);
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lateOperation!);
			Assert.False(invoked);
		}

		[Fact]
		public async Task RetirementWaitsForAnAcceptedDelayedCompletion()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var capture = dispatcher.BeginOperationCapture();
			var capturedContext = ExecutionContext.Capture();
			capture.Dispose();
			await capture.DrainAsync();
			var started = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var release = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			Task? delayed = null;

			ExecutionContext.Run(
				capturedContext!,
				_ =>
				{
					delayed = dispatcher.InvokeAsync(async () =>
					{
						started.TrySetResult(null);
						await release.Task;
					});
				},
				null);
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var retirement = dispatcher.RetireAsync();
			Assert.False(retirement.IsCompleted);
			release.TrySetResult(null);

			await Task.WhenAll(delayed!, retirement).WaitAsync(TimeSpan.FromSeconds(10));
		}

		[Fact]
		public async Task TrustedCleanupAuthorityExpiresForDelayedDescendants()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			var capture = dispatcher.BeginOperationCapture();
			var capturedContext = ExecutionContext.Capture();
			capture.Dispose();
			await capture.DrainAsync();
			await dispatcher.RetireAsync();
			var invoked = false;
			Task? cleanup = null;
			ExecutionContext? delayedContext = null;

			ExecutionContext.Run(
				capturedContext!,
				_ =>
				{
					using (dispatcher.SuppressOperationCapture())
					{
						cleanup = dispatcher.InvokeAsync(() =>
						{
							invoked = true;
							return Task.CompletedTask;
						});
						delayedContext = ExecutionContext.Capture();
					}
				},
				null);

			Assert.NotNull(cleanup);
			await cleanup!;
			Assert.True(invoked);

			var delayedInvoked = false;
			Task? delayed = null;
			ExecutionContext.Run(
				delayedContext!,
				_ =>
				{
					delayed = dispatcher.InvokeAsync(() =>
					{
						delayedInvoked = true;
						return Task.CompletedTask;
					});
				},
				null);

			Assert.NotNull(delayed);
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed!);
			Assert.False(delayedInvoked);
		}

		[Fact]
		public async Task ExpiredCleanupAuthorityIsRejectedWithoutAnOperationCapture()
		{
			var dispatcher = new TizenBlazorDispatcher(new InlineDispatcher());
			await dispatcher.RetireAsync();
			var cleanupInvoked = false;
			ExecutionContext? delayedContext;

			using (dispatcher.SuppressOperationCapture())
			{
				await dispatcher.InvokeAsync(() =>
				{
					cleanupInvoked = true;
					return Task.CompletedTask;
				});
				delayedContext = ExecutionContext.Capture();
			}

			Assert.True(cleanupInvoked);
			var delayedInvoked = false;
			Task? delayed = null;
			ExecutionContext.Run(
				delayedContext!,
				_ =>
				{
					delayed = dispatcher.InvokeAsync(() =>
					{
						delayedInvoked = true;
						return Task.CompletedTask;
					});
				},
				null);

			Assert.NotNull(delayed);
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed!);
			Assert.False(delayedInvoked);
		}

		private sealed class InlineDispatcher : IDispatcher
		{
			public bool IsDispatchRequired => false;

			public bool Dispatch(Action action)
			{
				action();
				return true;
			}

			public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

			public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
		}
	}
}
