using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers how the Tizen backend plugs into .NET MAUI's dependency injection, including the
/// lifetimes that give alerts their per-window affinity.
/// </summary>
public class TizenServiceRegistrationTests
{
	static ServiceCollection PresentationServices()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ITizenAlertDialogFactory>(new FakeAlertDialogFactory());
		services.AddSingleton<ITizenModalHost>(new FakeModalHost());
		services.AddSingleton<ITizenNativeGestureDetectorFactory>(new FakeNativeGestureDetectorFactory());
		return services;
	}

	[Fact]
	public void AddTizenAlertsRegistersTheAlertManagerAsThePublicMauiContract()
	{
		using var provider = PresentationServices().AddTizenAlerts().BuildServiceProvider();

		using var scope = provider.CreateScope();
		var manager = scope.ServiceProvider.GetService<IAlertManager>();

		Assert.IsType<TizenAlertManager>(manager);
	}

	[Fact]
	public void EachWindowScopeGetsItsOwnAlertManager()
	{
		using var provider = PresentationServices().AddTizenAlerts().BuildServiceProvider();

		using var firstWindow = provider.CreateScope();
		using var secondWindow = provider.CreateScope();

		var first = firstWindow.ServiceProvider.GetRequiredService<IAlertManager>();
		var second = secondWindow.ServiceProvider.GetRequiredService<IAlertManager>();

		// .NET MAUI resolves IAlertManager from the per-window scope. A shared instance would
		// route every window's dialogs through the same window-affine state.
		Assert.NotSame(first, second);
	}

	[Fact]
	public void TheSameWindowScopeReusesItsAlertManager()
	{
		using var provider = PresentationServices().AddTizenAlerts().BuildServiceProvider();
		using var window = provider.CreateScope();

		var first = window.ServiceProvider.GetRequiredService<IAlertManager>();
		var second = window.ServiceProvider.GetRequiredService<IAlertManager>();

		Assert.Same(first, second);
	}

	[Fact]
	public void EachWindowScopeGetsItsOwnWindowContext()
	{
		using var provider = PresentationServices().AddTizenAlerts().BuildServiceProvider();

		using var firstWindow = provider.CreateScope();
		using var secondWindow = provider.CreateScope();

		Assert.NotSame(
			firstWindow.ServiceProvider.GetRequiredService<ITizenWindowContext>(),
			secondWindow.ServiceProvider.GetRequiredService<ITizenWindowContext>());
	}

	[Fact]
	public void SubscriptionOnlyModeRegistersTheSubscriptionInsteadOfTheManager()
	{
		using var provider = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly)
			.BuildServiceProvider();

		using var scope = provider.CreateScope();

		Assert.Null(scope.ServiceProvider.GetService<IAlertManager>());
		Assert.IsType<TizenAlertManagerSubscription>(scope.ServiceProvider.GetService<IAlertManagerSubscription>());
	}

	[Fact]
	public async Task SubscriptionOnlyModeInvokesTheKeyedAlertDelegate()
	{
		var services = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly);
		var page = new ContentPage();
		var calls = 0;

		services.AddKeyedSingleton<Func<Page, AlertArguments, Task<bool>>>(
			TizenDelegateAlertManagerSubscription.DisplayAlertServiceKey,
			(sender, _) =>
			{
				Assert.Same(page, sender);
				calls++;
				return Task.FromResult(true);
			});

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var args = new AlertArguments("Title", "Message", "OK", "Cancel");

		scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>()
			.OnAlertRequested(page, args);

		Assert.True(await args.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(1, calls);
		Assert.Empty(
			Assert.IsType<FakeAlertDialogFactory>(
				scope.ServiceProvider.GetRequiredService<ITizenAlertDialogFactory>())
			.AlertRequests);
	}

	[Fact]
	public async Task SubscriptionOnlyModeInvokesTheKeyedActionSheetDelegate()
	{
		var services = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly);
		var page = new ContentPage();
		var calls = 0;

		services.AddKeyedSingleton<Func<Page, ActionSheetArguments, Task<string?>>>(
			TizenDelegateAlertManagerSubscription.DisplayActionSheetServiceKey,
			(sender, _) =>
			{
				Assert.Same(page, sender);
				calls++;
				return Task.FromResult<string?>("Selected");
			});

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var args = new ActionSheetArguments("Title", "Cancel", null, new[] { "Selected" });

		scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>()
			.OnActionSheetRequested(page, args);

		Assert.Equal("Selected", await args.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(1, calls);
		Assert.Empty(
			Assert.IsType<FakeAlertDialogFactory>(
				scope.ServiceProvider.GetRequiredService<ITizenAlertDialogFactory>())
			.ActionSheetRequests);
	}

	[Fact]
	public async Task SubscriptionOnlyModeInvokesTheKeyedPromptDelegate()
	{
		var services = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly);
		var page = new ContentPage();
		var calls = 0;

		services.AddKeyedSingleton<Func<Page, PromptArguments, Task<string?>>>(
			TizenDelegateAlertManagerSubscription.DisplayPromptServiceKey,
			(sender, _) =>
			{
				Assert.Same(page, sender);
				calls++;
				return Task.FromResult<string?>("Typed");
			});

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var args = new PromptArguments(
			"Title",
			"Message",
			"OK",
			"Cancel",
			"Placeholder",
			10,
			Keyboard.Default,
			string.Empty);

		scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>()
			.OnPromptRequested(page, args);

		Assert.Equal("Typed", await args.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(1, calls);
		Assert.Empty(
			Assert.IsType<FakeAlertDialogFactory>(
				scope.ServiceProvider.GetRequiredService<ITizenAlertDialogFactory>())
			.PromptRequests);
	}

	[Fact]
	public async Task SubscriptionOnlyModeFallsBackPerDialogAndDisposesTheFallback()
	{
		var dialogs = new FakeAlertDialogFactory();
		var windowProvider = new FakeWindowProvider();
		var services = new ServiceCollection();
		services.AddSingleton<ITizenAlertDialogFactory>(dialogs);
		services.AddSingleton<ITizenModalHost>(new FakeModalHost());
		services.AddSingleton<ITizenPlatformWindowProvider>(windowProvider);
		services.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly);

		var alertCalls = 0;
		services.AddKeyedSingleton<Func<Page, AlertArguments, Task<bool>>>(
			TizenDelegateAlertManagerSubscription.DisplayAlertServiceKey,
			(_, _) =>
			{
				alertCalls++;
				return Task.FromResult(true);
			});

		using var provider = services.BuildServiceProvider();
		var scope = provider.CreateScope();
		var context = new StubMauiContext(scope.ServiceProvider);
		var window = new object();
		((TizenWindowContext)scope.ServiceProvider.GetRequiredService<ITizenWindowContext>())
			.Attach(context, window);
		windowProvider.Map(context, window);
		var page = new ContentPage { Handler = new StubViewHandler(mauiContext: context) };
		var subscription = scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>();
		var alert = new AlertArguments("Title", "Message", "OK", "Cancel");
		var actionSheet = new ActionSheetArguments("Title", "Cancel", null, new[] { "Choice" });

		subscription.OnAlertRequested(page, alert);
		subscription.OnActionSheetRequested(page, actionSheet);

		Assert.True(await alert.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal(1, alertCalls);
		var dialog = dialogs.LastActionSheet!;
		Assert.True(dialog.Opened);

		scope.Dispose();

		Assert.True(dialog.Closed);
		Assert.True(dialog.Disposed);
		Assert.Equal("Cancel", await actionSheet.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
	}

	[Fact]
	public async Task SubscriptionOnlyFallbackCreatedDuringDisposalIsStillDisposed()
	{
		using var creationStarted = new ManualResetEventSlim();
		using var allowCreation = new ManualResetEventSlim();
		using var disposeStarted = new ManualResetEventSlim();
		var fallback = new DisposableAlertSubscription();
		using var subscription = new TizenDelegateAlertManagerSubscription(
			static (_, _) => Task.FromResult(true),
			actionSheetHandler: null,
			promptHandler: null,
			() =>
			{
				creationStarted.Set();
				allowCreation.Wait();
				return fallback;
			});
		var args = new ActionSheetArguments("Title", "Cancel", null, new[] { "Choice" });
		var page = new ContentPage();
		var request = Task.Run(() => subscription.OnActionSheetRequested(page, args));

		Assert.True(creationStarted.Wait(TimeSpan.FromSeconds(10)));

		var dispose = Task.Run(() =>
		{
			disposeStarted.Set();
			subscription.Dispose();
		});

		Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(10)));
		await Task.Delay(100);
		allowCreation.Set();
		await Task.WhenAll(request, dispose).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.True(fallback.Disposed);
		Assert.Equal("Fallback", await args.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
	}

	[Fact]
	public async Task SubscriptionOnlyFallbackAcceptsARequestBeforeConcurrentDisposal()
	{
		using var requestStarted = new ManualResetEventSlim();
		using var allowRequest = new ManualResetEventSlim();
		using var disposeStarted = new ManualResetEventSlim();
		var fallback = new BlockingDisposableAlertSubscription(requestStarted, allowRequest);
		using var subscription = new TizenDelegateAlertManagerSubscription(
			static (_, _) => Task.FromResult(true),
			actionSheetHandler: null,
			promptHandler: null,
			() => fallback);
		var args = new ActionSheetArguments("Title", "Cancel", null, new[] { "Choice" });
		var request = Task.Run(() => subscription.OnActionSheetRequested(new ContentPage(), args));

		Assert.True(requestStarted.Wait(TimeSpan.FromSeconds(10)));

		var dispose = Task.Run(() =>
		{
			disposeStarted.Set();
			subscription.Dispose();
		});

		Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(10)));
		await Task.Delay(100);
		allowRequest.Set();
		await Task.WhenAll(request, dispose).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.True(fallback.Disposed);
		Assert.Equal("Fallback", await args.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)));
	}

	[Fact]
	public void SubscriptionOnlyModeIsAlsoPerWindow()
	{
		using var provider = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly)
			.BuildServiceProvider();

		using var firstWindow = provider.CreateScope();
		using var secondWindow = provider.CreateScope();

		Assert.NotSame(
			firstWindow.ServiceProvider.GetRequiredService<IAlertManagerSubscription>(),
			secondWindow.ServiceProvider.GetRequiredService<IAlertManagerSubscription>());
	}

	[Fact]
	public void SubscriptionOnlyModeBindsTheSubscriptionToTheScopesWindow()
	{
		using var provider = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly)
			.BuildServiceProvider();

		using var scope = provider.CreateScope();
		var window = new object();
		((TizenWindowContext)scope.ServiceProvider.GetRequiredService<ITizenWindowContext>())
			.Attach(StubMauiContext.Empty(), window);

		var subscription = (TizenAlertManagerSubscription)scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>();

		Assert.Same(window, subscription.PlatformWindow);
	}

	[Fact]
	public void SubscriptionOnlyModeFallsBackWhenTheServiceProviderDoesNotSupportKeys()
	{
		using var provider = PresentationServices()
			.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly)
			.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var nonKeyedProvider = new NonKeyedServiceProvider(scope.ServiceProvider);

		using var subscription = Assert.IsType<TizenAlertManagerSubscription>(
			TizenControlsServiceCollectionExtensions.CreateSubscriptionOnly(nonKeyedProvider));

		Assert.NotNull(subscription);
	}

	[Fact]
	public void AddTizenGesturesRegistersTheFactoryAsThePublicMauiContract()
	{
		using var provider = PresentationServices().AddTizenGestures().BuildServiceProvider();

		Assert.IsType<TizenGesturePlatformManagerFactory>(provider.GetService<IGesturePlatformManagerFactory>());
	}

	[Fact]
	public void GestureServicesAreSingletons()
	{
		using var provider = PresentationServices().AddTizenGestures().BuildServiceProvider();

		// Gesture handling has no window affinity: the factory creates a fresh manager per handler
		// connection, so a singleton factory is correct and avoids per-window allocation.
		Assert.Same(
			provider.GetRequiredService<IGesturePlatformManagerFactory>(),
			provider.GetRequiredService<IGesturePlatformManagerFactory>());
	}

	// The identity scaler is only correct on a 1x display. Tizen wearables and TVs are not, so a
	// backend that ships identity scaling gets every pan, swipe, pinch, tap and pointer coordinate
	// wrong by the display factor. These tests cover the wiring the platform layer actually uses:
	// AddTizenPixelScaler with a real factor provider, which is exactly what
	// AddTizenNuiControlsPlatform calls with DeviceInfo.ScalingFactor.

	[Fact]
	public void TheRealScalerRegistrationConvertsUsingTheDisplayFactor()
	{
		using var provider = PresentationServices()
			.AddTizenPixelScaler(static () => 2.5)
			.AddTizenGestures()
			.BuildServiceProvider();

		var scaler = provider.GetRequiredService<ITizenPixelScaler>();

		// 100 device pixels at 2.5x is 40 device-independent units, not 100.
		Assert.Equal(40d, scaler.ToScaledDp(100));
	}

	[Fact]
	public void TheRealScalerBeatsTheIdentityFallback()
	{
		// Ordering mirrors AddTizenNuiControlsPlatform: the platform scaler is registered first,
		// and AddTizenGestures' TryAdd must not overwrite it.
		using var provider = PresentationServices()
			.AddTizenPixelScaler(static () => 4)
			.AddTizenControlsPlatform()
			.BuildServiceProvider();

		Assert.Equal(25d, provider.GetRequiredService<ITizenPixelScaler>().ToScaledDp(100));
	}

	[Fact]
	public void WithoutAPlatformScalerTheFallbackIsIdentity()
	{
		using var provider = PresentationServices().AddTizenGestures().BuildServiceProvider();

		Assert.Equal(100d, provider.GetRequiredService<ITizenPixelScaler>().ToScaledDp(100));
	}

	[Theory]
	[InlineData(0d)]
	[InlineData(-1d)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void AnUnusableDisplayFactorFallsBackToIdentityRatherThanThrowing(double factor)
	{
		// This runs during window creation. A mis-scaled UI is a far better failure than an app
		// that will not start.
		using var provider = PresentationServices()
			.AddTizenPixelScaler(() => factor)
			.AddTizenGestures()
			.BuildServiceProvider();

		Assert.Equal(100d, provider.GetRequiredService<ITizenPixelScaler>().ToScaledDp(100));
	}

	[Fact]
	public void TheDisplayFactorIsReadLazily()
	{
		// DeviceInfo is not usable until the Tizen application has initialised, so the provider
		// must not be invoked during registration.
		var reads = 0;

		using var provider = PresentationServices()
			.AddTizenPixelScaler(() => { reads++; return 2; })
			.AddTizenGestures()
			.BuildServiceProvider();

		Assert.Equal(0, reads);

		provider.GetRequiredService<ITizenPixelScaler>();
		provider.GetRequiredService<ITizenPixelScaler>();

		Assert.Equal(1, reads);
	}

	[Fact]
	public void AddTizenControlsPlatformRegistersBothAreas()
	{
		using var provider = PresentationServices().AddTizenControlsPlatform().BuildServiceProvider();
		using var scope = provider.CreateScope();

		Assert.NotNull(scope.ServiceProvider.GetService<IAlertManager>());
		Assert.NotNull(scope.ServiceProvider.GetService<IGesturePlatformManagerFactory>());
		Assert.NotNull(scope.ServiceProvider.GetService<IModalNavigationPlatformFactory>());
	}

	[Fact]
	public void AddTizenModalNavigationRegistersTheFactory()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();

		Assert.IsType<TizenModalNavigationPlatformFactory>(provider.GetService<IModalNavigationPlatformFactory>());
	}

	[Fact]
	public void EachWindowScopeGetsItsOwnNavigationStackHolder()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();

		using var firstWindow = provider.CreateScope();
		using var secondWindow = provider.CreateScope();

		// The navigation stack belongs to the window, so two windows must never share one.
		Assert.NotSame(
			firstWindow.ServiceProvider.GetRequiredService<ITizenNavigationStack>(),
			secondWindow.ServiceProvider.GetRequiredService<ITizenNavigationStack>());
	}

	[Fact]
	public void TheNavigationStackHolderStartsUnattached()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();
		using var scope = provider.CreateScope();

		// Registration happens at host-build time; the native stack only exists once the window
		// handler runs and calls AttachTizenWindow.
		var stack = Assert.IsType<TizenScopedNavigationStack>(scope.ServiceProvider.GetRequiredService<ITizenNavigationStack>());
		Assert.False(stack.IsAttached);
	}

	[Fact]
	public void EachWindowScopeGetsItsOwnBackButtonHolder()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();

		using var firstWindow = provider.CreateScope();
		using var secondWindow = provider.CreateScope();

		Assert.NotSame(
			firstWindow.ServiceProvider.GetRequiredService<ITizenWindowBackButton>(),
			secondWindow.ServiceProvider.GetRequiredService<ITizenWindowBackButton>());
	}

	[Fact]
	public void TheModalNavigationFactoryIsASingleton()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();

		// dotnet/maui#37853 calls the factory once per window and the returned platform holds the
		// per-window state, so the factory itself needs none.
		Assert.Same(
			provider.GetRequiredService<IModalNavigationPlatformFactory>(),
			provider.GetRequiredService<IModalNavigationPlatformFactory>());
	}

	[Fact]
	public void TheModalNavigationFactoryResolvesAPlatformFromAConfiguredWindowScope()
	{
		using var provider = PresentationServices().AddTizenModalNavigation().BuildServiceProvider();
		using var scope = provider.CreateScope();

		var context = new StubMauiContext(scope.ServiceProvider);
		((TizenScopedNavigationStack)scope.ServiceProvider.GetRequiredService<ITizenNavigationStack>())
			.Attach(new FakeNavigationStack());

		var factory = provider.GetRequiredService<IModalNavigationPlatformFactory>();
		using var platform = factory.CreateModalNavigationPlatform(new FakeModalNavigationHost(context));

		Assert.IsType<TizenModalNavigationPlatform>(platform);
	}

	[Fact]
	public void ApplicationRegistrationsWin()
	{
		var services = PresentationServices();
		var custom = new CustomGestureFactory();
		services.AddSingleton<IGesturePlatformManagerFactory>(custom);
		services.AddSingleton<ITizenPixelScaler>(new TizenPixelScaler(2.5));

		using var provider = services.AddTizenGestures().BuildServiceProvider();

		// Registration uses try-add semantics so an application can override any single piece
		// without having to avoid the convenience methods entirely.
		Assert.Same(custom, provider.GetRequiredService<IGesturePlatformManagerFactory>());
		Assert.Equal(4d, provider.GetRequiredService<ITizenPixelScaler>().ToScaledDp(10));
	}

	[Fact]
	public void RegistrationIsIdempotent()
	{
		using var provider = PresentationServices()
			.AddTizenControlsPlatform()
			.AddTizenControlsPlatform()
			.BuildServiceProvider();

		using var scope = provider.CreateScope();

		Assert.IsType<TizenAlertManager>(scope.ServiceProvider.GetService<IAlertManager>());
		Assert.IsType<TizenGesturePlatformManagerFactory>(scope.ServiceProvider.GetService<IGesturePlatformManagerFactory>());
	}

	[Fact]
	public void DisposingAWindowScopeDisposesItsAlertManager()
	{
		using var provider = PresentationServices().AddTizenAlerts().BuildServiceProvider();

		TizenAlertManager manager;

		using (var scope = provider.CreateScope())
		{
			manager = (TizenAlertManager)scope.ServiceProvider.GetRequiredService<IAlertManager>();
			manager.Subscribe();
			Assert.NotNull(manager.Subscription);
		}

		Assert.Null(manager.Subscription);
	}

	sealed class CustomGestureFactory : IGesturePlatformManagerFactory
	{
		public IGesturePlatformManager CreateGesturePlatformManager(IViewHandler handler) =>
			throw new NotSupportedException();
	}

	sealed class DisposableAlertSubscription : IAlertManagerSubscription, IDisposable
	{
		public bool Disposed { get; private set; }

		public void OnAlertRequested(Page sender, AlertArguments arguments) =>
			arguments.SetResult(false);

		public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments) =>
			arguments.SetResult("Fallback");

		public void OnPromptRequested(Page sender, PromptArguments arguments) =>
			arguments.SetResult(null);

		[Obsolete("Exercises the obsolete page-busy notification contract.")]
		public void OnPageBusy(Page sender, bool enabled)
		{
		}

		public void Dispose() => Disposed = true;
	}

	sealed class BlockingDisposableAlertSubscription : IAlertManagerSubscription, IDisposable
	{
		readonly ManualResetEventSlim _requestStarted;
		readonly ManualResetEventSlim _allowRequest;

		public BlockingDisposableAlertSubscription(
			ManualResetEventSlim requestStarted,
			ManualResetEventSlim allowRequest)
		{
			_requestStarted = requestStarted;
			_allowRequest = allowRequest;
		}

		public bool Disposed { get; private set; }

		public void OnAlertRequested(Page sender, AlertArguments arguments) =>
			arguments.SetResult(false);

		public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
		{
			_requestStarted.Set();
			_allowRequest.Wait();

			if (!Disposed)
			{
				arguments.SetResult("Fallback");
			}
		}

		public void OnPromptRequested(Page sender, PromptArguments arguments) =>
			arguments.SetResult(null);

		[Obsolete("Exercises the obsolete page-busy notification contract.")]
		public void OnPageBusy(Page sender, bool enabled)
		{
		}

		public void Dispose() => Disposed = true;
	}

	sealed class NonKeyedServiceProvider : IServiceProvider
	{
		readonly IServiceProvider _inner;

		public NonKeyedServiceProvider(IServiceProvider inner) => _inner = inner;

		public object? GetService(Type serviceType) => _inner.GetService(serviceType);
	}
}
