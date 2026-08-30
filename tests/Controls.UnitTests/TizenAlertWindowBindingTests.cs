using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers the ordering trap where a page handler is created before the window handler has
/// attached the native window.
/// </summary>
/// <remarks>
/// .NET MAUI does not guarantee that the window's native resources exist before the page's
/// handler is built, so anything that snapshots the window at construction can capture
/// <see langword="null"/> and then silently drop every alert for the window's lifetime. These
/// tests pin the late-bound behaviour that prevents it.
/// </remarks>
public class TizenAlertWindowBindingTests
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	static async Task<T> Completed<T>(Task<T> task)
	{
		var finished = await Task.WhenAny(task, Task.Delay(Timeout));
		Assert.Same(task, finished);
		return await task;
	}

	static AlertArguments Alert() => new("Title", "Message", "OK", "Cancel");

	[Fact]
	public async Task AnAlertRequestedBeforeTheWindowIsAttachedIsStillServiced()
	{
		var dialogs = new FakeAlertDialogFactory();
		var windowContext = new TizenWindowContext();
		var windowProvider = new FakeWindowProvider();

		// Subscribe happens first, while the window is still unattached - the exact ordering that
		// a snapshot-based implementation gets wrong.
		var manager = new TizenAlertManager(windowContext, dialogs, new FakeModalHost(), windowProvider);
		manager.Subscribe();

		var context = StubMauiContext.Empty();
		var page = new ContentPage { Handler = new StubViewHandler(mauiContext: context) };
		var args = Alert();

		manager.RequestAlert(page, args);

		Assert.Single(dialogs.AlertRequests);

		dialogs.LastAlert!.CompleteWith(true);
		Assert.True(await Completed(args.Result.Task));
	}

	[Fact]
	public void TheSubscriptionSeesTheWindowAsSoonAsItIsAttached()
	{
		var windowContext = new TizenWindowContext();
		var manager = new TizenAlertManager(
			windowContext,
			new FakeAlertDialogFactory(),
			new FakeModalHost(),
			new FakeWindowProvider());

		manager.Subscribe();
		var subscription = (TizenAlertManagerSubscription)manager.Subscription!;

		Assert.Null(subscription.PlatformWindow);

		var window = new object();
		windowContext.Attach(StubMauiContext.Empty(), window);

		// No re-subscription needed: the window is resolved per request, not captured.
		Assert.Same(window, subscription.PlatformWindow);
	}

	[Fact]
	public async Task WindowAffinityStillHoldsOnceTheWindowIsAttached()
	{
		var dialogs = new FakeAlertDialogFactory();
		var windowContext = new TizenWindowContext();
		var windowProvider = new FakeWindowProvider();
		var window = new object();

		var ourContext = StubMauiContext.Empty();
		windowContext.Attach(ourContext, window);
		windowProvider.Map(ourContext, window);

		var foreignContext = StubMauiContext.Empty();
		windowProvider.Map(foreignContext, new object());

		var manager = new TizenAlertManager(windowContext, dialogs, new FakeModalHost(), windowProvider);
		manager.Subscribe();

		var foreignPage = new ContentPage { Handler = new StubViewHandler(mauiContext: foreignContext) };
		var foreignArgs = Alert();
		manager.RequestAlert(foreignPage, foreignArgs);

		Assert.Empty(dialogs.AlertRequests);

		var ourPage = new ContentPage { Handler = new StubViewHandler(mauiContext: ourContext) };
		var ourArgs = Alert();
		manager.RequestAlert(ourPage, ourArgs);

		Assert.Single(dialogs.AlertRequests);

		dialogs.LastAlert!.CompleteWith(true);
		Assert.True(await Completed(ourArgs.Result.Task));
	}

	[Fact]
	public void TheRegisteredSubscriptionIsAlsoLateBound()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ITizenAlertDialogFactory>(new FakeAlertDialogFactory());
		services.AddSingleton<ITizenModalHost>(new FakeModalHost());
		services.AddSingleton<ITizenNativeGestureDetectorFactory>(new FakeNativeGestureDetectorFactory());

		using var provider = services.AddTizenAlerts(TizenAlertRegistrationMode.SubscriptionOnly).BuildServiceProvider();
		using var scope = provider.CreateScope();

		// Resolved before the window handler runs, which is the realistic ordering.
		var subscription = (TizenAlertManagerSubscription)scope.ServiceProvider.GetRequiredService<IAlertManagerSubscription>();
		Assert.Null(subscription.PlatformWindow);

		var window = new object();
		((TizenWindowContext)scope.ServiceProvider.GetRequiredService<ITizenWindowContext>())
			.Attach(StubMauiContext.Empty(), window);

		Assert.Same(window, subscription.PlatformWindow);
	}
}
