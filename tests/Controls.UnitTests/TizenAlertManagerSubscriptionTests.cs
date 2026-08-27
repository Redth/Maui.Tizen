using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

public class TizenAlertManagerSubscriptionTests
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	sealed class Fixture
	{
		public Fixture(object? window = null)
		{
			Window = window ?? new object();
			Dialogs = new FakeAlertDialogFactory();
			ModalHost = new FakeModalHost();
			WindowProvider = new FakeWindowProvider();

			Context = StubMauiContext.Empty();
			WindowProvider.Map(Context, Window);

			Page = new ContentPage { Handler = new StubViewHandler(mauiContext: Context) };

			Subscription = new TizenAlertManagerSubscription(Window, Dialogs, ModalHost, WindowProvider);
		}

		public object Window { get; }

		public FakeAlertDialogFactory Dialogs { get; }

		public FakeModalHost ModalHost { get; }

		public FakeWindowProvider WindowProvider { get; }

		public StubMauiContext Context { get; }

		public ContentPage Page { get; }

		public TizenAlertManagerSubscription Subscription { get; }

		/// <summary>Creates a page that belongs to a different window.</summary>
		public ContentPage CreateForeignPage()
		{
			var foreignContext = StubMauiContext.Empty();
			WindowProvider.Map(foreignContext, new object());
			return new ContentPage { Handler = new StubViewHandler(mauiContext: foreignContext) };
		}
	}

	static async Task<T> Completed<T>(Task<T> task)
	{
		var finished = await Task.WhenAny(task, Task.Delay(Timeout));
		Assert.Same(task, finished);
		return await task;
	}

	static async Task<bool> NeverCompletes<T>(Task<T> task)
	{
		var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(200)));
		return finished != task;
	}

	static AlertArguments Alert() => new("Title", "Message", "OK", "Cancel");

	static ActionSheetArguments ActionSheet() => new("Title", "Cancel", "Delete", new[] { "One", "Two" });

	static PromptArguments Prompt() => new("Title", "Message", "OK", "Cancel", placeholder: " ", maxLength: 10, keyboard: Keyboard.Default, initialValue: string.Empty);

	[Fact]
	public async Task AlertResultIsPropagatedToCaller()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);

		fixture.Dialogs.LastAlert!.CompleteWith(true);

		Assert.True(await Completed(args.Result.Task));
	}

	[Fact]
	public async Task AlertCancellationYieldsFalse()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);
		fixture.Dialogs.LastAlert!.Close();

		Assert.False(await Completed(args.Result.Task));
	}

	[Fact]
	public async Task ActionSheetResultIsPropagatedToCaller()
	{
		var fixture = new Fixture();
		var args = ActionSheet();

		fixture.Subscription.OnActionSheetRequested(fixture.Page, args);
		fixture.Dialogs.LastActionSheet!.CompleteWith("Two");

		Assert.Equal("Two", await Completed(args.Result.Task));
	}

	[Fact]
	public async Task ActionSheetCancellationYieldsCancelLabel()
	{
		var fixture = new Fixture();
		var args = ActionSheet();

		fixture.Subscription.OnActionSheetRequested(fixture.Page, args);
		fixture.Dialogs.LastActionSheet!.Close();

		Assert.Equal("Cancel", await Completed(args.Result.Task));
	}

	[Fact]
	public async Task PromptResultIsPropagatedToCaller()
	{
		var fixture = new Fixture();
		var args = Prompt();

		fixture.Subscription.OnPromptRequested(fixture.Page, args);
		fixture.Dialogs.LastPrompt!.CompleteWith("typed");

		Assert.Equal("typed", await Completed(args.Result.Task));
	}

	[Fact]
	public async Task PromptCancellationYieldsNull()
	{
		var fixture = new Fixture();
		var args = Prompt();

		fixture.Subscription.OnPromptRequested(fixture.Page, args);
		fixture.Dialogs.LastPrompt!.Close();

		Assert.Null(await Completed(args.Result.Task));
	}

	[Fact]
	public async Task DialogIsDisposedOnceTheRequestCompletes()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);
		var dialog = fixture.Dialogs.LastAlert!;
		dialog.CompleteWith(false);

		await Completed(args.Result.Task);

		Assert.True(dialog.Disposed);
	}

	[Fact]
	public async Task DialogDisposalFailureFaultsTheCallerInsteadOfLeavingItPending()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);
		var dialog = fixture.Dialogs.LastAlert!;
		dialog.DisposeFailure = new InvalidOperationException("dispose failed");
		dialog.CompleteWith(true);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Completed(args.Result.Task));
		Assert.Equal("dispose failed", exception.Message);
	}

	[Fact]
	public async Task DisposedRaceDialogDisposalFailureFaultsTheCaller()
	{
		var fixture = new Fixture();
		var args = Alert();
		fixture.Dialogs.BeforeCreateAlertDialog = fixture.Subscription.Dispose;
		fixture.Dialogs.AlertDialogDisposeFailure = new InvalidOperationException("dispose failed");

		fixture.Subscription.OnAlertRequested(fixture.Page, args);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Completed(args.Result.Task));
		Assert.Equal("dispose failed", exception.Message);
	}

	[Fact]
	public async Task ModalStackIsPushedAndPoppedInBalance()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);

		Assert.Equal(1, fixture.ModalHost.Entered);

		fixture.Dialogs.LastAlert!.CompleteWith(true);
		await Completed(args.Result.Task);

		Assert.True(fixture.ModalHost.IsBalanced);
	}

	[Fact]
	public async Task RequestFromAnotherWindowIsIgnored()
	{
		var fixture = new Fixture();
		var foreignPage = fixture.CreateForeignPage();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(foreignPage, args);

		Assert.Empty(fixture.Dialogs.AlertRequests);
		Assert.Equal(0, fixture.ModalHost.Entered);
		Assert.True(await NeverCompletes(args.Result.Task));
	}

	[Fact]
	public async Task UnexpectedDialogFailureFaultsTheCallerInsteadOfHanging()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);
		fixture.Dialogs.LastAlert!.FailWith(new InvalidOperationException("native failure"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Completed(args.Result.Task));
	}

	[Fact]
	public async Task ModalStackFailureCancelsTheCallerInsteadOfHanging()
	{
		var dialogs = new FakeAlertDialogFactory();
		var windowProvider = new FakeWindowProvider();
		var window = new object();
		var context = StubMauiContext.Empty();
		windowProvider.Map(context, window);

		var subscription = new TizenAlertManagerSubscription(
			window,
			dialogs,
			new ThrowingModalHost(new TaskCanceledException()),
			windowProvider);

		var page = new ContentPage { Handler = new StubViewHandler(mauiContext: context) };
		var args = Alert();

		subscription.OnAlertRequested(page, args);

		Assert.False(await Completed(args.Result.Task));
	}

	[Fact]
	public async Task DisposeDismissesInFlightDialogsAndCancelsCallers()
	{
		var fixture = new Fixture();
		var args = Alert();

		fixture.Subscription.OnAlertRequested(fixture.Page, args);
		var dialog = fixture.Dialogs.LastAlert!;

		Assert.True(dialog.Opened);

		fixture.Subscription.Dispose();

		Assert.True(dialog.Closed);
		Assert.False(await Completed(args.Result.Task));
	}

	[Fact]
	public void RequestsAfterDisposeAreIgnored()
	{
		var fixture = new Fixture();
		fixture.Subscription.Dispose();

		fixture.Subscription.OnAlertRequested(fixture.Page, Alert());
		fixture.Subscription.OnActionSheetRequested(fixture.Page, ActionSheet());
		fixture.Subscription.OnPromptRequested(fixture.Page, Prompt());

		Assert.Empty(fixture.Dialogs.AlertRequests);
		Assert.Empty(fixture.Dialogs.ActionSheetRequests);
		Assert.Empty(fixture.Dialogs.PromptRequests);
	}

	[Fact]
	public void DisposeIsIdempotent()
	{
		var fixture = new Fixture();

		fixture.Subscription.Dispose();
		fixture.Subscription.Dispose();
	}

	[Fact]
	public async Task ConcurrentDialogsAreTrackedIndependently()
	{
		var fixture = new Fixture();
		var alert = Alert();
		var prompt = Prompt();

		fixture.Subscription.OnAlertRequested(fixture.Page, alert);
		fixture.Subscription.OnPromptRequested(fixture.Page, prompt);

		fixture.Dialogs.LastPrompt!.CompleteWith("value");
		Assert.Equal("value", await Completed(prompt.Result.Task));

		fixture.Dialogs.LastAlert!.CompleteWith(true);
		Assert.True(await Completed(alert.Result.Task));

		Assert.Equal(2, fixture.ModalHost.Entered);
		Assert.True(fixture.ModalHost.IsBalanced);
	}

	// Page busy is obsolete upstream, but Page.IsBusy still routes through it, so the Tizen
	// backend keeps honouring it. These tests pin the behaviour explicitly.

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void PageBusyOpensTheIndicator()
	{
		var fixture = new Fixture();

		fixture.Subscription.OnPageBusy(fixture.Page, true);

		Assert.NotNull(fixture.Dialogs.LastBusyIndicator);
		Assert.True(fixture.Dialogs.LastBusyIndicator!.IsOpen);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void NestedPageBusyScopesKeepTheIndicatorOpenUntilTheLastOneCloses()
	{
		var fixture = new Fixture();

		fixture.Subscription.OnPageBusy(fixture.Page, true);
		fixture.Subscription.OnPageBusy(fixture.Page, true);

		var indicator = fixture.Dialogs.LastBusyIndicator!;

		// The original NUI implementation closed the popup on the second "busy" notification.
		// Reference-counted nesting is the corrected behaviour; see TizenAlertManagerSubscription.
		Assert.True(indicator.IsOpen);

		fixture.Subscription.OnPageBusy(fixture.Page, false);
		Assert.True(indicator.IsOpen);

		fixture.Subscription.OnPageBusy(fixture.Page, false);
		Assert.False(indicator.IsOpen);
		Assert.True(indicator.Disposed);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void PageBusyCountNeverGoesNegative()
	{
		var fixture = new Fixture();

		fixture.Subscription.OnPageBusy(fixture.Page, false);
		fixture.Subscription.OnPageBusy(fixture.Page, false);
		fixture.Subscription.OnPageBusy(fixture.Page, true);

		Assert.True(fixture.Dialogs.LastBusyIndicator!.IsOpen);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void PageBusyFromAnotherWindowIsIgnored()
	{
		var fixture = new Fixture();
		var foreignPage = fixture.CreateForeignPage();

		fixture.Subscription.OnPageBusy(foreignPage, true);

		Assert.Equal(0, fixture.Dialogs.BusyIndicatorsCreated);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void DisposeTearsDownTheBusyIndicator()
	{
		var fixture = new Fixture();
		fixture.Subscription.OnPageBusy(fixture.Page, true);

		var indicator = fixture.Dialogs.LastBusyIndicator!;
		fixture.Subscription.Dispose();

		Assert.False(indicator.IsOpen);
		Assert.True(indicator.Disposed);
	}

	[Fact]
	[Obsolete("Exercises the obsolete page-busy notification on purpose.")]
	public void BusyIndicatorIsDisposedEvenWhenCloseFails()
	{
		var fixture = new Fixture();
		fixture.Subscription.OnPageBusy(fixture.Page, true);
		var indicator = fixture.Dialogs.LastBusyIndicator!;
		indicator.CloseFailure = new InvalidOperationException("close failed");

		Assert.Throws<InvalidOperationException>(() => fixture.Subscription.OnPageBusy(fixture.Page, false));
		Assert.True(indicator.Disposed);
	}

	[Fact]
	public void DisposeContinuesThroughEveryOpenDialogWhenOneCloseFails()
	{
		var fixture = new Fixture();
		var alert = Alert();
		var prompt = Prompt();
		fixture.Subscription.OnAlertRequested(fixture.Page, alert);
		var alertDialog = fixture.Dialogs.LastAlert!;
		fixture.Subscription.OnPromptRequested(fixture.Page, prompt);
		var promptDialog = fixture.Dialogs.LastPrompt!;
		alertDialog.CloseFailure = new InvalidOperationException("close failed");

		Assert.Throws<AggregateException>(fixture.Subscription.Dispose);

		Assert.True(alertDialog.Disposed);
		Assert.True(promptDialog.Closed);
		Assert.True(promptDialog.Disposed);
	}
}
