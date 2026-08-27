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
}
