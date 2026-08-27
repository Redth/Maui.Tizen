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
		readonly Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TizenImageSource>?>> _resolve;

		public StubProvider(Func<IImageSource, CancellationToken, Task<TizenImageSource?>> resolve)
			: this(async (source, token) =>
			{
				var image = await resolve(source, token);
				return image is null ? null : new TizenImageSourceServiceResult(image);
			})
		{
		}

		// Returns the result instance itself, so tests can observe whether the loader disposed it.
		public StubProvider(Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TizenImageSource>?>> resolve) =>
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
			readonly Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TizenImageSource>?>> _resolve;

			public Service(Func<IImageSource, CancellationToken, Task<IImageSourceServiceResult<TizenImageSource>?>> resolve) =>
				_resolve = resolve;

			public Task<IImageSourceServiceResult<TizenImageSource>?> GetImageAsync(
				IImageSource imageSource,
				CancellationToken cancellationToken = default) =>
				_resolve(imageSource, cancellationToken);
		}
	}

	static Task<TizenImageApplyResult> NoopApply(TizenImageSource? image, TizenImageWrite write, CancellationToken token) =>
		Task.FromResult(write(static () => { }) ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled);

	[Fact]
	public async Task LoadAppliesTheResolvedImage()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = "a.png" }));

		TizenImageSource? applied = null;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(part, provider, (image, write, _) => { write(() => applied = image); return Task.FromResult(TizenImageApplyResult.Success); });

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
		Task<TizenImageApplyResult> Apply(TizenImageSource? image, TizenImageWrite write, CancellationToken token)
		{
			var wrote = write(() =>
			{
				lock (applied)
				{
					applied.Add(image?.ResourceUrl);
				}
			});

			return Task.FromResult(wrote ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled);
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
		Task<TizenImageApplyResult> Apply(TizenImageSource? image, TizenImageWrite write, CancellationToken token)
		{
			var wrote = write(() =>
			{
				lock (applied)
				{
					applied.Add(image?.ResourceUrl);
				}
			});

			return Task.FromResult(wrote ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled);
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

		var pending = loader.LoadAsync(part, provider, (_, write, _) => { write(() => applied = true); return Task.FromResult(TizenImageApplyResult.Success); });

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
		var pending = loader.LoadAsync(part, provider, async (_, _, token) =>
		{
			var never = new TaskCompletionSource();
			using var registration = token.Register(() => never.TrySetResult());
			await never.Task;
			return TizenImageApplyResult.Cancelled;
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
			tasks.Add(loader.LoadAsync(part, provider, (image, write, _) =>
			{
				write(() =>
				{
					lock (applied)
					{
						applied.Add(image?.ResourceUrl);
					}
				});

				return Task.FromResult(TizenImageApplyResult.Success);
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
		await loader.LoadAsync(part, provider, (_, write, _) => { write(() => applied = true); return Task.FromResult(TizenImageApplyResult.Success); });

		Assert.False(applied);
	}

	// ---------------------------------------------------------------------------------------
	// Ownership, disposal and honest completion reporting.
	// ---------------------------------------------------------------------------------------

	/// <summary>
	/// A load started before a disconnect must not complete into the reconnected view.
	/// </summary>
	/// <remarks>
	/// This is why the loader tracks a monotonic generation and not just a token. <see cref="TizenImageSourceLoader.Cancel"/>
	/// replaces the token source, so the old token being cancelled says nothing about whether the
	/// handler that started the load is still the one on screen. The generation never goes
	/// backwards, so a pre-disconnect load can always be recognised as stale.
	/// </remarks>
	[Fact]
	public async Task ALoadStartedBeforeDisconnectCannotCompleteIntoAReconnectedView()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var gate = new TaskCompletionSource();

		// Ignores the token, exactly as a badly behaved service would.
		var provider = new StubProvider(async (_, _) =>
		{
			await gate.Task;
			return new TizenImageSource { ResourceUrl = "stale.png" };
		});

		using var loader = new TizenImageSourceLoader();

		var applied = false;
		var pending = loader.LoadAsync(part, provider, (_, write, _) =>
		{
			write(() => applied = true);
			return Task.FromResult(TizenImageApplyResult.Success);
		});

		// Disconnect, then reconnect: a new generation begins.
		loader.Cancel();

		gate.SetResult();
		await pending;

		Assert.False(applied);
	}

	/// <summary>A successful load takes ownership of its result and disposes what it replaces.</summary>
	[Fact]
	public async Task ReplacingAnImageDisposesThePreviousResult()
	{
		var part = new StubPart { Source = new StubImageSource("first") };

		var results = new List<TizenImageSourceServiceResult>();
		var provider = new StubProvider((_, _) =>
		{
			var result = new TizenImageSourceServiceResult(new TizenImageSource());
			results.Add(result);
			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(result);
		});

		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(part, provider, NoopApply);
		Assert.All(results, r => Assert.False(r.IsDisposed));

		part.Source = new StubImageSource("second");
		await loader.LoadAsync(part, provider, NoopApply);

		// The first result is no longer displayed, so it must have been released.
		Assert.True(results[0].IsDisposed);
		Assert.False(results[1].IsDisposed);
	}

	/// <summary>Tearing the handler down releases the image it was holding.</summary>
	[Fact]
	public async Task CancelDisposesTheCurrentResult()
	{
		var part = new StubPart { Source = new StubImageSource("s") };

		TizenImageSourceServiceResult? result = null;
		var provider = new StubProvider((_, _) =>
		{
			result = new TizenImageSourceServiceResult(new TizenImageSource());
			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(result);
		});

		using var loader = new TizenImageSourceLoader();
		await loader.LoadAsync(part, provider, NoopApply);

		Assert.False(result!.IsDisposed);

		loader.Cancel();

		Assert.True(result.IsDisposed);
	}

	/// <summary>Setting the source to null must take the previous image down.</summary>
	/// <remarks>
	/// Without this the control keeps showing the old image and appears not to have changed at all,
	/// which reads as "the binding is broken" rather than "the source is empty".
	/// </remarks>
	[Fact]
	public async Task ANullSourceClearsThePreviousImage()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		using var loader = new TizenImageSourceLoader();
		await loader.LoadAsync(part, provider, NoopApply);

		var cleared = false;
		part.Source = null;
		await loader.LoadAsync(part, provider, NoopApply, () => cleared = true);

		Assert.True(cleared);
	}

	/// <summary>A source that resolves to nothing clears the view and reports failure.</summary>
	/// <remarks>This is the path a font image source takes, since Tizen cannot rasterise glyphs.</remarks>
	[Fact]
	public async Task AnUnresolvableSourceClearsAndReportsFailure()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(null));

		var cleared = false;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(part, provider, NoopApply, () => cleared = true);

		Assert.True(cleared);
		Assert.Equal(new[] { false }, part.Completions);
		Assert.False(part.IsLoading);
	}

	/// <summary>A failing service clears the view rather than leaving a stale image behind.</summary>
	[Fact]
	public async Task AFailingServiceClearsTheView()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromException<TizenImageSource?>(new InvalidOperationException("boom")));

		var cleared = false;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(part, provider, NoopApply, () => cleared = true);

		Assert.True(cleared);
		Assert.Single(part.Failures);
	}

	/// <summary>
	/// A platform that reports a failed decode must not be recorded as a successful load.
	/// </summary>
	/// <remarks>
	/// NUI assigns a resource URL synchronously and only later reports whether the bytes decoded.
	/// Treating the assignment as success marks a broken or missing image as loaded, which is
	/// exactly the state a caller cannot recover from because nothing told it anything went wrong.
	/// </remarks>
	[Fact]
	public async Task APlatformDecodeFailureIsReportedAsFailure()
	{
		var part = new StubPart { Source = new StubImageSource("s") };

		TizenImageSourceServiceResult? result = null;
		var provider = new StubProvider((_, _) =>
		{
			result = new TizenImageSourceServiceResult(new TizenImageSource());
			return Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(result);
		});

		var cleared = false;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(
			part,
			provider,
			(_, _, _) => Task.FromResult(TizenImageApplyResult.Failed),
			() => cleared = true);

		Assert.Equal(new[] { false }, part.Completions);
		Assert.True(cleared);

		// A result that never made it to the screen must not be retained.
		Assert.True(result!.IsDisposed);
	}

	/// <summary>A cancelled apply reports failure but must not clear a newer image.</summary>
	[Fact]
	public async Task ACancelledApplyDoesNotClearTheView()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		var cleared = false;
		using var loader = new TizenImageSourceLoader();

		await loader.LoadAsync(
			part,
			provider,
			(_, _, _) => Task.FromResult(TizenImageApplyResult.Cancelled),
			() => cleared = true);

		Assert.Equal(new[] { false }, part.Completions);
		Assert.False(cleared);
	}

	/// <summary>Every started load advances the generation.</summary>
	[Fact]
	public async Task EachLoadAdvancesTheGeneration()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource()));

		using var loader = new TizenImageSourceLoader();

		var before = loader.Generation;
		await loader.LoadAsync(part, provider, NoopApply);
		var afterLoad = loader.Generation;

		loader.Cancel();

		Assert.True(afterLoad > before);
		Assert.True(loader.Generation > afterLoad);
	}

	/// <summary>
	/// A stale load that fails must not clear the image a newer load already put on screen.
	/// </summary>
	/// <remarks>
	/// The service deliberately ignores its cancellation token and throws a plain
	/// <see cref="InvalidOperationException"/> rather than <see cref="OperationCanceledException"/>,
	/// so the load reaches the general exception handler instead of unwinding through the
	/// cancellation path. Without the ownership guard there, the superseded load clears the platform
	/// view and raises LoadingFailed for a source the control is no longer displaying: the image
	/// goes blank and an error surfaces for the wrong source.
	/// </remarks>
	[Fact]
	public async Task AStaleFailureDoesNotClearANewerImage()
	{
		var first = new StubImageSource("first");
		var second = new StubImageSource("second");
		var part = new StubPart { Source = first };

		var firstGate = new TaskCompletionSource();

		var provider = new StubProvider(async (source, token) =>
		{
			var stub = (StubImageSource)source;

			if (stub.Name == "first")
			{
				// Note: no WaitAsync(token) - this service ignores cancellation, which is what
				// forces the failure through the general handler.
				await firstGate.Task;
				throw new InvalidOperationException("stale decode failure");
			}

			return new TizenImageSource { ResourceUrl = stub.Name + ".png" };
		});

		using var loader = new TizenImageSourceLoader();

		var cleared = 0;
		void Clear() => Interlocked.Increment(ref cleared);

		var slow = loader.LoadAsync(part, provider, NoopApply, Clear);

		part.Source = second;
		await loader.LoadAsync(part, provider, NoopApply, Clear);

		// The newer load has succeeded. Only now does the stale one blow up.
		firstGate.SetResult();
		await slow;

		Assert.Equal(0, Volatile.Read(ref cleared));
		Assert.Empty(part.Failures);
		Assert.Equal(new[] { true, false }, part.Completions);
	}

	/// <summary>
	/// The same guard, on the apply path: a stale load whose apply reports failure must not clear
	/// a newer image either.
	/// </summary>
	/// <remarks>
	/// Applying awaits the platform, so a load can lose ownership between resolving and hearing
	/// back. The failure branch runs before the old ownership re-check did, so a superseded load
	/// whose decode failed would clear the newer image.
	/// </remarks>
	[Fact]
	public async Task AStaleApplyFailureDoesNotClearANewerImage()
	{
		var first = new StubImageSource("first");
		var second = new StubImageSource("second");
		var part = new StubPart { Source = first };

		var applyGate = new TaskCompletionSource();

		var provider = new StubProvider((source, token) =>
			Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = ((StubImageSource)source).Name + ".png" }));

		using var loader = new TizenImageSourceLoader();

		var cleared = 0;
		void Clear() => Interlocked.Increment(ref cleared);

		async Task<TizenImageApplyResult> Apply(TizenImageSource? image, TizenImageWrite write, CancellationToken token)
		{
			if (image?.ResourceUrl == "first.png")
			{
				await applyGate.Task;
				return TizenImageApplyResult.Failed;
			}

			write(static () => { });
			return TizenImageApplyResult.Success;
		}

		var slow = loader.LoadAsync(part, provider, Apply, Clear);

		part.Source = second;
		await loader.LoadAsync(part, provider, Apply, Clear);

		applyGate.SetResult();
		await slow;

		Assert.Equal(0, Volatile.Read(ref cleared));
		Assert.Equal(new[] { true, false }, part.Completions);
	}

	// ---------------------------------------------------------------------------------------
	// Atomicity of the native write.
	//
	// These interpose at the exact instant a stale write would happen. Checking ownership and then
	// writing as two steps leaves a window in between; the loader closes it by performing both
	// under the lock that guards the generation. Interposing deterministically is what proves the
	// window is gone, rather than merely unlikely.
	// ---------------------------------------------------------------------------------------

	/// <summary>
	/// A load disconnected between the ownership check and the write must be refused the write.
	/// </summary>
	/// <remarks>
	/// The apply callback cancels the loader — exactly what DisconnectHandler does — and only then
	/// attempts the write. That is the interleaving a check-then-act sequence cannot survive: the
	/// check has already passed, so the write would land on a view the handler no longer owns.
	/// </remarks>
	[Fact]
	public async Task AWriteIsRefusedAfterTheHandlerDisconnects()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = "a.png" }));

		using var loader = new TizenImageSourceLoader();

		var wrote = false;
		var permitted = true;

		await loader.LoadAsync(part, provider, (image, write, _) =>
		{
			// Disconnect happens here - after the loader's own checks, before the write.
			loader.Cancel();

			permitted = write(() => wrote = true);

			return Task.FromResult(permitted ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled);
		});

		Assert.False(permitted);
		Assert.False(wrote);
	}

	/// <summary>
	/// A load superseded between the ownership check and the write must be refused the write.
	/// </summary>
	/// <remarks>
	/// The same window, reached the other way: a newer load starts while this one is applying. The
	/// newer load bumps the generation, so the older one's write must be refused even though its
	/// own checks passed moments earlier.
	/// </remarks>
	[Fact]
	public async Task AWriteIsRefusedAfterANewerLoadStarts()
	{
		var first = new StubImageSource("first");
		var second = new StubImageSource("second");
		var part = new StubPart { Source = first };

		var provider = new StubProvider((source, _) =>
			Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = ((StubImageSource)source).Name + ".png" }));

		using var loader = new TizenImageSourceLoader();

		var written = new List<string?>();
		var firstPermitted = true;
		var interposed = false;

		async Task<TizenImageApplyResult> Apply(TizenImageSource? image, TizenImageWrite write, CancellationToken token)
		{
			if (image?.ResourceUrl == "first.png" && !interposed)
			{
				interposed = true;

				// A newer load lands mid-apply, exactly as a rapid source change would.
				part.Source = second;
				await loader.LoadAsync(part, provider, Apply);

				firstPermitted = write(() => written.Add("first.png"));

				return firstPermitted ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled;
			}

			return write(() => written.Add(image?.ResourceUrl)) ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled;
		}

		await loader.LoadAsync(part, provider, Apply);

		Assert.False(firstPermitted);
		Assert.Equal(new[] { "second.png" }, written);
	}

	/// <summary>
	/// Refusing the write must not leave the apply step waiting for a decode that cannot happen.
	/// </summary>
	/// <remarks>
	/// The real apply awaits NUI's ResourceReady, which only fires because an image was assigned. If
	/// a refused write still led to that await, a superseded load would hang forever holding its
	/// result. This models the contract that makes the real implementation return instead.
	/// </remarks>
	[Fact]
	public async Task ARefusedWriteCompletesRatherThanAwaitingADecode()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = "a.png" }));

		using var loader = new TizenImageSourceLoader();

		var load = loader.LoadAsync(part, provider, async (image, write, token) =>
		{
			loader.Cancel();

			if (!write(static () => { }))
				return TizenImageApplyResult.Cancelled;

			// Stands in for awaiting ResourceReady: unreachable once the write is refused.
			var never = new TaskCompletionSource();
			using var registration = token.Register(() => never.TrySetResult());
			await never.Task;

			return TizenImageApplyResult.Success;
		});

		var finished = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(5)));

		Assert.Same(load, finished);
		Assert.Equal(new[] { false }, part.Completions);
	}

	/// <summary>A clear is refused just as a write is, once ownership is gone.</summary>
	/// <remarks>
	/// Clearing is a native mutation too. An unguarded clear on a superseded load blanks the image
	/// the newer load has already put on screen — the same defect as a stale write, with the
	/// opposite symptom.
	/// </remarks>
	[Fact]
	public async Task AClearIsRefusedAfterTheHandlerDisconnects()
	{
		var part = new StubPart { Source = new StubImageSource("s") };

		// Held open so the disconnect lands before the failure propagates; without the gate the
		// service throws synchronously and the load is already over.
		var gate = new TaskCompletionSource();

		var provider = new StubProvider(async Task<TizenImageSource?> (_, _) =>
		{
			await gate.Task;
			throw new InvalidOperationException("boom");
		});

		using var loader = new TizenImageSourceLoader();

		var cleared = false;

		var load = loader.LoadAsync(part, provider, NoopApply, () => cleared = true);

		loader.Cancel();
		gate.SetResult();
		await load;

		Assert.False(cleared);
		Assert.Empty(part.Failures);
	}

	/// <summary>
	/// A concurrent disconnect cannot interleave with a native write.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The single-threaded interposition tests prove the guard is consulted; this one proves the
	/// guard and the mutation are <em>indivisible</em>. A second thread calls
	/// <see cref="TizenImageSourceLoader.Cancel()"/> while the write is executing: if ownership were
	/// checked and then released before mutating, the cancel could slip in between and the write
	/// would land on a disconnected view.
	/// </para>
	/// <para>
	/// Deterministic despite being concurrent: the assertion is an ordering, not a timing. The
	/// cancel is observed to complete only after the write has finished, which is exactly what
	/// holding one lock across both guarantees.
	/// </para>
	/// </remarks>
	[Fact]
	public async Task ADisconnectCannotInterleaveWithAWrite()
	{
		var part = new StubPart { Source = new StubImageSource("s") };
		var provider = new StubProvider((_, _) => Task.FromResult<TizenImageSource?>(new TizenImageSource { ResourceUrl = "a.png" }));

		using var loader = new TizenImageSourceLoader();

		var cancelStarted = new TaskCompletionSource();
		var order = new List<string>();

		await loader.LoadAsync(part, provider, async (image, write, _) =>
		{
			var cancelling = Task.Run(async () =>
			{
				await cancelStarted.Task;
				loader.Cancel();

				lock (order)
				{
					order.Add("cancel-finished");
				}
			});

			write(() =>
			{
				// Release the other thread while still inside the guarded section, then give it
				// every chance to run. It must not be able to complete until this returns.
				cancelStarted.SetResult();
				Thread.Sleep(50);

				lock (order)
				{
					order.Add("write-finished");
				}
			});

			await cancelling;

			return TizenImageApplyResult.Success;
		});

		Assert.Equal(new[] { "write-finished", "cancel-finished" }, order);
	}
}
