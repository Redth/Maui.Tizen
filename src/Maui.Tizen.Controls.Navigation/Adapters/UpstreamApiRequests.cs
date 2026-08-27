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
				"(none required)",
				nameof(ItemSelectionState),
				"The observable effect is a visual state transition, which VisualStateManager.GoToState already expresses publicly."),
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
				"bool IToolbar.DrawerToggleVisible { get; set; }",
				nameof(ToolbarDrawerToggle),
				"The only Wave C dependency with no published equivalent. IToolbar already exposes BackButtonVisible and IsVisible; DrawerToggleVisible is the missing third member of the same concept and platforms with a drawer cannot render a correct toolbar without it."),
		};
	}
}
