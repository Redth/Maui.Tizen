using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers the Tizen modal page navigation platform, shaped against the seam proposed by
/// dotnet/maui#37853.
/// </summary>
public class TizenModalNavigationPlatformTests
{
	static (TizenModalNavigationPlatform Platform, FakeModalNavigationHost Host, FakeNavigationStack Stack, FakeModalPageRealizer Realizer, FakeWindowBackButton BackButton) Build()
	{
		var host = new FakeModalNavigationHost(StubMauiContext.Empty()) { CurrentPage = new ContentPage() };
		var stack = new FakeNavigationStack();
		var realizer = new FakeModalPageRealizer();
		var backButton = new FakeWindowBackButton();
		var platform = new TizenModalNavigationPlatform(host, stack, realizer, backButton);
		return (platform, host, stack, realizer, backButton);
	}

	[Fact]
	public void TizenHasNoDeferredReadinessRequirement()
	{
		var (platform, host, _, _, _) = Build();
		using var _p = platform;

		// The NUI navigation stack accepts a push as soon as the window exists, matching
		// IsModalPlatformReady => true in the upstream Tizen partial. RequestSync is therefore
		// never needed.
		Assert.True(platform.IsReady);
		Assert.Equal(0, host.RequestSyncCount);
	}

	[Fact]
	public async Task PushRealizesThePageAndPushesItOntoTheStack()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;

		var modal = new ContentPage();
		host.RecordPush(modal);

		await platform.PushModalAsync(modal, animated: true);

