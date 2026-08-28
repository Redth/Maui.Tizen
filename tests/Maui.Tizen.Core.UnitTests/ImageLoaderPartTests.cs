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
	public class ImageLoaderPartTests
	{
		sealed class FakeSource : IImageSource
		{
			public bool IsEmpty => false;
		}

		sealed class FakeImage
		{
			public string Name { get; init; } = string.Empty;
		}

		sealed class FakeResult : IImageSourceServiceResult<FakeImage>
		{
			public FakeResult(string name) => Value = new FakeImage { Name = name };

			public FakeImage Value { get; }

			public bool IsResolutionDependent => false;

			public bool IsDisposed { get; private set; }

			public void Dispose() => IsDisposed = true;
		}

		sealed class FakePart : IImageSourcePart, IImageSourcePartEvents
		{
			public IImageSource? Source { get; set; }

			public bool IsAnimationPlaying => false;

			public bool IsLoading { get; private set; }

			public int Started { get; private set; }

			public List<bool> Completions { get; } = new();

			public List<Exception> Failures { get; } = new();

			public void UpdateIsLoading(bool isLoading) => IsLoading = isLoading;

			public void LoadingStarted() => Started++;

			public void LoadingCompleted(bool successful) => Completions.Add(successful);

			public void LoadingFailed(Exception exception) => Failures.Add(exception);
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

			public void RunNext()
			{
				var pending = _pending[0];
				_pending.RemoveAt(0);
				pending.Action();
				pending.Completion.SetResult();
			}
		}

		static Task CommitInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		[Fact]
		public async Task SuccessfulLoadReportsPartLifecycle()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var part = new FakePart { Source = new FakeSource() };
			FakeImage? applied = null;

			await loader.LoadPartAsync(
				part,
				events,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult("success")),
				CommitInline,
				image => applied = image,
				static () => true);

			Assert.Equal("success", applied?.Name);
			Assert.Equal(1, part.Started);
			Assert.Equal(new[] { true }, part.Completions);
			Assert.Empty(part.Failures);
			Assert.False(part.IsLoading);
		}

		[Fact]
		public async Task ServiceFailureReportsLoadingFailedAndClearsLoading()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var part = new FakePart { Source = new FakeSource() };
			var failure = new InvalidOperationException("load failed");

			await loader.LoadPartAsync(
				part,
				events,
				(_, _) => Task.FromException<IImageSourceServiceResult<FakeImage>?>(failure),
				CommitInline,
				static _ => { },
				static () => true);

			Assert.Equal(1, part.Started);
			Assert.Empty(part.Completions);
			Assert.Equal(new[] { failure }, part.Failures);
			Assert.False(part.IsLoading);
		}

		[Fact]
		public async Task SupersededSameSourceCannotClearTheNewLoadsState()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var source = new FakeSource();
			var part = new FakePart { Source = source };
			var firstResult = new TaskCompletionSource<IImageSourceServiceResult<FakeImage>?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var secondResult = new TaskCompletionSource<IImageSourceServiceResult<FakeImage>?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var call = 0;
			FakeImage? applied = null;

			Task<IImageSourceServiceResult<FakeImage>?> Load(
				IImageSource _,
				CancellationToken __) =>
				Interlocked.Increment(ref call) == 1 ? firstResult.Task : secondResult.Task;

			var first = loader.LoadPartAsync(
				part, events, Load, CommitInline, image => applied = image, static () => true);
			var second = loader.LoadPartAsync(
				part, events, Load, CommitInline, image => applied = image, static () => true);

			firstResult.SetResult(new FakeResult("stale"));
			await first;

			Assert.True(part.IsLoading);
			Assert.Equal(new[] { false }, part.Completions);
			Assert.Null(applied);

			secondResult.SetResult(new FakeResult("current"));
			await second;

			Assert.Equal("current", applied?.Name);
			Assert.Equal(new[] { false, true }, part.Completions);
			Assert.False(part.IsLoading);
		}

		[Fact]
		public async Task CompletionAndLoadingResetRunInTheDispatchedLifecycleCommit()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var part = new FakePart { Source = new FakeSource() };
			var commits = new QueuedCommit();

			var load = loader.LoadPartAsync(
				part,
				events,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult("image")),
				commits.Enqueue,
				static _ => { },
				static () => true);

			Assert.True(part.IsLoading);
			Assert.Equal(1, commits.Count);

			commits.RunNext();
			for (var i = 0; i < 20 && commits.Count == 0; i++)
				await Task.Yield();

			Assert.Equal(1, commits.Count);
			Assert.Empty(part.Completions);
			Assert.True(part.IsLoading);

			commits.RunNext();
			await load;

			Assert.Equal(new[] { true }, part.Completions);
			Assert.False(part.IsLoading);
		}

		[Fact]
		public async Task DisconnectClearsOriginatingLoadingStateAndRejectsLateResult()
		{
			var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var part = new FakePart { Source = new FakeSource() };
			var result = new TaskCompletionSource<IImageSourceServiceResult<FakeImage>?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			FakeImage? applied = null;

			var load = loader.LoadPartAsync(
				part,
				events,
				(_, _) => result.Task,
				CommitInline,
				image => applied = image,
				static () => true);

			Assert.True(part.IsLoading);

			events.Invalidate();
			loader.Dispose();
			result.SetResult(new FakeResult("late"));
			await load;

			Assert.False(part.IsLoading);
			Assert.Null(applied);
			Assert.Equal(new[] { false }, part.Completions);
		}

		[Fact]
		public async Task RejectedDispatcherIsResetByDisconnect()
		{
			using var loader = new TizenImageLoader<FakeImage>();
			var events = new TizenImageLoadEvents();
			var part = new FakePart { Source = new FakeSource() };

			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				loader.LoadPartAsync(
					part,
					events,
					(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult("image")),
					static _ => Task.FromException(new InvalidOperationException("dispatch rejected")),
					static _ => { },
					static () => true));

			events.Invalidate();

			Assert.False(part.IsLoading);
		}

		[Fact]
		public async Task LateLoadAAfterDisconnectReconnectAndLoadBCannotReplaceB()
		{
			var events = new TizenImageLoadEvents();
			var loaderA = new TizenImageLoader<FakeImage>();
			var partA = new FakePart { Source = new FakeSource() };
			var pendingA = new TaskCompletionSource<IImageSourceServiceResult<FakeImage>?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			FakeImage? applied = null;

			var loadA = loaderA.LoadPartAsync(
				partA,
				events,
				(_, _) => pendingA.Task,
				CommitInline,
				image => applied = image,
				static () => true);

			events.Invalidate();
			loaderA.Dispose();

			using var loaderB = new TizenImageLoader<FakeImage>();
			var partB = new FakePart { Source = new FakeSource() };
			await loaderB.LoadPartAsync(
				partB,
				events,
				(_, _) => Task.FromResult<IImageSourceServiceResult<FakeImage>?>(new FakeResult("B")),
				CommitInline,
				image => applied = image,
				static () => true);

			pendingA.SetResult(new FakeResult("A"));
			await loadA;

			Assert.Equal("B", applied?.Name);
			Assert.False(partA.IsLoading);
			Assert.False(partB.IsLoading);
			Assert.Equal(new[] { false }, partA.Completions);
			Assert.Equal(new[] { true }, partB.Completions);
		}
	}
}
