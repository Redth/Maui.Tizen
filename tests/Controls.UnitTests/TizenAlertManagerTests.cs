using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

public class TizenAlertManagerTests
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	sealed class Fixture
	{
		public Fixture()
		{
			Dialogs = new FakeAlertDialogFactory();
			ModalHost = new FakeModalHost();
			WindowProvider = new FakeWindowProvider();
			WindowContext = new TizenWindowContext();

			Context = StubMauiContext.Empty();
			WindowContext.Attach(Context, Window);
			WindowProvider.Map(Context, Window);

			Page = new ContentPage { Handler = new StubViewHandler(mauiContext: Context) };
			Manager = new TizenAlertManager(WindowContext, Dialogs, ModalHost, WindowProvider);
		}

		public object Window { get; } = new();

		public FakeAlertDialogFactory Dialogs { get; }

		public FakeModalHost ModalHost { get; }

		public FakeWindowProvider WindowProvider { get; }

		public TizenWindowContext WindowContext { get; }

		public StubMauiContext Context { get; }

		public ContentPage Page { get; }

		public TizenAlertManager Manager { get; }
	}

	static async Task<T> Completed<T>(Task<T> task)
	{
		var finished = await Task.WhenAny(task, Task.Delay(Timeout));
		Assert.Same(task, finished);
		return await task;
	}

	static AlertArguments Alert() => new("Title", "Message", "OK", "Cancel");

	[Fact]
	public void SubscribeCreatesASubscriptionBoundToTheWindow()
	{
		var fixture = new Fixture();

		fixture.Manager.Subscribe();

		var subscription = Assert.IsType<TizenAlertManagerSubscription>(fixture.Manager.Subscription);
		Assert.Same(fixture.Window, subscription.PlatformWindow);
	}

	[Fact]
	public void SubscribingTwiceKeepsTheExistingSubscription()
	{
		var fixture = new Fixture();

		fixture.Manager.Subscribe();
		var first = fixture.Manager.Subscription;

		// .NET MAUI calls Subscribe on every page handler change, including when a page that
		// already has a handler is assigned to the window, so this must be a safe no-op.
		fixture.Manager.Subscribe();

		Assert.Same(first, fixture.Manager.Subscription);
	}

	[Fact]
	public void UnsubscribeClearsTheSubscription()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		fixture.Manager.Unsubscribe();

		Assert.Null(fixture.Manager.Subscription);
	}

	[Fact]
	public void UnsubscribingWhileNotSubscribedIsSafe()
	{
		var fixture = new Fixture();

		fixture.Manager.Unsubscribe();
		fixture.Manager.Unsubscribe();

		Assert.Null(fixture.Manager.Subscription);
	}

	[Fact]
	public void ResubscribingCreatesAFreshSubscription()
	{
		var fixture = new Fixture();

		fixture.Manager.Subscribe();
		var first = fixture.Manager.Subscription;

		fixture.Manager.Unsubscribe();
		fixture.Manager.Subscribe();

		Assert.NotNull(fixture.Manager.Subscription);
		Assert.NotSame(first, fixture.Manager.Subscription);
	}

	[Fact]
	public void RepeatedSubscribeUnsubscribeCyclesKeepWorking()
	{
		var fixture = new Fixture();

		for (var i = 0; i < 5; i++)
		{
			fixture.Manager.Subscribe();
			Assert.NotNull(fixture.Manager.Subscription);

			fixture.Manager.Unsubscribe();
			Assert.Null(fixture.Manager.Subscription);
		}

		fixture.Manager.Subscribe();
		fixture.Manager.RequestAlert(fixture.Page, Alert());

		Assert.Single(fixture.Dialogs.AlertRequests);
	}

	[Fact]
	public void RequestsBeforeSubscribeAreIgnored()
	{
		var fixture = new Fixture();

		fixture.Manager.RequestAlert(fixture.Page, Alert());

		Assert.Empty(fixture.Dialogs.AlertRequests);
	}

	[Fact]
	public async Task UnsubscribeDismissesInFlightDialogs()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		var args = Alert();
		fixture.Manager.RequestAlert(fixture.Page, args);

		var dialog = fixture.Dialogs.LastAlert!;
		Assert.True(dialog.Opened);

		// This is the reason the Tizen backend supplies a full IAlertManager rather than only a
		// subscription: the built-in manager would drop the reference and leave this native popup
		// on screen with the caller pending forever.
		fixture.Manager.Unsubscribe();

		Assert.True(dialog.Closed);
		Assert.False(await Completed(args.Result.Task));
	}

	[Fact]
	public void DisposeUnsubscribes()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		fixture.Manager.Dispose();

		Assert.Null(fixture.Manager.Subscription);
	}

	[Fact]
	public void DisposeIsIdempotent()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		fixture.Manager.Dispose();
		fixture.Manager.Dispose();
	}

	[Fact]
	public void SubscribeAfterDisposeThrows()
	{
		var fixture = new Fixture();
		fixture.Manager.Dispose();

		Assert.Throws<ObjectDisposedException>(fixture.Manager.Subscribe);
	}

	[Fact]
	public void RequestsAreForwardedToTheSubscription()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		fixture.Manager.RequestAlert(fixture.Page, Alert());
		fixture.Manager.RequestActionSheet(fixture.Page, new ActionSheetArguments("T", "Cancel", null, new[] { "A" }));
		fixture.Manager.RequestPrompt(fixture.Page, new PromptArguments("T", "M", "OK", "Cancel", " ", 10, Keyboard.Default, string.Empty));

		Assert.Single(fixture.Dialogs.AlertRequests);
		Assert.Single(fixture.Dialogs.ActionSheetRequests);
		Assert.Single(fixture.Dialogs.PromptRequests);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void PageBusyIsForwardedRatherThanDropped()
	{
		var fixture = new Fixture();
		fixture.Manager.Subscribe();

		fixture.Manager.RequestPageBusy(fixture.Page, true);

		Assert.NotNull(fixture.Dialogs.LastBusyIndicator);
		Assert.True(fixture.Dialogs.LastBusyIndicator!.IsOpen);
	}

	[Fact]
	public void ManagerImplementsThePublicMauiContract()
	{
		var fixture = new Fixture();

		Assert.IsAssignableFrom<IAlertManager>(fixture.Manager);
	}

	[Fact]
	public void SubscriptionBindsToTheWindowAvailableAtSubscribeTime()
	{
		var dialogs = new FakeAlertDialogFactory();
		var windowProvider = new FakeWindowProvider();
		var windowContext = new TizenWindowContext();
		var manager = new TizenAlertManager(windowContext, dialogs, new FakeModalHost(), windowProvider);

		// No window attached yet.
		manager.Subscribe();
		Assert.Null(((TizenAlertManagerSubscription)manager.Subscription!).PlatformWindow);

		var window = new object();
		windowContext.Attach(StubMauiContext.Empty(), window);

		manager.Unsubscribe();
		manager.Subscribe();

		Assert.Same(window, ((TizenAlertManagerSubscription)manager.Subscription!).PlatformWindow);
	}
}
