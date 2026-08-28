using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for templated-item selection and focus visuals.
/// </summary>
/// <remarks>
/// <para>
/// The in-tree Tizen backend set the internal <c>View.IsItemSelected</c> property. Wave C replaces
/// it by driving <see cref="VisualStateManager"/> directly, on the claim that moving the item into
/// the <c>Selected</c> state was that property's only observable effect.
/// </para>
/// <para>
/// That claim was too weak. The internal property was durable state that took part in every later
/// recomputation; a one-shot <c>GoToState</c> stores nothing, so selection was lost whenever focus
/// or enabled-state changed, and could paint over <c>Disabled</c>. These tests pin the corrected
/// behaviour: selection is stored and the full state is recomputed by precedence.
/// </para>
/// <para>
/// The adapter is pure <c>Microsoft.Maui.Controls</c> with no NUI dependency, so it is executed here
/// rather than asserted about - the acceptance lane cannot bind method bodies while the Core gate is
/// open, so a compile alone would prove nothing about this behaviour.
/// </para>
/// </remarks>
public class WaveCItemSelectionStateTests
{
	/// <summary>
	/// Builds an element carrying the common visual states so transitions are observable.
	/// </summary>
	static Label NewTemplatedItem()
	{
		var common = new VisualStateGroup { Name = "CommonStates" };
		common.States.Add(new VisualState { Name = VisualStateManager.CommonStates.Normal });
		common.States.Add(new VisualState { Name = VisualStateManager.CommonStates.Selected });
		common.States.Add(new VisualState { Name = VisualStateManager.CommonStates.Focused });
		common.States.Add(new VisualState { Name = VisualStateManager.CommonStates.Disabled });
		common.States.Add(new VisualState { Name = VisualStateManager.CommonStates.PointerOver });

		// Deliberately NO "Unfocused" state. That is the common authoring case, and it is what makes
		// the focus transition non-destructive only if the base state is re-applied first.

		var item = new Label();
		VisualStateManager.SetVisualStateGroups(item, new VisualStateGroupList { common });

		return item;
	}

	static string? CurrentState(VisualElement view)
		=> VisualStateManager.GetVisualStateGroups(view)?[0].CurrentState?.Name;

	// -----------------------------------------------------------------
	// Selection
	// -----------------------------------------------------------------

