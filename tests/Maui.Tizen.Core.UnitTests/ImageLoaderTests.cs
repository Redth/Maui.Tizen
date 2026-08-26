// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
		}

		sealed class FakeResult : IImageSourceServiceResult<FakeImage>
		{
			public FakeResult(FakeImage value) => Value = value;

			public FakeImage Value { get; }

			public bool IsResolutionDependent => false;

			public bool IsDisposed { get; private set; }

			public void Dispose() => IsDisposed = true;
		}

		sealed class FakeSource : IImageSource
		{
			public bool IsEmpty => false;
		}

		[Fact]
		public async Task AppliesTheLoadedImage()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			FakeImage? applied = null;

			await loader.LoadAsync(
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

			await loader.LoadAsync(
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "a" })),
				image => { applied = image; applyCount++; },
				static () => true);

			await loader.LoadAsync(
				source: null,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(null),
				image => { applied = image; applyCount++; },
				static () => true);

			Assert.Null(applied);
			Assert.Equal(2, applyCount);
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

			var first = loader.LoadAsync(
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
			var second = loader.LoadAsync(
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

			await loader.LoadAsync(
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

			await loader.LoadAsync(
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

			await loader.LoadAsync(new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(first), _ => { }, static () => true);
			Assert.False(first.IsDisposed);

			await loader.LoadAsync(new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(second), _ => { }, static () => true);

			Assert.True(first.IsDisposed, "Replacing the image must dispose the result it replaced.");
			Assert.False(second.IsDisposed);
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

			await loader.LoadAsync(new FakeSource(), (_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(result), _ => { }, static () => true);

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

			var load = loader.LoadAsync(
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

			var slow = loader.LoadAsync(
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

			await loader.LoadAsync(
				new FakeSource(),
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult(new FakeImage { Name = "current" })),
				image => applied.Add(image?.Name),
				static () => true);

			release.SetResult();
			await slow;

			// The cancelled load must not have appended a clearing null after "current".
			Assert.Equal(["current"], applied);
		}
	}
}
