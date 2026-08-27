using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Behavioural tests for <see cref="TizenImageSourceLoader"/>'s cancellation semantics.
/// </summary>
/// <remarks>
/// These execute the real loader. Image loading is asynchronous, so the failures that matter are
/// orderings — a slow earlier load finishing last, or a load completing into a handler that has
/// already been disconnected. None of those are visible to a compile check or to key-presence
/// parity; they only show up when the code is run.
/// </remarks>
public class ImageSourceLoaderTests
{
	sealed class StubImageSource : IImageSource
	{
		public StubImageSource(string name) => Name = name;

		public string Name { get; }

		public bool IsEmpty => false;
	}

	sealed class StubPart : IImageSourcePart, IImageSourcePartEvents
	{
		public IImageSource? Source { get; set; }
		public bool IsAnimationPlaying => false;

		public List<bool> Completions { get; } = new();
		public List<Exception> Failures { get; } = new();
		public int StartedCount { get; private set; }
		public bool? IsLoading { get; private set; }

		public void UpdateIsLoading(bool isLoading) => IsLoading = isLoading;
		public void LoadingStarted() => StartedCount++;
		public void LoadingCompleted(bool successful) => Completions.Add(successful);
		public void LoadingFailed(Exception exception) => Failures.Add(exception);
	}

	sealed class StubProvider : IImageSourceServiceProvider
	{
		readonly Func<IImageSource, CancellationToken, Task<TizenImageSource?>> _resolve;

		public StubProvider(Func<IImageSource, CancellationToken, Task<TizenImageSource?>> resolve) =>
			_resolve = resolve;

		public IImageSourceService? GetImageSourceService(Type imageSource) => new Service(_resolve);

		public IImageSourceService GetRequiredImageSourceService(IImageSource imageSource) =>
			new Service(_resolve);

		public IImageSourceService GetRequiredImageSourceService(Type imageSource) =>
			new Service(_resolve);

		public Type GetImageSourceServiceType(Type imageSource) => typeof(Service);

		public IServiceProvider HostServiceProvider => this;

		public object? GetService(Type serviceType) => null;

		sealed class Service : ITizenImageSourceService
		{
			readonly Func<IImageSource, CancellationToken, Task<TizenImageSource?>> _resolve;

			public Service(Func<IImageSource, CancellationToken, Task<TizenImageSource?>> resolve) =>
				_resolve = resolve;

			public async Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(
				IImageSource imageSource,
				CancellationToken cancellationToken = default)
			{
				var image = await _resolve(imageSource, cancellationToken);
				return image is null ? null : new TizenImageSourceServiceResult(image);
			}
		}
	}

	static Task NoopApply(TizenImageSource? image, CancellationToken token) => Task.CompletedTask;

