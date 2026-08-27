using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Describes a concrete API request against <c>dotnet/maui</c> that would let this backend
	/// delete a Tizen-owned adapter.
	/// </summary>
	/// <param name="Id">Stable identifier, also used by the migration status report.</param>
	/// <param name="InternalMember">The internal member the in-tree Tizen backend called.</param>
	/// <param name="RequestedApi">The public API being requested upstream.</param>
	/// <param name="Adapter">The Tizen-owned type currently standing in for it.</param>
	/// <param name="Rationale">Why a published API is the right answer rather than a permanent fork.</param>
	public sealed record UpstreamApiRequest(
		string Id,
		string InternalMember,
		string RequestedApi,
		string Adapter,
		string Rationale);

	/// <summary>
	/// The complete, enumerable set of internal <c>Microsoft.Maui.Controls</c> members that Wave C
	/// had to replace, and what is being asked for upstream in each case.
	/// </summary>
	/// <remarks>
	/// This list is asserted against by the source tests: if a new adapter appears without a
	/// matching entry here, or an entry is left behind after its adapter is deleted, the build
	/// fails. That keeps the migration status report honest instead of letting it rot.
	/// </remarks>
	public static class UpstreamApiRequests
	{
		/// <summary>
		/// The only Wave C dependency with no published equivalent at all.
		/// </summary>
		public const string ToolbarDrawerToggleVisible = "MAUI-TIZEN-API-0009";

		/// <summary>
		/// All tracked requests.
		/// </summary>
		public static IReadOnlyList<UpstreamApiRequest> All { get; } = new UpstreamApiRequest[]
		{
			new(
				"MAUI-TIZEN-API-0001",
				"Microsoft.Maui.Controls.Shell.GetBindableObjectWithFlyoutItemTemplate(BindableObject)",
				"public static DataTemplate? Shell.ResolveFlyoutItemTemplate(Shell? shell, BindableObject flyoutItem) (dotnet/maui#37862, OPEN at head 6cc7f668f0)",
				nameof(ShellFlyoutTemplateResolution),
				"Any backend rendering a flyout must resolve which element owns the item template. Only partially reproducible off-tree: the MenuShellItem redirect cannot be expressed at all because MenuShellItem and its MenuItem property are both internal, so bare MenuItems in a flyout fall back to the shell-level template. Upstream dotnet/maui#37862 is OPEN and its shape is still moving (internal helper -> three-method contract -> single resolve-style call). The adapter stays provisional and no shape is baked in until the design merges and ships in a referenced package; the expiry test matches the concept rather than any proposed name. Adapters/ShellFlyoutTemplateResolution.cs is the adoption seam: its signature already matches the proposed API, so adopting it is a one-line body swap."),
			new(
				"MAUI-TIZEN-API-0002",
				"Microsoft.Maui.Controls.ViewExtensions.FindParentOfType<T>(Element, bool)",
				"public static T? Element.FindParentOfType<T>(this Element, bool includeThis = false)",
				nameof(ShellElementTree),
				"Every out-of-tree handler needs to walk to an owning Shell/Window. The public Parent chain works but each backend reinvents the Application termination rule."),
			new(
				"MAUI-TIZEN-API-0003",
				"Microsoft.Maui.Controls.Shell.GetCurrentShellPage()",
				"public Page? Shell.GetCurrentShellPage()",
				nameof(ShellElementTree),
				"Shell.CurrentPage exists but does not reproduce the navigation-stack-first ordering used for appearance and search resolution."),
			new(
				"MAUI-TIZEN-API-0004",
				"Microsoft.Maui.Controls.Shell.GetEffectiveValue<T>(BindableProperty, T)",
				"public T? Shell.GetEffectiveValue<T>(BindableProperty property, T? defaultValue)",
				nameof(ShellElementTree),
				"Shell property inheritance (SearchHandler, appearance) is a documented Shell behaviour, not a rendering detail; each backend recomputing it is a correctness risk."),
			new(
				"MAUI-TIZEN-API-0005",
				"Microsoft.Maui.Controls.Internals.BooleanBoxes",
				"(none required)",
				"(none)",
				"Allocation-avoidance detail with no behavioural meaning. Replaced with plain bool values; no upstream API is warranted."),
			new(
				"MAUI-TIZEN-API-0006",
				"Microsoft.Maui.Controls.View.IsItemSelected",
				"a public way for item selection to take part in VisualElement.ChangeVisualState - for example a protected virtual hook, or a public Selected input to the common-state precedence chain",
				nameof(ItemSelectionState),
				"CORRECTED. This entry previously read '(none required)', on the grounds that the observable effect was just a visual state transition that VisualStateManager.GoToState already expresses. That was measured and is wrong. The internal property worked because it was read INSIDE ChangeVisualState, so selection took part in the same recompute as Disabled/PointerOver/Normal. From outside it cannot: ChangeVisualState is invoked from the IsEnabled property-changed callback, which runs AFTER both PropertyChanging and PropertyChanged, and it unconditionally applies Normal on re-enable with no knowledge of selection. No public hook runs after it, and VisualStateGroup is sealed with no change notification. Wave C stores selection durably and recomputes on every transition it owns, and the items layer calls ItemSelectionState.Refresh; the residual gap is a re-enable with no other item-state transition, which is device-observable only."),
			new(
				"MAUI-TIZEN-API-0007",
				"Microsoft.Maui.Controls.DataTemplateExtensions.SelectDataTemplate(DataTemplate, object, BindableObject)",
				"public static DataTemplate SelectDataTemplate(this DataTemplate, object, BindableObject)",
				nameof(ShellTemplateResolver),
				"Trivial to reimplement, but it is the canonical selector-unwrapping helper and every backend needs it; publishing it removes duplicated logic."),
			new(
				"MAUI-TIZEN-API-0008",
				"Microsoft.Maui.Controls.Shell.Toolbar",
				"(already satisfied by IToolbarElement.Toolbar)",
				nameof(ShellElementTree),
				"Accessibility only: IToolbarElement is public and Shell implements it, so the adapter is a one-line cast that can be inlined once call sites are updated."),
			new(
				ToolbarDrawerToggleVisible,
				"Microsoft.Maui.Controls.Toolbar.DrawerToggleVisible",
				"interface IToolbarDrawerToggleVisible { bool DrawerToggleVisible { get; } } (dotnet/maui#37863, exact-head APPROVED at 53b9073, not yet merged or packaged)",
				nameof(ToolbarDrawerToggle),
				"Platforms with a drawer cannot render a correct toolbar without knowing whether a drawer toggle is available. Upstream settled on an ADDITIVE capability interface rather than a new IToolbar member, so IToolbar is unchanged and the property is READ-ONLY - the in-tree Tizen write/latch is removed. Wave C therefore computes the value instead of storing it, and renders back-precedence rather than mutual exclusivity."),
			new(
				"MAUI-TIZEN-API-0010",
				"Microsoft.Maui.Controls.Handlers.Items.SelectableItemsView selection synchronisation",
				"(none required)",
				nameof(ItemSelectionSynchronizer),
				"No missing API: SelectedItem, SelectedItems and the native select/unselect requests are all public. The adapter exists because the SYNCHRONISATION RULES - a set difference in both directions, and a guard so the native echo of a push is not applied back - are shared logic that every backend has to get right, and keeping them behind an interface is what makes them executable in a host test instead of device-only."),
			new(
				"MAUI-TIZEN-API-0011",
				"Microsoft.Maui.Controls.ShellSection platform view caching",
				"(none required)",
				nameof(ShellSectionViewCache<,>),
				"No missing API: ShellSection.ToPlatform is public. The helper exists so the lazy-creation and current-section rules are separable from NUI - TizenShellItemView is an NView and cannot be instantiated off-device, so without the split this behaviour could only be asserted about, never executed."),
		};
	}
}
