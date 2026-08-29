using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for the toolbar drawer-toggle capability.
/// </summary>
/// <remarks>
/// <para>
/// The capability is read-only upstream (dotnet/maui#37863 merged and adds
/// <c>IToolbarDrawerToggleVisible</c> additively, but the pinned package does not contain it;
/// <c>IToolbar</c> is unchanged). The in-tree Tizen
/// backend instead <em>wrote</em> a latched <c>drawerToggle &amp;&amp; !backButton</c>, and an
/// earlier revision of this backend reproduced that.
/// </para>
/// <para>
/// These tests pin the two properties that correction has to preserve: the capability is
/// <b>independent of the back button</b>, and it <b>cannot go stale</b>. Both are runtime
/// behaviours, and the adapter is NUI-free, so they are executed here rather than asserted about
/// source.
/// </para>
/// </remarks>
public class WaveCToolbarDrawerToggleTests
{
	static Shell NewShell(FlyoutBehavior behaviour)
	{
		var shell = new Shell { FlyoutBehavior = behaviour };

		shell.Items.Add(new ShellContent { Content = new ContentPage() });

		return shell;
	}

	static IToolbar ToolbarOf(Shell shell) => ((IToolbarElement)shell).Toolbar!;

	static bool Capability(Shell shell) =>
		ToolbarDrawerToggle.GetDrawerToggleVisible(ToolbarOf(shell), shell);

	[Fact]
	public void AFlyoutShellOffersADrawerToggle()
		=> Assert.True(Capability(NewShell(FlyoutBehavior.Flyout)));

	[Theory]
	[InlineData(FlyoutBehavior.Disabled)]
	[InlineData(FlyoutBehavior.Locked)]
	public void ANonFlyoutShellOffersNoDrawerToggle(FlyoutBehavior behaviour)
		=> Assert.False(Capability(NewShell(behaviour)));

