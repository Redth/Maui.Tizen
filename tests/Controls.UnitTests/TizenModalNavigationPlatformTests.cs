using System;
using Microsoft.Extensions.DependencyInjection;
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
		var (platform, host, stack, _, _) = Build();
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
		var (platform, host, stack, _, _) = Build();
		using var _p = platform;

		stack.PushFailure = new InvalidOperationException("native failure");

		var modal = new ContentPage();
		host.RecordPush(modal);

		// The framework rolls the platform stack back and rethrows to the PushModalAsync caller,
		// so the platform must not swallow this.
		await Assert.ThrowsAsync<InvalidOperationException>(() => platform.PushModalAsync(modal, false));
	}

	[Fact]
	public void PageAttachedInstallsTheBackButtonHandler()
	{
		var (platform, _, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();

		Assert.NotNull(backButton.Handler);
	}

	[Fact]
	public void PageAttachedIsSafeToCallRepeatedly()
	{
		var (platform, _, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();
		platform.PageAttached();

		Assert.Equal(2, backButton.SetCount);
		Assert.NotNull(backButton.Handler);
	}

	[Fact]
	public void TheBackButtonHandlerResolvesTheCurrentPageOnEveryPress()
	{
		var (platform, host, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();

		var first = new ContentPage();
		host.CurrentPage = first;
		Assert.False(backButton.Handler!());

		// The current page changes as modals come and go, so the handler must not capture it.
		var second = new BackHandlingPage();
		host.CurrentPage = second;
		Assert.True(backButton.Handler!());
	}

	[Fact]
	public void TheBackButtonHandlerIsSafeWhenThereIsNoCurrentPage()
	{
		var (platform, host, _, _, backButton) = Build();
		using var _p = platform;

		platform.PageAttached();
		host.CurrentPage = null;

		Assert.False(backButton.Handler!());
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
	public void DisposeClearsTheBackButtonHandler()
	{
		var (platform, _, _, _, backButton) = Build();

		platform.PageAttached();
		platform.Dispose();

		Assert.Null(backButton.Handler);
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

	sealed class BackHandlingPage : ContentPage
	{
		protected override bool OnBackButtonPressed() => true;
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
	public void PicksUpTheWindowsBackButtonWhenOneIsRegistered()
	{
		var backButton = new FakeWindowBackButton();
		var factory = new TizenModalNavigationPlatformFactory(new FakeModalPageRealizer());
		var host = new FakeModalNavigationHost(ContextWith(
			(typeof(ITizenNavigationStack), new FakeNavigationStack()),
			(typeof(ITizenWindowBackButton), backButton)));

		using var platform = factory.CreateModalNavigationPlatform(host)!;
		platform.PageAttached();

		Assert.NotNull(backButton.Handler);
	}
}
