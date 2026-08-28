// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Regressions for the asynchronous image load lifecycle.
	/// </summary>
	/// <remarks>
	/// Every case here corresponds to a way the previous implementation - a bare
	/// <c>await ... ConfigureAwait(false)</c> followed by an unconditional apply - produced a wrong
	/// result or a leak. None of them are reachable by inspection alone; they only appear when a
	/// source changes or a handler is torn down while a load is in flight.
	/// </remarks>
	public class ImageLoaderTests
	{
		sealed class FakeImage
		{
			public string Name { get; init; } = string.Empty;

			public bool IsDisposed { get; set; }
		}

		sealed class FakeResult : IImageSourceServiceResult<FakeImage>
		{
			readonly FakeImage _value;

			public FakeResult(FakeImage value) => _value = value;

			public FakeImage Value
			{
				get
				{
					ValueAccessCount++;

					if (IsDisposed)
						throw new ObjectDisposedException(nameof(FakeResult));

					return _value;
				}
			}

			public bool IsResolutionDependent => false;

			public int DisposeCount { get; private set; }

			public int ValueAccessCount { get; private set; }

			public bool IsDisposed => DisposeCount != 0;

			public void Dispose()
			{
				DisposeCount++;
				_value.IsDisposed = true;
			}
		}

		sealed class FakeSource : IImageSource
		{
			public bool IsEmpty => false;
		}

		sealed class QueuedCommit
		{
			readonly List<(Action Action, TaskCompletionSource Completion)> _pending = new();

			public int Count => _pending.Count;

			public Task Enqueue(Action action)
			{
				var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				_pending.Add((action, completion));
				return completion.Task;
			}

			public void RunAt(int index)
			{
				var pending = _pending[index];
				_pending.RemoveAt(index);

				try
				{
					pending.Action();
					pending.Completion.SetResult();
				}
				catch (Exception exception)
				{
					pending.Completion.SetException(exception);
				}
			}
		}

		static Task RunInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		static Task LoadAsync(
			TizenImageLoader<FakeImage> loader,
			IImageSource? source,
			Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<FakeImage>?>> load,
			Action<FakeImage?> apply,
			Func<bool> isTargetCurrent) =>
			loader.LoadAsync(
				source,
				load,
				RunInline,
				apply,
				static () => true,
				isTargetCurrent);

		[Fact]
		public async Task AppliesTheLoadedImage()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			FakeImage? applied = null;

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "a" })),
				image => applied = image,
				static () => true);

			Assert.Equal("a", applied?.Name);
		}

		/// <summary>
		/// A null source clears rather than leaving the previous image.
		/// </summary>
		[Fact]
		public async Task NullSourceClearsTheImage()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			FakeImage? applied = null;
			var applyCount = 0;

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "a" })),
				image => { applied = image; applyCount++; },
				static () => true);

			await LoadAsync(
				loader,
				source: null,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(null),
				image => { applied = image; applyCount++; },
				static () => true);

			Assert.Null(applied);
			Assert.Equal(2, applyCount);
		}

		/// <summary>
		/// A load that resolves successfully but yields no image clears rather than leaving the
		/// previous one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Distinct from both <see cref="NullSourceClearsTheImage"/> and
		/// <see cref="AFailedLoadClearsTheImage"/>: here the source is non-null and the service does
		/// not throw, it simply returns no result - which an image-source service is allowed to do
		/// for a source it cannot resolve. Nothing about "the call succeeded" implies an image
		/// exists.
		/// </para>
		/// <para>
		/// Without this the previous image stays on screen under a new, unresolvable source, which
		/// reads as the background silently failing to change.
		/// </para>
		/// </remarks>
		[Fact]
		public async Task ALoadResolvingToNoImageClearsThePrevious()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			FakeImage? applied = null;

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "a" })),
				image => applied = image,
				static () => true);

			Assert.NotNull(applied);

			// Non-null source, no exception, no result.
			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(null),
				image => applied = image,
				static () => true);

			Assert.True(
				applied is null,
				"A load that resolved to no image left the previous image applied. A successful " +
				"call that yields nothing must clear, exactly as a failed one does.");
		}

		/// <summary>
		/// A slower earlier load must not overwrite a newer one.
		/// </summary>
		/// <remarks>
		/// The defect this pins: with no supersession check, whichever load finishes last wins.
		/// Set a source, change it before the first resolves, and the view ends up showing the
		/// image you navigated away from.
		/// </remarks>
		[Fact]
		public async Task ASupersededLoadDoesNotApply()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var applied = new List<string?>();

			var firstStarted = new TaskCompletionSource();
			var releaseFirst = new TaskCompletionSource();

			var first = LoadAsync(
				loader,
				new FakeSource(),
				async (_, token) =>
				{
					firstStarted.SetResult();
					await releaseFirst.Task;
					token.ThrowIfCancellationRequested();
					return new FakeResult(new FakeImage { Name = "slow" });
				},
				image => applied.Add(image?.Name),
				static () => true);

			await firstStarted.Task;

			// Supersede while the first is still in flight.
			var second = LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "fast" })),
				image => applied.Add(image?.Name),
				static () => true);

			await second;
			releaseFirst.SetResult();
			await first;

			Assert.Equal(["fast"], applied);
			Assert.Equal("fast", loader.Current?.Name);
		}

		/// <summary>
		/// A load that completes after the view changed must not apply.
		/// </summary>
		[Fact]
		public async Task ALoadForAReplacedViewDoesNotApply()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var applied = false;
			var result = new FakeResult(new FakeImage { Name = "a" });

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result),
				_ => applied = true,
				// The handler was reconnected to a different platform view mid-load.
				static () => false);

			Assert.False(applied);
			Assert.True(result.IsDisposed, "The unapplied result owns a native handle and must be disposed.");
		}

		/// <summary>
		/// A failed load clears the image instead of leaving the previous one.
		/// </summary>
		[Fact]
		public async Task AFailedLoadClearsTheImage()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			FakeImage? applied = new() { Name = "stale" };

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromException<IImageSourceServiceResult<FakeImage>?>(new InvalidOperationException("boom")),
				image => applied = image,
				static () => true);

			Assert.Null(applied);
		}

		/// <summary>
		/// Replacing an image disposes the one it replaced.
		/// </summary>
		/// <remarks>
		/// The service result owns a NUI image buffer. Without this, every source change leaks one.
		/// </remarks>
		[Fact]
		public async Task ReplacingAnImageDisposesThePrevious()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var first = new FakeResult(new FakeImage { Name = "first" });
			var second = new FakeResult(new FakeImage { Name = "second" });

			await LoadAsync(loader, new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(first), _ => { }, static () => true);
			Assert.False(first.IsDisposed);

			await LoadAsync(loader, new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(second), _ => { }, static () => true);

			Assert.True(first.IsDisposed, "Replacing the image must dispose the result it replaced.");
			Assert.False(second.IsDisposed);
		}

		[Fact]
		public async Task ReapplyingTheSameResultKeepsItAliveAndDisposesItOnce()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var result = new FakeResult(new FakeImage { Name = "shared" });

			await LoadAsync(loader, new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result), _ => { }, static () => true);
			await LoadAsync(loader, new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result), _ => { }, static () => true);

			Assert.Equal(0, result.DisposeCount);

			loader.Dispose();

			Assert.Equal(1, result.DisposeCount);
		}

		/// <summary>
		/// Disposing the loader releases the applied image.
		/// </summary>
		/// <remarks>
		/// This is the handler's disconnect path. Without it a torn-down handler leaks its image.
		/// </remarks>
		[Fact]
		public async Task DisposingReleasesTheAppliedImage()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var result = new FakeResult(new FakeImage { Name = "a" });

			await LoadAsync(loader, new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result), _ => { }, static () => true);

			loader.Dispose();

			Assert.True(result.IsDisposed);
			Assert.Null(loader.Current);
		}

		/// <summary>
		/// A load started before disposal must not apply afterwards.
		/// </summary>
		[Fact]
		public async Task ALoadCompletingAfterDisposalDoesNotApply()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var applied = false;
			var release = new TaskCompletionSource();
			var started = new TaskCompletionSource();
			var result = new FakeResult(new FakeImage { Name = "a" });

			var load = LoadAsync(
				loader,
				new FakeSource(),
				async (_, _) =>
				{
					started.SetResult();
					await release.Task;
					return result;
				},
				_ => applied = true,
				static () => true);

			await started.Task;
			loader.Dispose();
			release.SetResult();
			await load;

			Assert.False(applied);
			Assert.True(result.IsDisposed, "A result arriving after disposal still owns a handle and must be disposed.");
		}

		/// <summary>
		/// Cancellation is not treated as a failure, so it does not clear a newer image.
		/// </summary>
		[Fact]
		public async Task ACancelledLoadDoesNotClearTheCurrentImage()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var applied = new List<string?>();

			var started = new TaskCompletionSource();
			var release = new TaskCompletionSource();

			var slow = LoadAsync(
				loader,
				new FakeSource(),
				async (_, token) =>
				{
					started.SetResult();
					await release.Task;
					token.ThrowIfCancellationRequested();
					return new FakeResult(new FakeImage { Name = "slow" });
				},
				image => applied.Add(image?.Name),
				static () => true);

			await started.Task;

			await LoadAsync(
				loader,
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "current" })),
				image => applied.Add(image?.Name),
				static () => true);

			release.SetResult();
			await slow;

			// The cancelled load must not have appended a clearing null after "current".
			Assert.Equal(["current"], applied);
		}

		/// <summary>
		/// A commit that was queued first but executes after a newer commit cannot win.
		/// </summary>
		[Fact]
		public async Task QueuedOlderCommitCannotOverwriteOrUseDisposedResult()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var commits = new QueuedCommit();
			var firstSource = new FakeSource();
			var secondSource = new FakeSource();
			IImageSource currentSource = firstSource;
			var firstResult = new FakeResult(new FakeImage { Name = "old" });
			var secondResult = new FakeResult(new FakeImage { Name = "new" });
			var applied = new List<string?>();

			var first = loader.LoadAsync(
				firstSource,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(firstResult),
				commits.Enqueue,
				image => applied.Add(image?.Name),
				() => ReferenceEquals(currentSource, firstSource),
				static () => true);

			Assert.Equal(1, commits.Count);

			currentSource = secondSource;
			var second = loader.LoadAsync(
				secondSource,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(secondResult),
				commits.Enqueue,
				image => applied.Add(image?.Name),
				() => ReferenceEquals(currentSource, secondSource),
				static () => true);

			Assert.Equal(2, commits.Count);

			// Execute the newer callback first, then the superseded callback.
			commits.RunAt(1);
			await second;

			Assert.Equal(["new"], applied);
			Assert.False(firstResult.IsDisposed);
			Assert.Equal(0, firstResult.ValueAccessCount);

			commits.RunAt(0);
			await first;

			Assert.Equal(["new"], applied);
			Assert.Equal(1, firstResult.DisposeCount);
			Assert.Equal(0, firstResult.ValueAccessCount);
			Assert.False(secondResult.IsDisposed);
			Assert.Equal("new", loader.Current?.Name);
		}

		/// <summary>
		/// Disposal between queueing and execution invalidates the callback before it reads Value.
		/// </summary>
		[Fact]
		public async Task QueuedCommitAfterDisposalDoesNotApplyOrTouchTheResult()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var commits = new QueuedCommit();
			var result = new FakeResult(new FakeImage { Name = "orphaned" });
			var applied = false;

			var load = loader.LoadAsync(
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result),
				commits.Enqueue,
				_ => applied = true,
				static () => true,
				static () => true);

			Assert.Equal(1, commits.Count);

			loader.Dispose();
			Assert.False(result.IsDisposed);

			commits.RunAt(0);
			await load;

			Assert.False(applied);
			Assert.Equal(0, result.ValueAccessCount);
			Assert.Equal(1, result.DisposeCount);
		}

		/// <summary>
		/// Source identity is checked after dispatch, immediately before the platform write.
		/// </summary>
		[Fact]
		public async Task QueuedCommitRechecksSourceIdentity()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var commits = new QueuedCommit();
			var source = new FakeSource();
			IImageSource currentSource = source;
			var result = new FakeResult(new FakeImage { Name = "stale" });
			var applied = false;

			var load = loader.LoadAsync(
				source,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result),
				commits.Enqueue,
				_ => applied = true,
				() => ReferenceEquals(currentSource, source),
				static () => true);

			currentSource = new FakeSource();
			commits.RunAt(0);
			await load;

			Assert.False(applied);
			Assert.Equal(0, result.ValueAccessCount);
			Assert.Equal(1, result.DisposeCount);
		}

		/// <summary>
		/// The accepted result remains owned and alive through the platform write.
		/// </summary>
		[Fact]
		public async Task SuccessfulCommitKeepsResultAliveUntilApplyReturns()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var result = new FakeResult(new FakeImage { Name = "live" });

			await loader.LoadAsync(
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result),
				RunInline,
				image =>
				{
					Assert.False(result.IsDisposed);
					Assert.False(image?.IsDisposed);
				},
				static () => true,
				static () => true);

			Assert.Equal(0, result.DisposeCount);

			loader.Dispose();

			Assert.Equal(1, result.DisposeCount);
		}

		/// <summary>
		/// Negative control: the old check-then-dispatch shape is sensitive to this ordering.
		/// </summary>
		[Fact]
		public void LegacyCheckThenDispatchWouldOverwriteAndUseDisposedImage()
		{
			var callbacks = new List<Action>();
			var oldResult = new FakeResult(new FakeImage { Name = "old" });
			var newResult = new FakeResult(new FakeImage { Name = "new" });
			string? applied = null;
			var touchedDisposedImage = false;

			static void QueueLegacyApply(
				List<Action> queue,
				FakeResult result,
				Action<FakeImage> apply)
			{
				// This is the old production sequence: Value is captured after validity checks,
				// then the actual platform write is dispatched without being awaited.
				var image = result.Value;
				queue.Add(() => apply(image));
			}

			QueueLegacyApply(callbacks, oldResult, image =>
			{
				touchedDisposedImage |= image.IsDisposed;
				applied = image.Name;
			});

			// A newer commit replaced the old active result before its callback ran.
			oldResult.Dispose();
			QueueLegacyApply(callbacks, newResult, image => applied = image.Name);

			callbacks[1]();
			callbacks[0]();

			Assert.Equal("old", applied);
			Assert.True(touchedDisposedImage);
		}

		[Theory]
		[InlineData("TizenButtonHandler.cs", "_iconLoader")]
		[InlineData("TizenSliderHandler.cs", "_thumbLoader")]
		public void ReconnectedHandlersCreateANewLoaderLifetime(string fileName, string fieldName)
		{
			var source = File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Handlers",
				fileName));
			var connectStart = source.IndexOf(
				"protected override void ConnectHandler",
				StringComparison.Ordinal);
			var disconnectStart = source.IndexOf(
				"protected override void DisconnectHandler",
				connectStart,
				StringComparison.Ordinal);

			Assert.True(connectStart >= 0 && disconnectStart > connectStart);

			var connect = source[connectStart..disconnectStart];
			Assert.Contains($"{fieldName}.Dispose();", connect, StringComparison.Ordinal);
			Assert.Contains($"{fieldName} = new();", connect, StringComparison.Ordinal);
		}
	}
}