	/// <summary>
	/// The capability is independent of the back button - back-precedence, not mutual exclusivity.
	/// </summary>
	/// <remarks>
	/// The removed latch stored <c>drawerToggle &amp;&amp; !backButton</c>, so raising the back
	/// button made the capability itself report <see langword="false"/>. That conflated "a drawer
	/// toggle is available" with "a drawer toggle is what we draw". Only the renderer should apply
	/// precedence; the capability must keep reporting that a drawer exists.
	/// </remarks>
	[Fact]
	public void TheCapabilityStaysTrueWhileABackButtonIsShowing()
	{
		var shell = NewShell(FlyoutBehavior.Flyout);
		var toolbar = ToolbarOf(shell);

		Assert.True(Capability(shell));

		toolbar.BackButtonVisible = true;

		Assert.True(
			ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, shell),
			"The drawer still exists while a back button is showing. Rendering prefers the back "
				+ "button; the capability must not be forced false, which is what the old latch did.");
	}

	/// <summary>
	/// The value tracks flyout behaviour without anything having to write it.
	/// </summary>
	/// <remarks>
	/// This is the staleness the latch had: a stored flag is only as fresh as the last code path
	/// that remembered to update it. Nothing calls a setter here - the value is recomputed on read.
	/// </remarks>
	[Fact]
	public void TheCapabilityTracksFlyoutBehaviourWithoutAnyWrite()
	{
		var shell = NewShell(FlyoutBehavior.Flyout);

		Assert.True(Capability(shell));

		shell.FlyoutBehavior = FlyoutBehavior.Disabled;
		Assert.False(Capability(shell));

		shell.FlyoutBehavior = FlyoutBehavior.Flyout;
		Assert.True(Capability(shell));
	}

	/// <summary>
	/// A toolbar with no known flyout owner offers no drawer toggle.
	/// </summary>
	/// <remarks>
	/// There is no public path from a toolbar to its shell off-tree, so a caller that cannot supply
	/// the owner must get <see langword="false"/> rather than a guess.
	/// </remarks>
	[Fact]
	public void AToolbarWithNoKnownOwnerOffersNoDrawerToggle()
		=> Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(new Toolbar(new ContentPage()), null));

	[Fact]
	public void ANullToolbarOffersNoDrawerToggle()
		=> Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(null, NewShell(FlyoutBehavior.Flyout)));

	/// <summary>
	/// The mapper key must match the upstream member name so adoption does not change it.
	/// </summary>
	[Fact]
	public void TheMapperKeyMatchesTheProposedUpstreamMemberName()
		=> Assert.Equal("DrawerToggleVisible", ToolbarDrawerToggle.DrawerToggleVisiblePropertyName);

	/// <summary>
	/// The adapter must expose no writer.
	/// </summary>
	/// <remarks>
	/// Upstream removed the write path, so re-adding one would reintroduce both the staleness and
	/// the mutual-exclusivity conflation, and would not survive adoption.
	/// </remarks>
	[Fact]
	public void TheAdapterExposesNoWriter()
	{
		var writers = typeof(ToolbarDrawerToggle)
			.GetMethods()
			.Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal))
			.Select(m => m.Name)
			.ToList();

		Assert.True(
			writers.Count == 0,
			"The drawer-toggle capability is read-only upstream; found writer(s): "
				+ string.Join(", ", writers));
	}

	[Theory]
	[InlineData(true, true, true, true)]
	[InlineData(true, false, true, false)]
	[InlineData(false, true, true, false)]
	[InlineData(true, true, false, false)]
	public void BackNavigationRequiresVisibleEnabledBackButton(
		bool backVisible,
		bool backEnabled,
		bool toolbarVisible,
		bool expected)
	{
		var toolbar = new Toolbar(new ContentPage())
		{
			BackButtonVisible = backVisible,
			BackButtonEnabled = backEnabled,
			IsVisible = toolbarVisible,
		};

		Assert.Equal(expected, ToolbarDrawerToggle.ShouldNavigateBack(toolbar));
	}

	// -----------------------------------------------------------------
	// Owner resolution
	// -----------------------------------------------------------------

	/// <summary>
	/// The owner must be discovered from the toolbar's own page, not supplied by the caller.
	/// </summary>
	/// <remarks>
	/// The toolbar handler's virtual view IS the toolbar, so deriving the owner from it (an
	/// <c>as IFlyoutView</c> cast) never matched and left the capability permanently false. The
	/// visible symptom was a shell popping back to its root and rendering an empty navigation slot
	/// where the hamburger belongs.
	/// </remarks>
	[Fact]
	public void TheFlyoutOwnerIsResolvedFromTheToolbarsPage()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		var toolbar = new Toolbar(page);

		Assert.Same(shell, ToolbarDrawerToggle.FindFlyoutOwner(toolbar));
		Assert.True(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null));
	}

	/// <summary>
	/// A toolbar with no flyout ancestor offers no drawer toggle.
	/// </summary>
	[Fact]
	public void APageOutsideAnyFlyoutHasNoDrawerToggle()
	{
		var toolbar = new Toolbar(new ContentPage());

		Assert.Null(ToolbarDrawerToggle.FindFlyoutOwner(toolbar));
		Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null));
	}

	/// <summary>
	/// Popping back to the root restores the drawer toggle without any flyout-behaviour change.
	/// </summary>
	/// <remarks>
	/// This is the exact regression: push makes the back button win the slot, and popping must hand
	/// the slot back to the drawer. If the owner cannot be resolved the capability reads false and
	/// the pop renders an empty slot instead of the hamburger.
	/// </remarks>
	[Fact]
	public void PoppingBackToTheRootRestoresTheDrawerToggle()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		var toolbar = new Toolbar(page);

		// Root: the drawer owns the slot.
		Assert.Equal(
			TizenNavigationIconKind.DrawerToggle,
			TizenToolbarNavigationSlot.GetNavigationIconKind(
				toolbar,
				ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null)));

		// Push: back wins by precedence, while the drawer remains available.
		toolbar.BackButtonVisible = true;
		Assert.True(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null));
		Assert.Equal(
			TizenNavigationIconKind.BackButton,
			TizenToolbarNavigationSlot.GetNavigationIconKind(
				toolbar,
				ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null)));

		// Pop: the drawer takes the slot back. FlyoutBehavior never changed.
		toolbar.BackButtonVisible = false;
		Assert.Equal(
			TizenNavigationIconKind.DrawerToggle,
			TizenToolbarNavigationSlot.GetNavigationIconKind(
				toolbar,
				ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null)));

		Assert.Equal(FlyoutBehavior.Flyout, shell.FlyoutBehavior);
	}

	/// <summary>
	/// A shell whose flyout is disabled offers no drawer toggle even at the root.
	/// </summary>
	[Fact]
	public void ADisabledFlyoutOffersNoDrawerToggle()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Disabled };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(new Toolbar(page), owner: null));
	}

	// -----------------------------------------------------------------
	// Icon-press routing (blocker: back press toggled the drawer too)
	// -----------------------------------------------------------------

	/// <summary>
	/// A back press must not also toggle the drawer.
	/// </summary>
	/// <remarks>
	/// The shell view and the toolbar handler both subscribe to the same <c>IconPressed</c> event.
	/// Gating the drawer side on <c>FlyoutBehavior == Flyout</c> alone meant a back press toggled the
	/// drawer open <em>and</em> popped the stack, because the drawer stays available in flyout mode
	/// while a pushed page shows a back button. Availability is not ownership.
	/// </remarks>
	[Fact]
	public void ABackPressDoesNotToggleTheDrawer()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		var toolbar = new Toolbar(page) { BackButtonVisible = true, IsVisible = true };

		// The drawer is still available - that is exactly the trap.
		Assert.True(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null));
		Assert.False(ToolbarDrawerToggle.ShouldToggleDrawer(toolbar));
	}

	[Fact]
	public void ADrawerPressTogglesTheDrawer()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		Assert.True(ToolbarDrawerToggle.ShouldToggleDrawer(new Toolbar(page) { IsVisible = true }));
	}

	/// <summary>
	/// Popping back to the root hands the press back to the drawer.
	/// </summary>
	[Fact]
	public void PoppingBackRestoresDrawerPressRouting()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		var toolbar = new Toolbar(page) { IsVisible = true };
		Assert.True(ToolbarDrawerToggle.ShouldToggleDrawer(toolbar));

		toolbar.BackButtonVisible = true;
		Assert.False(ToolbarDrawerToggle.ShouldToggleDrawer(toolbar));

		toolbar.BackButtonVisible = false;
		Assert.True(ToolbarDrawerToggle.ShouldToggleDrawer(toolbar));
	}

	/// <summary>
	/// An invisible toolbar routes no press at all.
	/// </summary>
	[Fact]
	public void AnInvisibleToolbarRoutesNoPress()
	{
		var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
		var page = new ContentPage();
		shell.Items.Add(new ShellContent { Content = page });

		Assert.False(ToolbarDrawerToggle.ShouldToggleDrawer(new Toolbar(page) { IsVisible = false }));
	}

	// -----------------------------------------------------------------
	// FlyoutLayoutBehavior changes the drawer capability
	// -----------------------------------------------------------------

	/// <summary>
	/// Switching a FlyoutPage between Popover and Split changes whether a drawer toggle exists,
	/// without touching back-button visibility.
	/// </summary>
	/// <remarks>
	/// This is why re-dispatching <c>FlyoutBehavior</c> is not sufficient on its own: the drawer is
	/// updated but the toolbar's leading slot also has to be redrawn, or the hamburger survives a
	/// switch to Split and a switch back to Popover leaves the slot empty.
	/// </remarks>
	[Fact]
	public void PopoverAndSplitChangeTheDrawerCapabilityWithoutTouchingBack()
	{
		var page = new ContentPage();
		var flyoutPage = new FlyoutPage
		{
			Flyout = new ContentPage { Title = "flyout" },
			Detail = page,
			FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover,
		};

		var toolbar = new Toolbar(page);
		Assert.False(toolbar.BackButtonVisible);

		var owner = (IFlyoutView)flyoutPage;

		Assert.Equal(FlyoutBehavior.Flyout, owner.FlyoutBehavior);
		Assert.True(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner));
		Assert.Equal(
			TizenNavigationIconKind.DrawerToggle,
			TizenToolbarNavigationSlot.GetNavigationIconKind(
				toolbar,
				ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner)));

		flyoutPage.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split;

		Assert.Equal(FlyoutBehavior.Locked, owner.FlyoutBehavior);
		Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner));
		Assert.Equal(
			TizenNavigationIconKind.None,
			TizenToolbarNavigationSlot.GetNavigationIconKind(
				toolbar,
				ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner)));

		// Back visibility never changed - only the drawer capability did.
		Assert.False(toolbar.BackButtonVisible);

		flyoutPage.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover;

		Assert.Equal(FlyoutBehavior.Flyout, owner.FlyoutBehavior);
		Assert.True(ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner));
		Assert.False(toolbar.BackButtonVisible);
	}

	/// <summary>
	/// A Split flyout offers no toggle, so an icon press must not open the drawer.
	/// </summary>
	[Fact]
	public void ASplitFlyoutRoutesNoDrawerPress()
	{
		var page = new ContentPage();
		var flyoutPage = new FlyoutPage
		{
			Flyout = new ContentPage { Title = "flyout" },
			Detail = page,
			FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split,
		};

		Assert.False(ToolbarDrawerToggle.ShouldToggleDrawer(new Toolbar(page) { IsVisible = true }, (IFlyoutView)flyoutPage));
	}
}

