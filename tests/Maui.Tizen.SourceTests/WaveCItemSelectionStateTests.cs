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
/// That claim is the whole justification for dropping the internal dependency, and until now nothing
/// checked it. The adapter is pure <c>Microsoft.Maui.Controls</c> with no NUI dependency, so it is
/// executed here rather than asserted about - the acceptance lane cannot bind method bodies while
/// the Core gate is open, so a compile alone would prove nothing about this behaviour.
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
	}

	[Fact]
	public void DeselectingAnItemReturnsToNormal()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemSelected(item, selected: true);
		ItemSelectionState.SetItemSelected(item, selected: false);

		Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));
	}

	/// <summary>
	/// Recycled items must not accumulate selection: the visual state has to be re-driven, not
	/// toggled.
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
	// Focus
	// -----------------------------------------------------------------

	[Fact]
	public void FocusingAnItemEntersTheFocusedStateAndSetsIsFocused()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);

		Assert.Equal(VisualStateManager.CommonStates.Focused, CurrentState(item));
		Assert.True(item.IsFocused);
	}

	[Fact]
	public void UnfocusingAnItemReturnsToNormalAndClearsIsFocused()
	{
		var item = NewTemplatedItem();

		ItemSelectionState.SetItemFocused(item, focused: true);
		ItemSelectionState.SetItemFocused(item, focused: false);

		Assert.Equal(VisualStateManager.CommonStates.Normal, CurrentState(item));
		Assert.False(item.IsFocused);
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
	}
}
