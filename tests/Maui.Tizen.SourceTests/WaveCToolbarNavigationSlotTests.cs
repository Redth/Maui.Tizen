using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for the toolbar's single navigation icon slot.
/// </summary>
/// <remarks>
/// <para>
/// Back button, drawer toggle and title icon all render into one slot. Title icons load
/// asynchronously, so a completion callback can arrive after the navigation state has already moved
/// on — silently overwriting whichever icon is now correct, or landing out of order behind a second
/// load.
/// </para>
/// <para>
/// The generation guard is pure <c>Microsoft.Maui.Controls</c> logic with no NUI dependency, so it is
/// executed here rather than asserted about. Upstream added the same guard for this race in
/// dotnet/maui#37863; these tests pin the behaviour so Wave C keeps it through adoption, when the
/// drawer-toggle adapter is deleted.
/// </para>
/// </remarks>
public class WaveCToolbarNavigationSlotTests
{
	static Toolbar NewToolbar() => new(new ContentPage());

	// -----------------------------------------------------------------
	// Precedence
	// -----------------------------------------------------------------

	[Fact]
	public void ABackButtonOwnsTheSlot()
	{
		var toolbar = NewToolbar();
		toolbar.BackButtonVisible = true;

		Assert.Equal(
			TizenNavigationIconKind.BackButton,
			TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, drawerToggleVisible: false));
	}

	/// <summary>
	/// The back button wins even while a drawer toggle is available.
	/// </summary>
	/// <remarks>
	/// Back-precedence, not mutual exclusivity: a shell in flyout mode still has a drawer while a
	/// pushed page shows a back button. Only one icon fits, so the precedence lives here rather than
	/// being baked into the capability.
	/// </remarks>
	[Fact]
	public void ABackButtonOutranksAnAvailableDrawerToggle()
	{
		var toolbar = NewToolbar();
		toolbar.BackButtonVisible = true;

		Assert.Equal(
			TizenNavigationIconKind.BackButton,
			TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, drawerToggleVisible: true));
	}

	[Fact]
	public void ADrawerToggleOwnsTheSlotWhenNoBackButtonIsShowing()
	{
		var toolbar = NewToolbar();

		Assert.Equal(
			TizenNavigationIconKind.DrawerToggle,
			TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, drawerToggleVisible: true));
	}

	[Fact]
	public void NothingOwnsTheSlotWhenNeitherIsShowing()
	{
		Assert.Equal(
			TizenNavigationIconKind.None,
			TizenToolbarNavigationSlot.GetNavigationIconKind(NewToolbar(), drawerToggleVisible: false));
	}

	[Fact]
	public void ANullToolbarOwnsNothing()
	{
		Assert.Equal(
			TizenNavigationIconKind.None,
			TizenToolbarNavigationSlot.GetNavigationIconKind(null, drawerToggleVisible: true));
	}

	// -----------------------------------------------------------------
	// Stale async guard
	// -----------------------------------------------------------------

	[Fact]
	public void AnInFlightTitleIconUpdateIsCurrentUntilSomethingSupersedesIt()
	{
		var toolbar = NewToolbar();
		var source = ImageSource.FromFile("icon.png");
		toolbar.TitleIcon = source;

		var generation = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		Assert.True(TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(toolbar, generation, source));
	}

	/// <summary>
	/// A callback that lands after a newer update must be discarded.
	/// </summary>
	/// <remarks>
	/// This is the crash-free but visually wrong case: the image finishes loading after the back
	/// button was raised, and without the guard it overwrites the back button with a title icon.
	/// </remarks>
	[Fact]
	public void ATitleIconCallbackThatLandsAfterANewerUpdateIsDiscarded()
	{
		var toolbar = NewToolbar();
		var source = ImageSource.FromFile("icon.png");
		toolbar.TitleIcon = source;

		var inFlight = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		// The navigation state moves on while the image is still loading.
		toolbar.BackButtonVisible = true;
		TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		Assert.False(
			TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(toolbar, inFlight, source),
			"A title-icon load that completes after a newer navigation update must not overwrite the "
				+ "icon that update chose.");
	}

	/// <summary>
	/// Two racing loads must not land out of order.
	/// </summary>
	[Fact]
	public void TheOlderOfTwoRacingTitleIconLoadsIsDiscarded()
	{
		var toolbar = NewToolbar();

		var first = ImageSource.FromFile("first.png");
		toolbar.TitleIcon = first;
		var firstGeneration = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		var second = ImageSource.FromFile("second.png");
		toolbar.TitleIcon = second;
		var secondGeneration = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		Assert.False(TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(toolbar, firstGeneration, first));
		Assert.True(TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(toolbar, secondGeneration, second));
	}

	/// <summary>
	/// A callback whose source was swapped at the same generation is discarded.
	/// </summary>
	/// <remarks>
	/// The generation alone is not sufficient: the title icon can be replaced without a new
	/// navigation update, and the stale load would otherwise still match.
	/// </remarks>
	[Fact]
	public void ATitleIconCallbackForASupersededSourceIsDiscarded()
	{
		var toolbar = NewToolbar();

		var original = ImageSource.FromFile("original.png");
		toolbar.TitleIcon = original;

		var generation = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

		toolbar.TitleIcon = ImageSource.FromFile("replacement.png");

		Assert.False(
			TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(toolbar, generation, original),
			"A load started for a title icon that has since been replaced must not apply its result.");
	}

	[Fact]
	public void GenerationsAreTrackedPerToolbar()
	{
		var first = NewToolbar();
		var second = NewToolbar();

		var firstSource = ImageSource.FromFile("a.png");
		first.TitleIcon = firstSource;

		var secondSource = ImageSource.FromFile("b.png");
		second.TitleIcon = secondSource;

		var firstGeneration = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(first);

		// Updating another toolbar must not invalidate this one's in-flight load.
		TizenToolbarNavigationSlot.BeginNavigationIconUpdate(second);
		TizenToolbarNavigationSlot.BeginNavigationIconUpdate(second);

		Assert.True(TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(first, firstGeneration, firstSource));
	}

	[Fact]
	public void AnUntrackedToolbarHasNoCurrentUpdate()
		=> Assert.False(TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(
			NewToolbar(),
			generation: 1,
			ImageSource.FromFile("icon.png")));
}