/// <summary>
/// Source invariants for icon-press routing.
/// </summary>
/// <remarks>
/// The executable tests above cover the routing predicate, but the defect was in the two call sites
/// that decided a press for themselves. Neither can be instantiated in a host test - one is an NUI
/// view, the other needs a platform handler - so these pin the call sites at source level instead of
/// leaving them unguarded.
/// </remarks>
public class WaveCToolbarIconPressRoutingSourceTests
{
	static string ReadWaveCSource(string fileName)
		=> File.ReadAllText(WaveCSource.Files.Single(f => Path.GetFileName(f) == fileName));

	/// <summary>
	/// The shell view must route through the shared predicate.
	/// </summary>
	/// <remarks>
	/// The original body was <c>if (Shell?.FlyoutBehavior == FlyoutBehavior.Flyout)</c>, which reads
	/// availability rather than ownership and so also fired on a back press.
	/// </remarks>
	[Fact]
	public void TheShellViewRoutesIconPressesThroughTheSharedPredicate()
	{
		var source = ReadWaveCSource("TizenShellView.cs");
		var body = source[source.IndexOf("void OnToolbarIconPressed", StringComparison.Ordinal)..];
		body = body[..body.IndexOf("\n\t\t}", StringComparison.Ordinal)];

		Assert.Contains("ShouldToggleDrawer", body, StringComparison.Ordinal);
		Assert.DoesNotContain("FlyoutBehavior == FlyoutBehavior.Flyout", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The flyout handler must route through the same predicate.
	/// </summary>
	/// <remarks>
	/// Its original guard was <c>!toolbar.BackButtonVisible &amp;&amp; toolbar.IsVisible</c>. That
	/// honoured back precedence but not the drawer capability, so a Split (Locked) flyout - which
	/// offers no toggle at all - still opened its drawer on an icon press.
	/// </remarks>
	[Fact]
	public void TheFlyoutHandlerRoutesIconPressesThroughTheSharedPredicate()
	{
		var source = ReadWaveCSource("TizenFlyoutViewHandler.cs");
		var body = source[source.IndexOf("_toolbarIconPressed = (", StringComparison.Ordinal)..];
		body = body[..body.IndexOf("};", StringComparison.Ordinal)];

		Assert.Contains("ShouldToggleDrawer", body, StringComparison.Ordinal);
		Assert.DoesNotContain("!toolbar.BackButtonVisible", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// A FlyoutLayoutBehavior change must redraw the toolbar's leading slot, not only the drawer.
	/// </summary>
	[Fact]
	public void FlyoutLayoutBehaviorAlsoRefreshesTheToolbarLeadingSlot()
	{
		var source = ReadWaveCSource("TizenFlyoutViewHandler.cs");
		var body = source[source.IndexOf("public static void MapFlyoutLayoutBehavior", StringComparison.Ordinal)..];
		body = body[..body.IndexOf("\n\t\t}", StringComparison.Ordinal)];

		Assert.Contains("UpdateValue(nameof(IFlyoutView.FlyoutBehavior))", body, StringComparison.Ordinal);
		Assert.Contains("RefreshToolbarLeadingIcon", body, StringComparison.Ordinal);
	}
}
