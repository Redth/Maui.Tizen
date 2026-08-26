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

	[Fact]
	public void AddTizenControlsPlatformRegistersBothAreas()
	{
		using var provider = PresentationServices().AddTizenControlsPlatform().BuildServiceProvider();
		using var scope = provider.CreateScope();

		Assert.NotNull(scope.ServiceProvider.GetService<IAlertManager>());
		Assert.NotNull(scope.ServiceProvider.GetService<IGesturePlatformManagerFactory>());
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