		Assert.Equal(modal, Assert.Single(realizer.Realized));
		Assert.Equal(1, stack.Count);
		Assert.True(Assert.Single(stack.PushAnimations));
	}

	[Fact]
	public async Task PushPropagatesTheAnimationFlag()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;

		var first = new ContentPage();
		var second = new ContentPage();
		host.RecordPush(first);
		await platform.PushModalAsync(first, animated: false);
		host.RecordPush(second);
		await platform.PushModalAsync(second, animated: true);

		Assert.Equal(new[] { false, true }, stack.PushAnimations);
	}

	[Fact]
	public async Task PopPopsTheStackAndReleasesThePage()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;

		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, animated: false);

		host.RecordPop(modal);
		await platform.PopModalAsync(modal, animated: true);

		Assert.Equal(0, stack.Count);
		Assert.Equal(modal, Assert.Single(realizer.Released));
		Assert.True(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task PreMutationPopFailureThenRetryRemovalBeforeDisposalStillDisposesExactlyOnce()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);

		stack.PopFailure = new InvalidOperationException("native failure");
		stack.MutateBeforePopFailure = true;
		stack.PopFailuresBeforeMutationRemaining = 1;
		stack.RemoveBeforePopFailureWithoutDisposal = true;
		host.RecordPop(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PopModalAsync(modal, false));

		Assert.Equal(0, stack.Count);
		Assert.False(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task APopThatRemovesThenFaultsBeforeDisposalStillDisposesExactlyOnce()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);

		stack.PopFailure = new InvalidOperationException("native failure");
		stack.MutateBeforePopFailure = true;
		stack.RemoveBeforePopFailureWithoutDisposal = true;
		host.RecordPop(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PopModalAsync(modal, false));

		Assert.Equal(0, stack.Count);
		Assert.False(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task BatchPushSuppressesAnimationSoIntermediateModalsDoNotFlash()
	{
		var (platform, host, stack, _, _) = Build();
		using var _p = platform;

		var modal = new ContentPage();
		host.IsBatchPushing = true;
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, animated: true);

		Assert.False(Assert.Single(stack.PushAnimations));
	}

	[Fact]
	public async Task BatchPopSuppressesAnimationSoIntermediateModalsDoNotFlash()
	{
		var (platform, host, stack, _, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, animated: false);

		host.IsBatchPopping = true;
		host.RecordPop(modal);
		await platform.PopModalAsync(modal, animated: true);

		Assert.False(Assert.Single(stack.PopAnimations));
	}

	[Fact]
	public async Task MultipleModalsPopInReverseOrder()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;

		var first = new ContentPage();
		var second = new ContentPage();

		host.RecordPush(first);
		await platform.PushModalAsync(first, animated: false);
		host.RecordPush(second);
		await platform.PushModalAsync(second, animated: false);

		Assert.Equal(2, stack.Count);

		host.RecordPop(second);
		await platform.PopModalAsync(second, animated: false);
		host.RecordPop(first);
		await platform.PopModalAsync(first, animated: false);

		Assert.Equal(0, stack.Count);
		Assert.Equal(new[] { second, first }, realizer.Released);
	}

	[Fact]
	public async Task AFailedPushSurfacesToTheCaller()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;

		stack.PushFailure = new InvalidOperationException("native failure");

		var modal = new ContentPage();
		host.RecordPush(modal);

		// The framework rolls the platform stack back and rethrows to the PushModalAsync caller,
		// so the platform must not swallow this.
		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PushModalAsync(modal, false));

		Assert.False(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task APushThatMutatesBeforeFaultingRemovesTheViewAndReleasesTheHandler()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		stack.PushFailure = new InvalidOperationException("native failure");
		stack.MutateBeforePushFailure = true;

		var modal = new ContentPage();
		host.RecordPush(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PushModalAsync(modal, false));

		Assert.Equal(0, stack.Count);
		Assert.Equal(modal, Assert.Single(realizer.Released));
		Assert.True(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task BuriedModalRemovalExplicitlyDisposesThePlatformView()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);
		await stack.PushAsync(new object(), false);
		host.RecordPop(modal);

		await platform.PopModalAsync(modal, false);

		Assert.False(Assert.Single(realizer.Releases).PlatformViewDisposed);
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task APopThatMutatesBeforeFaultingStillReleasesTheHandler()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);

		stack.PopFailure = new InvalidOperationException("native failure");
		stack.MutateBeforePopFailure = true;
		host.RecordPop(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PopModalAsync(modal, false));

		Assert.Equal(0, stack.Count);
		Assert.Equal(modal, Assert.Single(realizer.Released));
	}

	[Fact]
	public async Task APopFailureBeforeMutationStillRemovesTheViewAndReleasesTheHandler()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);

		stack.PopFailure = new InvalidOperationException("native failure");
		host.RecordPop(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PopModalAsync(modal, true));

		Assert.Equal(0, stack.Count);
		Assert.Equal(modal, Assert.Single(realizer.Released));
	}

	[Fact]
	public async Task PopDoesNotRemoveAnUnrelatedTopWhenTheTrackedModalViewIsAlreadyGone()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _p = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);

		stack.Remove(realizer.PlatformViewFor(modal));
		var replacementRoot = new object();
		await stack.PushAsync(replacementRoot, false);
		host.RecordPop(modal);

		await platform.PopModalAsync(modal, false);

		Assert.Same(replacementRoot, stack.Top);
		Assert.Equal(modal, Assert.Single(realizer.Released));
	}

	[Fact]
	public void PageAttachedDoesNotInstallADuplicatePageBackHandler()
	{
		var (platform, _, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();

		Assert.Null(backButton.Handler);
		Assert.Equal(0, backButton.SetCount);
	}

	[Fact]
	public void PageAttachedIsSafeToCallRepeatedly()
	{
		var (platform, _, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();
		platform.PageAttached();

		Assert.Equal(0, backButton.SetCount);
		Assert.Null(backButton.Handler);
	}

	[Fact]
	public void PageAttachedWithoutABackButtonImplementationIsANoOp()
	{
		var host = new FakeModalNavigationHost(StubMauiContext.Empty()) { CurrentPage = new ContentPage() };
		using var platform = new TizenModalNavigationPlatform(host, new FakeNavigationStack(), new FakeModalPageRealizer());

		// The back-button registry belongs to the Tizen Core layer; without it back presses fall
		// through to the platform default rather than throwing.
		var exception = Record.Exception(platform.PageAttached);

		Assert.Null(exception);
	}

	[Fact]
	public void DisposeLeavesCoreBackRoutingUntouched()
	{
		var (platform, _, _, _, backButton) = Build();

		platform.PageAttached();
		platform.Dispose();

		Assert.Equal(0, backButton.SetCount);
		Assert.False(platform.IsReady);
	}

	[Fact]
	public void DisposeIsIdempotent()
	{
		var (platform, _, _, _, _) = Build();

		platform.Dispose();
		platform.Dispose();
	}

	[Fact]
	public async Task DisposeDuringPushLeavesTheInFlightOperationOwningItsViewUntilLateCompletion()
	{
		var (platform, host, stack, realizer, _) = Build();
		var blocker = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		stack.PushBlocker = blocker;
		var modal = new ContentPage();
		host.RecordPush(modal);

		var push = platform.PushModalAsync(modal, animated: true);
		await stack.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

		platform.Dispose();

		Assert.Empty(realizer.Released);
		Assert.Equal(0, realizer.DisposeCountFor(modal));

		blocker.SetResult(null);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => push);

		Assert.Equal(modal, Assert.Single(realizer.Released));
		Assert.Equal(1, realizer.DisposeCountFor(modal));

		platform.Dispose();
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task DisposeDuringPopDefersReleaseUntilTheNativePopCompletes()
	{
		var (platform, host, stack, realizer, _) = Build();
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);
		var blocker = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		stack.PopBlocker = blocker;
		host.RecordPop(modal);

		var pop = platform.PopModalAsync(modal, animated: true);
		await stack.PopStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

		platform.Dispose();
		Assert.Empty(realizer.Released);

		blocker.SetResult(null);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => pop);

		Assert.Equal(modal, Assert.Single(realizer.Released));
		Assert.Equal(1, realizer.DisposeCountFor(modal));

		platform.Dispose();
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task FailedPopAndFailedIdentityRemovalPreserveTrackingForRetry()
	{
		var (platform, host, stack, realizer, _) = Build();
		using var _platform = platform;
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);
		var platformView = realizer.PlatformViewFor(modal);
		stack.PopFailure = new InvalidOperationException("pop failed");
		stack.RemoveFailures.Add(platformView);
		host.RecordPop(modal);

		await Assert.ThrowsAsync<AggregateException>(() => platform.PopModalAsync(modal, false));

		Assert.True(stack.Contains(platformView));
		Assert.Empty(realizer.Released);
		Assert.Equal(0, realizer.DisposeCountFor(modal));

		stack.PopFailure = null;
		stack.RemoveFailures.Clear();
		await platform.PopModalAsync(modal, false);

		Assert.False(stack.Contains(platformView));
		Assert.Equal(modal, Assert.Single(realizer.Released));
		Assert.Equal(1, realizer.DisposeCountFor(modal));
	}

	[Fact]
	public async Task FrameworkDisposeAttemptsEveryCleanupLogsFailuresAndReturns()
	{
		var host = new FakeModalNavigationHost(StubMauiContext.Empty()) { CurrentPage = new ContentPage() };
		var stack = new FakeNavigationStack();
		var realizer = new FakeModalPageRealizer();
		var logger = new RecordingLogger<TizenModalNavigationPlatform>();
		var platform = new TizenModalNavigationPlatform(host, stack, realizer, logger: logger);
		var first = new ContentPage();
		var second = new ContentPage();
		host.RecordPush(first);
		await platform.PushModalAsync(first, false);
		host.RecordPush(second);
		await platform.PushModalAsync(second, false);
		var secondView = realizer.PlatformViewFor(second);
		stack.RemoveFailures.Add(secondView);

		var failure = Record.Exception(platform.Dispose);

		Assert.Null(failure);
		Assert.Contains(first, realizer.Released);
		Assert.DoesNotContain(second, realizer.Released);
		Assert.True(stack.Contains(secondView));
		Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);

		stack.RemoveFailures.Clear();
		failure = Record.Exception(platform.Dispose);

		Assert.Null(failure);
		Assert.Contains(second, realizer.Released);
		Assert.Equal(1, realizer.DisposeCountFor(first));
		Assert.Equal(1, realizer.DisposeCountFor(second));
	}

	[Fact]
	public async Task FrameworkDisposeStillReturnsWhenFailureLoggingThrows()
	{
		var host = new FakeModalNavigationHost(StubMauiContext.Empty()) { CurrentPage = new ContentPage() };
		var stack = new FakeNavigationStack();
		var realizer = new FakeModalPageRealizer();
		var logger = new RecordingLogger<TizenModalNavigationPlatform> { ThrowOnLog = true };
		var platform = new TizenModalNavigationPlatform(host, stack, realizer, logger: logger);
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);
		stack.RemoveFailures.Add(realizer.PlatformViewFor(modal));

		Assert.Null(Record.Exception(platform.Dispose));
	}

	[Fact]
	public async Task DisposeRemovesBuriedModalViewsAndReleasesEveryHandler()
	{
		var (platform, host, stack, realizer, _) = Build();
		var first = new ContentPage();
		var second = new ContentPage();

		host.RecordPush(first);
		await platform.PushModalAsync(first, false);
		host.RecordPush(second);
		await platform.PushModalAsync(second, false);

		platform.Dispose();

		Assert.Equal(0, stack.Count);
		Assert.Equal(new[] { second, first }, realizer.Released);
	}

	[Fact]
	public async Task PushAfterDisposeThrows()
	{
		var (platform, _, _, _, _) = Build();
		platform.Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => platform.PushModalAsync(new ContentPage(), false));
	}

	[Fact]
	public async Task PopAfterDisposeIsANoOpRatherThanAFailure()
	{
		var (platform, _, stack, realizer, _) = Build();
		platform.Dispose();

		// The window went away underneath us; faulting here would surface as a PopModalAsync
		// failure for a modal that is already gone with its window.
		await platform.PopModalAsync(new ContentPage(), false);

		Assert.Empty(stack.PopAnimations);
		Assert.Empty(realizer.Released);
	}

	[Fact]
	public void PageAttachedAfterDisposeIsANoOp()
	{
		var (platform, _, _, _, backButton) = Build();
		platform.Dispose();

		platform.PageAttached();

		Assert.Null(backButton.Handler);
	}

	// A page can be reused across windows - popped from one and pushed modally on another. Its
	// existing handler is bound to the ORIGINATING window's IMauiContext, and reusing it would
	// realize the page into the wrong window's view tree.

	[Fact]
	public void AHandlerBoundToAnotherWindowIsReplaced()
	{
		var realizer = new TizenModalPageRealizer();
		var originating = StubMauiContext.WithHandlers();
		var target = StubMauiContext.WithHandlers();

		var page = new ContentPage();
		var staleHandler = new StubViewHandler(page, mauiContext: originating);
		((Element)page).Handler = staleHandler;

		realizer.Realize(page, target);

		Assert.True(staleHandler.Disconnected);
		Assert.NotSame(staleHandler, ((Element)page).Handler);
		Assert.Same(target, ((Element)page).Handler!.MauiContext);
	}

	[Fact]
	public void AHandlerAlreadyBoundToTheTargetWindowIsReused()
	{
		var realizer = new TizenModalPageRealizer();
		var target = StubMauiContext.WithHandlers();

		var page = new ContentPage();
		var handler = new StubViewHandler(page, mauiContext: target);
		((Element)page).Handler = handler;

		realizer.Realize(page, target);

		Assert.False(handler.Disconnected);
		Assert.Same(handler, ((Element)page).Handler);
	}

	[Fact]
	public void TheTargetContextIsAlwaysAppliedBeforeRealization()
	{
		var realizer = new TizenModalPageRealizer();
		var target = StubMauiContext.WithHandlers();

		// A handler that exists but never received a context - realizing it without applying one
		// would produce a platform view belonging to no window.
		var page = new ContentPage();
		var handler = new StubViewHandler(page);
		((Element)page).Handler = handler;

		realizer.Realize(page, target);

		Assert.Same(target, ((Element)page).Handler!.MauiContext);
	}

	[Fact]
	public void ThePageIsRealizedAgainstTheTargetWindowsHandlerFactory()
	{
		var realizer = new TizenModalPageRealizer();
		var originating = StubMauiContext.WithHandlers();
		var target = StubMauiContext.WithHandlers();

		var page = new ContentPage();
		((Element)page).Handler = new StubViewHandler(page, mauiContext: originating);

		realizer.Realize(page, target);

		// The replacement comes from the TARGET window's factory, not the originating one.
		Assert.Equal(1, ((StubHandlersFactory)target.Handlers).Created);
		Assert.Equal(0, ((StubHandlersFactory)originating.Handlers).Created);
	}

	[Fact]
	public void TheVirtualViewIsBoundToThePageBeingPresented()
	{
		var realizer = new TizenModalPageRealizer();
		var target = StubMauiContext.WithHandlers();
		var page = new ContentPage();

		realizer.Realize(page, target);

		Assert.Same(page, ((Element)page).Handler!.VirtualView);
	}

	[Fact]
	public void SetVirtualViewFailureDisposesTheOwnedHandlerAndNativeView()
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new StubViewHandler(platformView: nativeView)
		{
			SetVirtualViewFailure = new InvalidOperationException("bind failed"),
		};
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();

		Assert.Throws<InvalidOperationException>(() => realizer.Realize(page, target));

		Assert.True(handler.Disposed);
		Assert.True(handler.Disconnected);
		Assert.True(nativeView.Disposed);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public void MissingPlatformViewDisposesTheOwnedHandlerAndClearsThePage()
	{
		var handler = new StubViewHandler();
		handler.PlatformView = null;
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();

		Assert.Throws<InvalidOperationException>(() => realizer.Realize(page, target));

		Assert.True(handler.Disposed);
		Assert.True(handler.Disconnected);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public void CrossWindowRecontextualizationDisposesTheOriginatingHandlerAndNativeView()
	{
		var realizer = new TizenModalPageRealizer();
		var originating = StubMauiContext.WithHandlers();
		var target = StubMauiContext.WithHandlers();
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var page = new ContentPage();
		var staleHandler = new StubViewHandler(page, platformView: nativeView, mauiContext: originating);
		((Element)page).Handler = staleHandler;

		realizer.Realize(page, target);

		Assert.True(staleHandler.Disposed);
		Assert.True(nativeView.Disposed);
		Assert.Equal(1, staleHandler.DisposeCount);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.NotSame(staleHandler, ((Element)page).Handler);
	}

	[Fact]
	public void ReleaseClearsAndDisposesTheOwnedHandlerSoThePageCanBeRealizedAgain()
	{
		var realizer = new TizenModalPageRealizer();
		var target = StubMauiContext.WithHandlers();
		var page = new ContentPage();
		var platformView = realizer.Realize(page, target);
		var handler = Assert.IsType<StubViewHandler>(((Element)page).Handler);

		realizer.Release(page, platformView, platformViewDisposed: false);

		Assert.True(handler.Disposed);
		Assert.Null(((Element)page).Handler);

		var secondPlatformView = realizer.Realize(page, target);
		Assert.NotSame(platformView, secondPlatformView);
	}

	[Fact]
	public void ReleaseDisposesTheCapturedHandlerWithoutClearingANewerPageHandler()
	{
		var realizer = new TizenModalPageRealizer();
		var target = StubMauiContext.WithHandlers();
		var page = new ContentPage();
		var platformView = realizer.Realize(page, target);
		var original = Assert.IsType<StubViewHandler>(((Element)page).Handler);
		var replacement = new StubViewHandler(page, mauiContext: target);
		((Element)page).Handler = replacement;

		realizer.Release(page, platformView, platformViewDisposed: false);

		Assert.True(original.Disposed);
		Assert.False(replacement.Disposed);
		Assert.Same(replacement, ((Element)page).Handler);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ReplacedDisposableHandlerReleasesOrphanedCapturedViewExactlyOnce(
		bool platformViewDisposed)
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var oldHandler = new DisposableDisconnectClearsPlatformViewHandler(nativeView);
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => oldHandler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();
		var platformView = realizer.Realize(page, target);

		oldHandler.DisconnectHandler();
		var replacement = new StubViewHandler(page, mauiContext: target);
		((Element)page).Handler = replacement;

		if (platformViewDisposed)
		{
			nativeView.Dispose();
		}

		realizer.Release(page, platformView, platformViewDisposed);

		Assert.Equal(1, oldHandler.DisposeCount);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.Same(replacement, ((Element)page).Handler);
	}

	[Fact]
	public void LiveDisposableContainerHandlerOwnsItsDistinctCapturedContainer()
	{
		var platformView = new FakeModalPageRealizer.FakePlatformView();
		var containerView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new NativeFaithfulDisposableContainerHandler(platformView, containerView);
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();
		var captured = realizer.Realize(page, target);

		Assert.Same(containerView, captured);

		realizer.Release(page, captured, platformViewDisposed: false);

		Assert.Equal(1, handler.DisposeCount);
		Assert.Equal(1, platformView.DisposeCount);
		Assert.Equal(1, containerView.DisposeCount);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public void ReplacedDisposableContainerHandlerReleasesOrphanedCaptureAndPreservesNewerHandler()
	{
		var platformView = new FakeModalPageRealizer.FakePlatformView();
		var containerView = new FakeModalPageRealizer.FakePlatformView();
		var oldHandler = new NativeFaithfulDisposableContainerHandler(platformView, containerView);
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => oldHandler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();
		var captured = realizer.Realize(page, target);

		oldHandler.DisconnectHandler();
		var replacement = new StubViewHandler(page, mauiContext: target);
		((Element)page).Handler = replacement;

		realizer.Release(page, captured, platformViewDisposed: false);

		Assert.Equal(1, oldHandler.DisposeCount);
		Assert.Equal(1, platformView.DisposeCount);
		Assert.Equal(1, containerView.DisposeCount);
		Assert.Same(replacement, ((Element)page).Handler);
	}

	[Fact]
	public void StackDisposedContainerIsNotDisposedAgainWhileOtherHandlerResourcesAreReleased()
	{
		var platformView = new FakeModalPageRealizer.FakePlatformView();
		var containerView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new NativeFaithfulDisposableContainerHandler(platformView, containerView);
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();
		var captured = realizer.Realize(page, target);
		containerView.Dispose();

		realizer.Release(page, captured, platformViewDisposed: true);

		Assert.Equal(1, handler.DisposeCount);
		Assert.Equal(1, platformView.DisposeCount);
		Assert.Equal(1, containerView.DisposeCount);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public void ReleaseDisposesTheCapturedLiveViewBeforeClearingThePageHandler()
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new DisconnectClearsPlatformViewHandler(
			nativeView,
			() => nativeView.Disposed);
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();
		var platformView = realizer.Realize(page, target);

		realizer.Release(page, platformView, platformViewDisposed: false);

		Assert.True(handler.DisconnectCount >= 1);
		Assert.False(handler.PlatformWasDisposedWhenDisconnected);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public void FailedRealizationDisposesCapturedViewExactlyOnceAfterDisconnectClearsIt()
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new DisconnectClearsPlatformViewHandler(nativeView)
		{
			SetVirtualViewFailure = new InvalidOperationException("bind failed"),
		};
		var target = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var page = new ContentPage();
		var realizer = new TizenModalPageRealizer();

		Assert.Throws<InvalidOperationException>(() => realizer.Realize(page, target));

		Assert.True(handler.DisconnectCount >= 1);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.Null(((Element)page).Handler);
	}

	[Fact]
	public async Task FailedPushRollbackReleasesCapturedLiveViewExactlyOnce()
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new DisconnectClearsPlatformViewHandler(nativeView);
		var context = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var host = new FakeModalNavigationHost(context) { CurrentPage = new ContentPage() };
		var stack = new FakeNavigationStack { PushFailure = new InvalidOperationException("push") };
		var platform = new TizenModalNavigationPlatform(
			host,
			stack,
			new TizenModalPageRealizer());
		var modal = new ContentPage();
		host.RecordPush(modal);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => platform.PushModalAsync(modal, false));

		Assert.True(handler.DisconnectCount >= 1);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.Null(((Element)modal).Handler);
	}

	[Fact]
	public async Task BuriedRemovalReleasesCapturedLiveViewExactlyOnce()
	{
		var nativeView = new FakeModalPageRealizer.FakePlatformView();
		var handler = new DisconnectClearsPlatformViewHandler(nativeView);
		var context = StubMauiContext.WithHandlers(new StubHandlersFactory(() => handler));
		var host = new FakeModalNavigationHost(context) { CurrentPage = new ContentPage() };
		var stack = new FakeNavigationStack();
		var platform = new TizenModalNavigationPlatform(
			host,
			stack,
			new TizenModalPageRealizer());
		var modal = new ContentPage();
		host.RecordPush(modal);
		await platform.PushModalAsync(modal, false);
		await stack.PushAsync(new object(), false);
		host.RecordPop(modal);

		await platform.PopModalAsync(modal, false);

		Assert.True(handler.DisconnectCount >= 1);
		Assert.Equal(1, nativeView.DisposeCount);
		Assert.Null(((Element)modal).Handler);
	}

}

