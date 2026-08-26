using System;
using Microsoft.Extensions.DependencyInjection;
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
}
