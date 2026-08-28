using System;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Identifies which icon currently owns a toolbar's navigation slot.
	/// </summary>
	/// <remarks>
	/// Back button, drawer toggle and title icon all render into the same single slot, so choosing
	/// between them is a precedence decision rather than three independent flags. Naming the outcome
	/// keeps that decision in one place instead of scattered <c>if</c> chains that can disagree.
	/// </remarks>
	public enum TizenNavigationIconKind
	{
		/// <summary>Nothing owns the slot.</summary>
		None,

		/// <summary>A title icon owns the slot. Lowest precedence.</summary>
		TitleIcon,

		/// <summary>A back button owns the slot. Highest precedence.</summary>
		BackButton,

		/// <summary>A drawer toggle owns the slot.</summary>
		DrawerToggle,
	}

	/// <summary>
	/// Guards the toolbar's navigation icon slot against stale asynchronous updates.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Loading a title icon is asynchronous. Its completion callback captures the toolbar and then
	/// writes the icon whenever it happens to finish - so if the navigation state changed while the
	/// load was in flight, a late callback silently overwrites a back button or drawer toggle with
	/// an icon that is no longer wanted. Two loads racing each other can also land out of order and
	/// leave the older image on screen.
	/// </para>
	/// <para>
	/// Every update to the slot therefore takes a generation number, and an async callback only
	/// applies its result if it is still the newest update AND the image source it was started for
	/// is still the toolbar's current one. Anything older is dropped.
	/// </para>
	/// <para>
	/// This mirrors the approach upstream took for the same race in dotnet/maui#37863.
	/// </para>
	/// </remarks>
	public static class TizenToolbarNavigationSlot
	{
		// Keyed weakly so a discarded toolbar does not pin its generation for the process lifetime.
		static readonly ConditionalWeakTable<IToolbar, StrongBox<int>> s_generations = new();

		/// <summary>
		/// Begins an update to <paramref name="toolbar"/>'s navigation slot and returns its
		/// generation.
		/// </summary>
		/// <remarks>
		/// Call this once per update, before doing any work. Every earlier in-flight update is
		/// invalidated by the act of beginning a new one.
		/// </remarks>
		public static int BeginNavigationIconUpdate(IToolbar toolbar)
		{
			ArgumentNullException.ThrowIfNull(toolbar);

			StrongBox<int> generation = s_generations.GetValue(toolbar, static _ => new StrongBox<int>(0));

			return ++generation.Value;
		}

		/// <summary>
		/// Gets whether an asynchronous title-icon load may still apply its result.
		/// </summary>
		/// <param name="toolbar">The toolbar being updated.</param>
		/// <param name="generation">The generation returned when the update began.</param>
		/// <param name="source">The image source the load was started for.</param>
		/// <param name="drawerToggleVisible">Whether a drawer toggle is currently available.</param>
		/// <remarks>
		/// <para>
		/// THREE checks, all load-bearing:
		/// </para>
		/// <list type="number">
		/// <item><description>the generation rejects a callback a newer update superseded;</description></item>
		/// <item><description>the source comparison rejects one whose image was swapped at the same
		/// generation;</description></item>
		/// <item><description>the OWNER check rejects one that would paint over a back button or
		/// drawer toggle.</description></item>
		/// </list>
		/// <para>
		/// The third is not redundant. Setting <c>TitleIcon</c> while a back button is already
		/// showing legitimately starts a load at the newest generation for the current source - both
		/// of the first two checks pass - and without the owner check the image would overwrite the
		/// back button when it arrives. Upstream applies the same guard by requiring
		/// <c>NavigationIconKind == TitleIcon</c>.
		/// </para>
		/// </remarks>
		public static bool IsCurrentTitleIconUpdate(
			IToolbar toolbar,
			int generation,
			ImageSource? source,
			bool drawerToggleVisible)
		{
			if (toolbar is null)
			{
				return false;
			}

			if (!s_generations.TryGetValue(toolbar, out StrongBox<int>? current) || current.Value != generation)
			{
				return false;
			}

			if (!ReferenceEquals((toolbar as Toolbar)?.TitleIcon, source))
			{
				return false;
			}

			// Only paint the title icon if it still owns the slot.
			return GetNavigationIconKind(toolbar, drawerToggleVisible) == TizenNavigationIconKind.TitleIcon;
		}

		/// <summary>
		/// Decides which icon owns <paramref name="toolbar"/>'s navigation slot.
		/// </summary>
		/// <remarks>
		/// Back-precedence, not mutual exclusivity: <paramref name="drawerToggleVisible"/> may be
		/// true at the same time as <see cref="IToolbar.BackButtonVisible"/> - a shell in flyout mode
		/// still has a drawer while a pushed page shows a back button. Only one icon fits, so the
		/// back button wins.
		/// </remarks>
		public static TizenNavigationIconKind GetNavigationIconKind(IToolbar? toolbar, bool drawerToggleVisible)
		{
			if (toolbar is null)
			{
				return TizenNavigationIconKind.None;
			}

			if (toolbar.BackButtonVisible)
			{
				return TizenNavigationIconKind.BackButton;
			}

			if (drawerToggleVisible)
			{
				return TizenNavigationIconKind.DrawerToggle;
			}

			return (toolbar as Toolbar)?.TitleIcon is not null
				? TizenNavigationIconKind.TitleIcon
				: TizenNavigationIconKind.None;
		}
	}
}