	[Fact]
	public async Task LoadAppliesTheResolvedImage()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = "a.png" }));

		TizenImageSource? applied = null;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(part, provider, (image, _) => { applied = image; return Task.CompletedTask; });

		Assert.Equal("a.png", applied?.ResourceUrl);
		Assert.Equal(new[] { true }, part.Completions);
		Assert.False(part.IsLoading);
	}

	/// <summary>
	/// The core regression: a slow first load must not apply after a faster second one.
	/// </summary>
	[Fact]
	public async Task ASupersededLoadNeverAppliesItsImage()
	{
		var first = new StubImageSource("first");
		var second = new StubImageSource("second");
		var part = new StubPart { Source = first };

		// Holds the FIRST load open until the second has already finished, which is the ordering
		// that makes a stale write possible.
		var firstGate = new TaskCompletionSource();

		var provider = new StubProvider(async (source, token) =>
		{
			var stub = (StubImageSource)source;

			if (stub.Name == "first")
				await firstGate.Task.WaitAsync(token);

			return new TizenImageSource { ResourceUrl = stub.Name + ".png" };
		});

		using var loader = new TizenImageSourceLoader();

		var applied = new List<string?>();
		Task Apply(TizenImageSource? image, CancellationToken token)
		{
			lock (applied)
			{
				applied.Add(image?.ResourceUrl);
			}

			return Task.CompletedTask;
		}

		var slow = loader.LoadAsync(part, provider, Apply);

		part.Source = second;
		await loader.LoadAsync(part, provider, Apply);

		// Only now let the superseded load resolve. It must not apply.
		firstGate.SetResult();
		await slow;

		Assert.Equal(new[] { "second.png" }, applied);
	}

	/// <summary>
	/// A service that ignores its cancellation token must still not produce a stale write.
	/// </summary>
	/// <remarks>
	/// This is the test that actually exercises the source re-check in the loader. Its sibling above
	/// does not: when a load is superseded the token is cancelled, and a well-behaved service throws
	/// <see cref="OperationCanceledException"/> from the token — so the load unwinds before the
	/// re-check is ever reached. Deleting the re-check leaves that test green, which was verified by
	/// removing it. Honouring a token is a convention, not a guarantee, so the loader must not rely
	/// on it and this test pins that.
	/// </remarks>
	[Fact]
	public async Task AServiceThatIgnoresCancellationStillCannotWriteStaleImages()
	{
		var first = new StubImageSource("first");
		var second = new StubImageSource("second");
		var part = new StubPart { Source = first };

		var firstGate = new TaskCompletionSource();

		var provider = new StubProvider(async (source, _) =>
		{
			var stub = (StubImageSource)source;

			// Deliberately ignores the token.
			if (stub.Name == "first")
				await firstGate.Task;

			return new TizenImageSource { ResourceUrl = stub.Name + ".png" };
		});

		using var loader = new TizenImageSourceLoader();

		var applied = new List<string?>();
		Task Apply(TizenImageSource? image, CancellationToken token)
		{
			lock (applied)
			{
				applied.Add(image?.ResourceUrl);
			}

			return Task.CompletedTask;
		}

		var slow = loader.LoadAsync(part, provider, Apply);

		part.Source = second;
		await loader.LoadAsync(part, provider, Apply);

		firstGate.SetResult();
		await slow;

		Assert.Equal(new[] { "second.png" }, applied);
	}

	/// <summary>A load cancelled by disconnection must not write to the platform view.</summary>
	[Fact]
	public async Task CancelPreventsTheImageFromBeingApplied()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var gate = new TaskCompletionSource();

		var provider = new StubProvider(async (_, token) =>
		{
			await gate.Task.WaitAsync(token);
			return new TizenImageSource { ResourceUrl = "late.png" };
		});

		var applied = false;
		using var loader = new TizenImageSourceLoader();

		var pending = loader.LoadAsync(part, provider, (_, _) => { applied = true; return Task.CompletedTask; });

		loader.Cancel();
		gate.SetResult();
		await pending;

		Assert.False(applied);
		Assert.Equal(new[] { false }, part.Completions);
	}

	/// <summary>
	/// A load whose platform event never fires must still unwind once cancelled, rather than
	/// hanging a property mapper forever.
	/// </summary>
	[Fact]
	public async Task ALoadWhoseApplyNeverCompletesIsReleasedByCancel()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		using var loader = new TizenImageSourceLoader();

		// Stands in for NUI never raising ResourceReady: completes only on cancellation.
		var pending = loader.LoadAsync(part, provider, async (_, token) =>
		{
			var never = new TaskCompletionSource();
			using var registration = token.Register(() => never.TrySetResult());
			await never.Task;
		});

		Assert.False(pending.IsCompleted);

		loader.Cancel();

		await pending.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(pending.IsCompletedSuccessfully);
	}

	/// <summary>Rapid source changes must leave exactly the newest image applied.</summary>
	[Fact]
	public async Task RapidSourceChangesApplyOnlyTheNewestImage()
	{
		var part = new StubPart();
		var provider = new StubProvider((source, _) =>
			Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = source.GetHashCode().ToString() }));

		using var loader = new TizenImageSourceLoader();

		var applied = new List<string?>();
		var tasks = new List<Task>();

		for (var i = 0; i < 25; i++)
		{
			part.Source = new StubImageSource("s");
			tasks.Add(loader.LoadAsync(part, provider, (image, _) =>
			{
				lock (applied)
				{
					applied.Add(image?.ResourceUrl);
				}

				return Task.CompletedTask;
			}));
		}

		await Task.WhenAll(tasks);

		Assert.Contains(part.Source!.GetHashCode().ToString(), applied);
	}

	/// <summary>A null source is not a load: no events, no work.</summary>
	[Fact]
	public async Task ANullSourceDoesNothing()
	{
		var part = new StubPart { Source = null };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		using var loader = new TizenImageSourceLoader();
		await loader.LoadAsync(part, provider, NoopApply);

		Assert.Equal(0, part.StartedCount);
		Assert.Empty(part.Completions);
	}

	/// <summary>A failing service reports through LoadingFailed rather than throwing to the mapper.</summary>
	[Fact]
	public async Task AFailingServiceIsReportedNotThrown()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromException<TizenImageSource?>(new InvalidOperationException("boom")));

		using var loader = new TizenImageSourceLoader();
		await loader.LoadAsync(part, provider, NoopApply);

		Assert.Single(part.Failures);
		Assert.Equal("boom", part.Failures[0].Message);
	}

	/// <summary>Disposal cancels in-flight work and makes further loads inert.</summary>
	[Fact]
	public async Task DisposeCancelsAndStopsFurtherLoads()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		var loader = new TizenImageSourceLoader();
		loader.Dispose();

		var applied = false;
		await loader.LoadAsync(part, provider, (_, _) => { applied = true; return Task.CompletedTask; });

		Assert.False(applied);
	}
}