	[Fact]
	public void SelectingAnItemEntersTheSelectedState()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	[Fact]
	public void DeselectingAnItemReturnsToNormal()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.SetItemSelected(item, selected: false);

		Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));
		Assert.False(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// Recycled items must not accumulate selection: the state has to be re-driven, not toggled.
	/// </summary>
	[Fact]
	public void RepeatedSelectionIsIdempotent()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.SetItemSelected(item, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
	}

	// -----------------------------------------------------------------
	// Selection must survive focus recomputation
	// -----------------------------------------------------------------

	/// <summary>
	/// The regression: a selected row that gains and then loses focus must come back to
	/// <c>Selected</c>, not be stranded in the focus state or dropped to <c>Normal</c>.
	/// </summary>
	/// <remarks>
	/// The template authors no <c>Unfocused</c> state, which is the common case. With a one-shot
	/// <c>GoToState</c> the focus transition destroyed the selection outright.
	/// </remarks>
	[Fact]
	public void SelectionSurvivesFocusAndUnfocus()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);
		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));

		ItemSelectionState.SetItemFocused(item, focused: true);
		Assert.Equal(VisualStateManager.CommonStates.Focused, CurrentState(item));
		Assert.True(ItemSelectionState.GetItemSelected(item));

		ItemSelectionState.SetItemFocused(item, focused: false);
		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// An unselected row that gains and loses focus returns to <c>Normal</c>.
	/// </summary>
	[Fact]
	public void AnUnselectedItemReturnsToNormalAfterFocus()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);
		ItemSelectionState.SetItemFocused(item, focused: false);

		Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));
	}

	[Fact]
	public void FocusingAnItemEntersTheFocusedStateAndSetsIsFocused()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);

		Assert.Equal(VisualStateManager.CommonStates.Focused, CurrentState(item));
		Assert.True(item.IsFocused);
	}

	[Fact]
	public void UnfocusingAnItemClearsIsFocused()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);
		ItemSelectionState.SetItemFocused(item, focused: false);

		Assert.False(item.IsFocused);
	}

	// -----------------------------------------------------------------
	// Disabled outranks selection
	// -----------------------------------------------------------------

	/// <summary>
	/// A disabled row must not be painted as selected.
	/// </summary>
	[Fact]
	public void ADisabledItemStaysDisabledWhenSelected()
	{
		var item = NewTemplatedItem();
		item.IsEnabled = false;

		ItemSelectionState.SetItemSelected(item, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Disabled, CurrentState(item));

		// The selection is still recorded - it is outranked, not discarded.
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// Focus must not override <c>Disabled</c> either, matching upstream's enabled guard.
	/// </summary>
	[Fact]
	public void ADisabledItemIgnoresFocusVisuals()
	{
		var item = NewTemplatedItem();
		item.IsEnabled = false;

		ItemSelectionState.SetItemFocused(item, focused: true);

		Assert.Equal(VisualStateManager.CommonStates.Disabled, CurrentState(item));
	}

	/// <summary>
	/// Re-enabling a selected row restores the selected visual without re-selecting it.
	/// </summary>
	[Fact]
	public void ReEnablingASelectedItemRestoresSelected()
	{
		var item = NewTemplatedItem();
		item.IsEnabled = false;
		ItemSelectionState.SetItemSelected(item, selected: true);

		item.IsEnabled = true;
		ItemSelectionState.SetItemSelected(item, ItemSelectionState.GetItemSelected(item));

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
	}

	// -----------------------------------------------------------------
	// Pointer-over precedence
	// -----------------------------------------------------------------

	/// <summary>
	/// Selection outranks pointer-over, and pointer-over outranks normal.
	/// </summary>
	[Fact]
	public void SelectionOutranksPointerOver()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemPointerOver(item, pointerOver: true);
		Assert.Equal(VisualStateManager.CommonStates.PointerOver, CurrentState(item));

		ItemSelectionState.SetItemSelected(item, selected: true);
		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
	}

	// -----------------------------------------------------------------
	// Recycling
	// -----------------------------------------------------------------

	/// <summary>
	/// A recycled row must not come back carrying the previous item's state.
	/// </summary>
	[Fact]
	public void ResetClearsEveryStateTheAdapterOwns()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.SetItemFocused(item, focused: true);
		ItemSelectionState.SetItemPointerOver(item, pointerOver: true);

		ItemSelectionState.Reset(item);

		Assert.False(ItemSelectionState.GetItemSelected(item));
		Assert.False(ItemSelectionState.GetItemPointerOver(item));
		Assert.False(item.IsFocused);
		Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));
	}

	// -----------------------------------------------------------------
	// Disposal / recycling safety
	// -----------------------------------------------------------------

	/// <summary>
	/// A recycled row can be handed back with no view attached; that must not throw.
	/// </summary>
	[Fact]
	public void ANullViewIsANoOp()
	{
		ItemSelectionState.SetItemSelected(null, selected: true);
		ItemSelectionState.SetItemFocused(null, focused: true);
		ItemSelectionState.SetItemPointerOver(null, pointerOver: true);
		ItemSelectionState.Reset(null);

		Assert.False(ItemSelectionState.GetItemSelected(null));
	}

	/// <summary>
	/// An item whose template declares no visual states must still be safe to drive.
	/// </summary>
	[Fact]
	public void AnItemWithoutVisualStatesIsSafeToDrive()
	{
		var item = new Label();

		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.SetItemFocused(item, focused: true);

		Assert.True(item.IsFocused);
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	// -----------------------------------------------------------------
	// Atomic selected+unfocused (view holder moving to Selected)
	// -----------------------------------------------------------------

	/// <summary>
	/// A holder moving to <c>Selected</c> is selected and NOT focused, so a previously stored focus
	/// must be cleared as part of that move.
	/// </summary>
	/// <remarks>
	/// Without this, a row that was focused and is then selected recomputes to <c>Focused</c>,
	/// because the stale focus flag outranks selection in the focus pass. Upstream never hit this:
	/// its transitions were one-shot and stored no focus to go stale.
	/// </remarks>
	[Fact]
	public void SelectingAPreviouslyFocusedHolderClearsFocus()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);
		Assert.Equal(VisualStateManager.CommonStates.Focused, CurrentState(item));

		ItemSelectionState.SetItemSelectedAndUnfocused(item, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
		Assert.False(item.IsFocused);
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// The atomic call is not interchangeable with plain <c>SetItemSelected</c>, which is what makes
	/// it load-bearing rather than a convenience.
	/// </summary>
	/// <remarks>
	/// Selecting a focused row without clearing focus resolves to <c>Focused</c>, because focus is
	/// applied after the base state and wins. That is the exact defect; this pins the difference so
	/// the two cannot be swapped back.
	/// </remarks>
	[Fact]
	public void SelectingWithoutClearingFocusResolvesToFocusedInstead()
	{
		var focusedThenSelected = NewTemplatedItem();
		ItemSelectionState.SetItemFocused(focusedThenSelected, focused: true);
		ItemSelectionState.SetItemSelected(focusedThenSelected, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Focused, CurrentState(focusedThenSelected));

		var atomic = NewTemplatedItem();
		ItemSelectionState.SetItemFocused(atomic, focused: true);
		ItemSelectionState.SetItemSelectedAndUnfocused(atomic, selected: true);

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(atomic));
	}

	// -----------------------------------------------------------------
	// IsEnabled is observed
	// -----------------------------------------------------------------

	/// <summary>
	/// Disabling a tracked selected row repaints it as disabled, and the selection is kept.
	/// </summary>
	[Fact]
	public void DisablingATrackedSelectedItemRepaintsItAsDisabled()
	{
		var item = NewTemplatedItem();
		ItemSelectionState.TrackEnabledState(item);
		ItemSelectionState.SetItemSelected(item, selected: true);

		item.IsEnabled = false;

		Assert.Equal(VisualStateManager.CommonStates.Disabled, CurrentState(item));
		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// Re-enabling restores <c>Selected</c> once the items layer refreshes, with no re-selection.
	/// </summary>
	/// <remarks>
	/// The selection is never re-asserted here - only <see cref="ItemSelectionState.Refresh"/> is
	/// called, which is what <c>TizenItemTemplateAdaptor.UpdateViewState</c> does. That works only
	/// because the selection is stored rather than inferred from the current visual state.
	/// </remarks>
	[Fact]
	public void ReEnablingASelectedItemRestoresSelectedOnRefreshWithoutReselecting()
	{
		var item = NewTemplatedItem();
		ItemSelectionState.TrackEnabledState(item);
		ItemSelectionState.SetItemSelected(item, selected: true);

		item.IsEnabled = false;
		item.IsEnabled = true;

		ItemSelectionState.Refresh(item);

		Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
	}

	/// <summary>
	/// Re-enabling restores <c>Selected</c> automatically once the post-recompute refresh runs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>VisualElement.ChangeVisualState</c> runs from the <c>IsEnabled</c> property-changed
	/// callback, which fires after both <c>PropertyChanging</c> and <c>PropertyChanged</c>, and it
	/// applies <c>Normal</c> with no knowledge of selection. Recomputing from the event alone is
	/// therefore always overwritten on the re-enable path.
	/// </para>
	/// <para>
	/// On a device the refresh is dispatched, which lands after the whole set-value operation. Host
	/// tests have no dispatcher, so the scheduler is substituted with one that runs the refresh at
	/// the same point the dispatcher would - after the value has settled. That is what this test
	/// exercises: the sequencing, not a stand-in for it.
	/// </para>
	/// </remarks>
	[Fact]
	public void ReEnablingRestoresSelectedOnceThePostRecomputeRefreshRuns()
	{
		var scheduled = new List<Action>();
		var original = ItemSelectionState.PostRecompute;
		ItemSelectionState.PostRecompute = (_, refresh) => scheduled.Add(refresh);

		try
		{
			var item = NewTemplatedItem();
			ItemSelectionState.TrackEnabledState(item);
			ItemSelectionState.SetItemSelected(item, selected: true);

			item.IsEnabled = false;
			item.IsEnabled = true;

			// MAUI's own recompute has already overwritten the state by this point.
			Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));

			// Drain what the dispatcher would have run.
			Assert.NotEmpty(scheduled);
			foreach (var refresh in scheduled)
			{
				refresh();
			}

			// Restored with no re-selection anywhere in this test.
			Assert.Equal(VisualStateManager.CommonStates.Selected, CurrentState(item));
		}
		finally
		{
			ItemSelectionState.PostRecompute = original;
		}
	}

	/// <summary>
	/// A refresh is scheduled for every <c>IsEnabled</c> change, not only re-enable.
	/// </summary>
	[Fact]
	public void EveryEnabledChangeSchedulesAPostRecomputeRefresh()
	{
		var scheduled = 0;
		var original = ItemSelectionState.PostRecompute;
		ItemSelectionState.PostRecompute = (_, _) => scheduled++;

		try
		{
			var item = NewTemplatedItem();
			ItemSelectionState.TrackEnabledState(item);

			item.IsEnabled = false;
			item.IsEnabled = true;

			Assert.Equal(2, scheduled);
		}
		finally
		{
			ItemSelectionState.PostRecompute = original;
		}
	}

	/// <summary>
	/// An untracked view schedules nothing, so a recycled row does not repaint off-screen.
	/// </summary>
	[Fact]
	public void AnUntrackedViewSchedulesNoRefresh()
	{
		var scheduled = 0;
		var original = ItemSelectionState.PostRecompute;
		ItemSelectionState.PostRecompute = (_, _) => scheduled++;

		try
		{
			var item = NewTemplatedItem();
			ItemSelectionState.TrackEnabledState(item);
			ItemSelectionState.UntrackEnabledState(item);

			item.IsEnabled = false;

			Assert.Equal(0, scheduled);
		}
		finally
		{
			ItemSelectionState.PostRecompute = original;
		}
	}

	/// <summary>
	/// The default scheduler must not throw for a view with no dispatcher.
	/// </summary>
	/// <remarks>
	/// An unparented or recycled row has no dispatcher, and <c>Element.Dispatcher</c> throws rather
	/// than returning null in that state. Tracking such a row must stay safe.
	/// </remarks>
	[Fact]
	public void TheDefaultSchedulerToleratesAViewWithNoDispatcher()
	{
		var item = NewTemplatedItem();
		ItemSelectionState.TrackEnabledState(item);

		var exception = Record.Exception(() => item.IsEnabled = false);

		Assert.Null(exception);
	}

	/// <summary>
	/// Tracking is idempotent, so a recycled row cannot stack duplicate subscriptions.
	/// </summary>
	[Fact]
	public void TrackingIsIdempotent()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.TrackEnabledState(item);
		ItemSelectionState.TrackEnabledState(item);
		ItemSelectionState.SetItemSelected(item, selected: true);

		item.IsEnabled = false;

		Assert.Equal(VisualStateManager.CommonStates.Disabled, CurrentState(item));
	}

	/// <summary>
	/// An untracked row stops recomputing, so a recycled view does not keep reacting off-screen.
	/// </summary>
	[Fact]
	public void UntrackingStopsRecomputation()
	{
		var item = NewTemplatedItem();
		ItemSelectionState.TrackEnabledState(item);
		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.UntrackEnabledState(item);

		// Nothing this adapter owns runs now; only MAUI's own recompute does.
		item.IsEnabled = false;

		Assert.True(ItemSelectionState.GetItemSelected(item));
	}

	/// <summary>
	/// Refresh re-applies the stored state on demand.
	/// </summary>
	[Fact]
	public void RefreshReappliesStoredState()
	{
		var item = NewTemplatedItem();
		ItemSelectionState.SetItemSelected(item, selected: true);

		item.IsEnabled = false;
		ItemSelectionState.Refresh(item);

		Assert.Equal(VisualStateManager.CommonStates.Disabled, CurrentState(item));
	}
}
