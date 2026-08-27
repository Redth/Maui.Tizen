using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Drives templated-item selection, focus and enabled visuals through the published visual state
	/// contract.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The in-tree Tizen backend set the internal <c>View.IsItemSelected</c> property. An earlier
	/// revision of this adapter replaced that with a single <c>VisualStateManager.GoToState</c> call
	/// per event, which is <b>not</b> equivalent: the internal property was durable state that took
	/// part in every later visual-state recomputation, whereas a one-shot transition stores nothing.
	/// </para>
	/// <para>
	/// Three defects followed from that. Selection was lost the moment anything else recomputed the
	/// state - focusing a selected row dropped it out of <c>Selected</c>, and unfocusing left it in
	/// whatever the focus transition landed on. Selection could also paint over <c>Disabled</c>,
	/// because nothing consulted <see cref="VisualElement.IsEnabled"/>. And recycled rows could not
	/// be reasoned about at all, since the adapter could not answer whether a row was selected.
	/// </para>
	/// <para>
	/// Selection is therefore stored, in an attached <see cref="BindableProperty"/> - the same shape
	/// upstream's own Tizen <c>ShellFlyoutItemView</c> uses for its selected state, and public API,
	/// so no reflection is involved - and every change recomputes the whole state from scratch.
	/// </para>
	/// </remarks>
	public static class ItemSelectionState
	{
		/// <summary>
		/// Backing store for an item's logical selection.
		/// </summary>
		/// <remarks>
		/// Attached rather than held in a side table so it travels with the view, is cleared with it,
		/// and can be read back by a recycling adaptor deciding how to rebind a reused row.
		/// </remarks>
		public static readonly BindableProperty IsItemSelectedProperty =
			BindableProperty.CreateAttached(
				"IsItemSelected",
				typeof(bool),
				typeof(ItemSelectionState),
				false);

		/// <summary>
		/// Backing store for an item's pointer-over state.
		/// </summary>
		/// <remarks>
		/// <see cref="VisualElement"/>'s own <c>IsPointerOver</c> is internal and deliberately not
		/// read here. Tizen's item views drive no pointer-over today, so this stays false; it exists
		/// so the precedence chain below is the real one rather than a truncated copy that would
		/// silently rank selection wrongly if the items layer ever grows hover support.
		/// </remarks>
		public static readonly BindableProperty IsItemPointerOverProperty =
			BindableProperty.CreateAttached(
				"IsItemPointerOver",
				typeof(bool),
				typeof(ItemSelectionState),
				false);

		/// <summary>Gets whether <paramref name="view"/> is logically selected.</summary>
		public static bool GetItemSelected(VisualElement? view)
			=> view is not null && (bool)view.GetValue(IsItemSelectedProperty);

		/// <summary>Gets whether <paramref name="view"/> is logically under the pointer.</summary>
		public static bool GetItemPointerOver(VisualElement? view)
			=> view is not null && (bool)view.GetValue(IsItemPointerOverProperty);

		/// <summary>
		/// Selects or deselects <paramref name="view"/> and re-applies its visual state.
		/// </summary>
		public static void SetItemSelected(VisualElement? view, bool selected)
		{
			if (view is null)
			{
				return;
			}

			view.SetValue(IsItemSelectedProperty, selected);
			UpdateVisualState(view);
		}

		/// <summary>
		/// Sets or clears <paramref name="view"/>'s pointer-over state and re-applies its visual
		/// state.
		/// </summary>
		public static void SetItemPointerOver(VisualElement? view, bool pointerOver)
		{
			if (view is null)
			{
				return;
			}

			view.SetValue(IsItemPointerOverProperty, pointerOver);
			UpdateVisualState(view);
		}

		/// <summary>
		/// Focuses or unfocuses <paramref name="view"/> and re-applies its visual state.
		/// </summary>
		public static void SetItemFocused(VisualElement? view, bool focused)
		{
			if (view is null)
			{
				return;
			}

			// IsFocusedPropertyKey is public, so the focus flag itself needs no adapter. It is set
			// here (rather than at the call site) to keep focus handling in one place.
			view.SetValue(VisualElement.IsFocusedPropertyKey, focused);
			UpdateVisualState(view);
		}

		/// <summary>
		/// Clears every state this adapter owns, for a row about to be recycled.
		/// </summary>
		/// <remarks>
		/// A recycled row keeps its attached values, so without this a reused row can come back
		/// already selected and paint the wrong item.
		/// </remarks>
		public static void Reset(VisualElement? view)
		{
			if (view is null)
			{
				return;
			}

			view.ClearValue(IsItemSelectedProperty);
			view.ClearValue(IsItemPointerOverProperty);
			view.SetValue(VisualElement.IsFocusedPropertyKey, false);
			UpdateVisualState(view);
		}

		/// <summary>
		/// Recomputes <paramref name="view"/>'s common visual state from all of its stored state.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Mirrors <c>VisualElement.ChangeVisualState</c>: a base state chosen by precedence, then
		/// focus applied independently. Disabled outranks everything - a disabled row must not be
		/// painted as selected - then selection, then pointer-over, then normal.
		/// </para>
		/// <para>
		/// Focus is applied as a second transition rather than folded into the chain, exactly as
		/// upstream does, and that ordering is what makes selection durable. <c>Focused</c> and
		/// <c>Unfocused</c> live in the same group as <c>Selected</c>, so if a template authors
		/// <c>Focused</c> it wins while focused; when focus is lost the base state is re-applied
		/// first, so a template that authors no <c>Unfocused</c> state - the common case - lands back
		/// on <c>Selected</c> instead of being stranded in <c>Focused</c>.
		/// </para>
		/// <para>
		/// The <c>Unfocused</c> name is a literal because
		/// <c>VisualStateManager.CommonStates.Unfocused</c> is internal. The state name itself is
		/// part of the public XAML contract that templates are authored against, so this is a
		/// spelling of a documented name and not a use of internal API.
		/// </para>
		/// </remarks>
		static void UpdateVisualState(VisualElement view)
		{
			string baseState = !view.IsEnabled
				? VisualStateManager.CommonStates.Disabled
				: GetItemSelected(view)
					? VisualStateManager.CommonStates.Selected
					: GetItemPointerOver(view)
						? VisualStateManager.CommonStates.PointerOver
						: VisualStateManager.CommonStates.Normal;

			VisualStateManager.GoToState(view, baseState);

			if (view.IsEnabled)
			{
				VisualStateManager.GoToState(
					view,
					view.IsFocused ? VisualStateManager.CommonStates.Focused : "Unfocused");
			}
		}
	}
}
