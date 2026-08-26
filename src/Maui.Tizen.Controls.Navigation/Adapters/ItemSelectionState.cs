using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Drives item selection visuals through the published visual state contract.
	/// </summary>
	/// <remarks>
	/// The in-tree Tizen backend set the internal <c>View.IsItemSelected</c> property, whose only
	/// observable effect was to move the templated item into or out of the
	/// <see cref="VisualStateManager.CommonStates.Selected"/> state. Driving
	/// <see cref="VisualStateManager"/> directly is the published equivalent and removes the last
	/// selection-related internal dependency.
	/// </remarks>
	public static class ItemSelectionState
	{
		/// <summary>Moves <paramref name="view"/> into the selected or normal visual state.</summary>
		public static void SetItemSelected(VisualElement? view, bool selected)
		{
			if (view is null)
			{
				return;
			}

			VisualStateManager.GoToState(
				view,
				selected ? VisualStateManager.CommonStates.Selected : VisualStateManager.CommonStates.Normal);
		}

		/// <summary>Moves <paramref name="view"/> into or out of the focused visual state.</summary>
		public static void SetItemFocused(VisualElement? view, bool focused)
		{
			if (view is null)
			{
				return;
			}

			VisualStateManager.GoToState(
				view,
				focused ? VisualStateManager.CommonStates.Focused : VisualStateManager.CommonStates.Normal);

			// IsFocusedPropertyKey is public, so the focus flag itself needs no adapter. It is set
			// here (rather than at the call site) to keep focus handling in one place.
			view.SetValue(VisualElement.IsFocusedPropertyKey, focused);
		}
	}
}
