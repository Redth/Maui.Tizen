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
		/// <summary>Nothing owns the slot; a title icon may render there.</summary>
		None,

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
		/// <remarks>
		/// Both checks are load-bearing. The generation rejects a callback that a newer update has
		/// superseded; the source comparison rejects one whose image was swapped for a different one
		/// at the same generation.
		/// </remarks>
		public static bool IsCurrentTitleIconUpdate(IToolbar toolbar, int generation, ImageSource? source)
		{
			if (toolbar is null)
			{
				return false;
			}

			if (!s_generations.TryGetValue(toolbar, out StrongBox<int>? current) || current.Value != generation)
			{
				return false;
			}

			return ReferenceEquals((toolbar as Toolbar)?.TitleIcon, source);
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

			return drawerToggleVisible ? TizenNavigationIconKind.DrawerToggle : TizenNavigationIconKind.None;
		}
	}
}
