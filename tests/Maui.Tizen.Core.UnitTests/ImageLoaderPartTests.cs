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
	}
}