/// <summary>
/// Covers the factory that .NET MAUI will call once per window.
/// </summary>
public class TizenModalNavigationPlatformFactoryTests
{
	static IMauiContext ContextWith(params (Type Service, object Instance)[] services)
	{
		var collection = new ServiceCollection();

		foreach (var (service, instance) in services)
		{
			collection.AddSingleton(service, instance);
		}

		return new StubMauiContext(collection.BuildServiceProvider());
	}

	[Fact]
	public void CreatesAPlatformWhenTheWindowHasANavigationStack()
	{
		var factory = new TizenModalNavigationPlatformFactory(new FakeModalPageRealizer());
		var host = new FakeModalNavigationHost(ContextWith((typeof(ITizenNavigationStack), new FakeNavigationStack())));

		using var platform = factory.CreateModalNavigationPlatform(host);

		Assert.IsType<TizenModalNavigationPlatform>(platform);
	}

	[Fact]
	public void ReturnsNullWhenTheWindowHasNoNavigationStack()
	{
		var factory = new TizenModalNavigationPlatformFactory(new FakeModalPageRealizer());
		var host = new FakeModalNavigationHost(StubMauiContext.Empty());

		// The seam defines null as "keep the built-in platform", so a partially configured host
		// degrades rather than throwing at window creation.
		Assert.Null(factory.CreateModalNavigationPlatform(host));
	}

	[Fact]
	public void ReturnsANewPlatformForEveryWindow()
	{
		var factory = new TizenModalNavigationPlatformFactory(new FakeModalPageRealizer());
		var first = new FakeModalNavigationHost(ContextWith((typeof(ITizenNavigationStack), new FakeNavigationStack())));
		var second = new FakeModalNavigationHost(ContextWith((typeof(ITizenNavigationStack), new FakeNavigationStack())));

		using var a = factory.CreateModalNavigationPlatform(first);
		using var b = factory.CreateModalNavigationPlatform(second);

		Assert.NotSame(a, b);
	}

	[Fact]
	public void DoesNotRegisterASecondPageBackRoute()
	{
		var backButton = new FakeWindowBackButton();
		var factory = new TizenModalNavigationPlatformFactory(new FakeModalPageRealizer());
		var host = new FakeModalNavigationHost(ContextWith(
			(typeof(ITizenNavigationStack), new FakeNavigationStack()),
			(typeof(ITizenWindowBackButton), backButton)));

		using var platform = factory.CreateModalNavigationPlatform(host)!;
		platform.PageAttached();

		Assert.Null(backButton.Handler);
	}
}
