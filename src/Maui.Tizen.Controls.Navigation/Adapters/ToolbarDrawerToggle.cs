using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Reads whether a toolbar can offer a drawer (hamburger) toggle.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Adoption seam for the additive capability proposed by dotnet/maui#37863:
	/// <c>public interface IToolbarDrawerToggleVisible { bool DrawerToggleVisible { get; } }</c>.
	/// <see cref="IToolbar"/> itself is unchanged, so this is additive rather than breaking.
	/// </para>
	/// <para>
	/// <b>The capability is READ-ONLY, which is a behavioural correction rather than a detail.</b>
	/// The in-tree Tizen backend <em>wrote</em> a latched flag onto the toolbar
	/// (<c>Toolbar.DrawerToggleVisible = ...</c>), and an earlier revision of this backend
	/// reproduced that faithfully with a <c>ConditionalWeakTable</c>. Upstream has removed the write
	/// path, so the value is computed by Controls and only observed by a backend.
	/// </para>
	/// <para>
	/// Dropping the latch removes a real staleness hazard: a stored flag is only as fresh as the last
	/// code path that remembered to update it, so any state change not routed through that path left
	/// the toolbar drawing a stale icon. Computing on read cannot go stale.
	/// </para>
	/// <para>
	/// <b>Back-precedence, not mutual exclusivity.</b> The latch stored
	/// <c>drawerToggle &amp;&amp; !backButton</c>, conflating "a drawer toggle is available" with
	/// "a drawer toggle is what we draw". Those are different questions. This answers only the
	/// first, and stays <see langword="true"/> while a back button is showing; choosing the icon is
	/// the renderer's job and it prefers the back button - see
	/// <c>TizenToolbarExtensions.UpdateBackButton</c>.
	/// </para>
	/// <para>
	/// Once #37863 merges and reaches the referenced package, the body below becomes a pattern match
	/// on the real interface and this adapter is deleted - see
	/// <see cref="UpstreamApiRequests.ToolbarDrawerToggleVisible"/>.
	/// </para>
	/// </remarks>
	public static class ToolbarDrawerToggle
	{
		/// <summary>
		/// Mapper key for the capability, matching the proposed
		/// <c>IToolbarDrawerToggleVisible.DrawerToggleVisible</c>.
		/// </summary>
		/// <remarks>
		/// A constant so the mapper key survives adoption unchanged: the upstream member has the same
		/// name, so this is later swapped for
		/// <c>nameof(IToolbarDrawerToggleVisible.DrawerToggleVisible)</c> with an identical string.
		/// </remarks>
		public const string DrawerToggleVisiblePropertyName = "DrawerToggleVisible";

		/// <summary>
		/// Gets whether <paramref name="toolbar"/> can offer a drawer toggle.
		/// </summary>
		/// <param name="toolbar">The toolbar being rendered.</param>
		/// <param name="owner">
		/// The flyout-owning element, when the caller knows it. Required today; ignored once the
		/// upstream capability ships, because the toolbar will answer for itself.
		/// </param>
		/// <remarks>
		/// <para>
		/// <b>Why the owner is a parameter.</b> Upstream computes this inside Controls, where the
		/// shell is in scope, and exposes only <c>toolbar is IToolbarDrawerToggleVisible</c>. Off-tree
		/// there is no public path from a toolbar to its shell - <c>ShellToolbar</c> is not even an
		/// <see cref="Element"/>, so there is no parent chain to walk. Rather than latch the answer
		/// onto the toolbar (which is exactly the write path upstream removed), the caller that
		/// already knows the owner passes it in.
		/// </para>
		/// <para>
		/// Deliberately independent of <see cref="IToolbar.BackButtonVisible"/>: a shell in flyout
		/// mode still has a drawer while a pushed page shows a back button. Folding the back button
		/// in here is what the removed latch did wrong.
		/// </para>
		/// <para>
		/// On adoption every call site collapses to
		/// <c>toolbar is IToolbarDrawerToggleVisible { DrawerToggleVisible: true }</c> and this
		/// adapter is deleted, owner parameter and all.
		/// </para>
		/// </remarks>
		public static bool GetDrawerToggleVisible(IToolbar? toolbar, IFlyoutView? owner)
		{
			// Adoption replaces this entire body - and the owner parameter - with:
			//     toolbar is IToolbarDrawerToggleVisible { DrawerToggleVisible: true }
			if (toolbar is null)
			{
				return false;
			}

			return (owner ?? FindFlyoutOwner(toolbar))?.FlyoutBehavior == FlyoutBehavior.Flyout;
		}

		/// <summary>
		/// Gets whether a press on the toolbar's navigation icon belongs to the drawer toggle.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two subscribers listen to the same <c>IconPressed</c> event: the flyout/shell side, which
		/// opens or toggles the drawer, and the toolbar handler, which pops the navigation stack.
		/// Only one of them owns any given press, and the owner is whichever icon is actually drawn -
		/// so routing the press has to ask the same question the rendering does.
		/// </para>
		/// <para>
		/// Gating the drawer side on <c>FlyoutBehavior == Flyout</c> alone is what made a back press
		/// toggle the drawer open <em>and</em> navigate back: the drawer stays available in flyout
		/// mode while a pushed page shows a back button, so availability is not ownership.
		/// </para>
		/// </remarks>
		public static bool ShouldToggleDrawer(IToolbar? toolbar, IFlyoutView? owner = null)
		{
			if (toolbar is null || !toolbar.IsVisible)
			{
				return false;
			}

			return TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, GetDrawerToggleVisible(toolbar, owner))
				== TizenNavigationIconKind.DrawerToggle;
		}

		/// <summary>
		/// Finds the flyout view that owns <paramref name="toolbar"/>, when the caller did not
		/// supply one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A toolbar is not an <see cref="Element"/> - <c>ShellToolbar</c> in particular is not - so
		/// there is no parent chain to walk from it. What a handler DOES have is the page the toolbar
		/// is presenting, and that page can be walked up to its owning <see cref="Shell"/> or
		/// <see cref="FlyoutPage"/>.
		/// </para>
		/// <para>
		/// This exists because deriving the owner from the toolbar handler's own virtual view is
		/// silently wrong: the virtual view IS the toolbar, so a cast to <see cref="IFlyoutView"/>
		/// never succeeds and the capability is permanently false. The visible symptom is a shell
		/// popping back to its root and showing an empty navigation slot where the hamburger should
		/// be, with no flyout-behaviour change anywhere to explain it.
		/// </para>
		/// </remarks>
		public static IFlyoutView? FindFlyoutOwner(IToolbar? toolbar)
		{
			if (toolbar is not Toolbar concrete)
			{
				return null;
			}

			// Toolbar.Parent is the page whose toolbar this is; walk that page's public parent
			// chain. Element.Parent is public API - Controls' own FindParentOfType helper is
			// internal and deliberately not used here.
			for (IElement? element = concrete.Parent; element is not null; element = element.Parent)
			{
				if (element is IFlyoutView flyout and (Shell or FlyoutPage))
				{
					return flyout;
				}
			}

			return null;
		}
	}
}
