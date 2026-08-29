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
	internal sealed record UpstreamApiRequest(
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
	internal static class UpstreamApiRequests
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
				"public static DataTemplate? Shell.ResolveFlyoutItemTemplate(Shell? shell, BindableObject flyoutItem) (dotnet/maui#37862, merged upstream but absent from the pinned package)",
				nameof(ShellFlyoutTemplateResolution),
				"Any backend rendering a flyout must resolve which element owns the item template. Only partially reproducible off-tree: the MenuShellItem redirect cannot be expressed because MenuShellItem and its MenuItem property are internal, so bare MenuItems fall back to the shell-level template. dotnet/maui#37862 is merged upstream but not present in the pinned package; the typed adapter and expiry test remain until a package update makes the API available."),
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
				"CORRECTED TWICE. It first read '(none required)', on the grounds that the effect was just a visual state transition GoToState already expresses; that was measured and is wrong. VisualElement.ChangeVisualState is invoked from the IsEnabled property-changed callback, which runs AFTER both PropertyChanging and PropertyChanged, and applies Normal with no knowledge of selection, so recomputing from the event alone is always overwritten on re-enable. It then read as an unfixable gap; that was also wrong. Wave C now dispatches a post-recompute refresh, which lands after the whole set-value operation including ChangeVisualState, so the behaviour is correct on device without any upstream change. The request stands only as an ERGONOMIC one: every backend that renders selection has to discover this ordering and re-dispatch around it, and a supported hook - or selection as a first-class input to the common-state precedence chain - would remove a sharp edge rather than unblock anything."),
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
				"interface IToolbarDrawerToggleVisible { bool DrawerToggleVisible { get; } } (dotnet/maui#37863, merged upstream but absent from the pinned package)",
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
