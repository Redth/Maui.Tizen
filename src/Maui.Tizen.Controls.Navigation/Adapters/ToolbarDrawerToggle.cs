using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Tizen-owned storage for the "is the flyout/drawer toggle currently shown in the toolbar"
	/// flag.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the one Wave C dependency with no published equivalent. The in-tree backend wrote
	/// <c>Microsoft.Maui.Controls.Toolbar.DrawerToggleVisible</c>, which is <c>internal</c>;
	/// <see cref="IToolbar"/> exposes <see cref="IToolbar.BackButtonVisible"/> and
	/// <see cref="IToolbar.IsVisible"/> but nothing equivalent.
	/// </para>
	/// <para>
	/// Rather than reflect onto the internal property, the flag is kept here, attached to the
	/// toolbar instance. Tizen is the only consumer: the value is written by the shell view when
	/// flyout behaviour changes, and read by the Tizen toolbar view when it decides whether to draw
	/// a hamburger icon. Nothing in Controls needs to observe it, so Tizen-side ownership is
	/// behaviourally equivalent for this backend.
	/// </para>
	/// <para>
	/// The corresponding upstream request is tracked as
	/// <see cref="UpstreamApiRequests.ToolbarDrawerToggleVisible"/>.
	/// </para>
	/// </remarks>
	public static class ToolbarDrawerToggle
	{
		// Weak keys so a discarded toolbar does not pin Tizen state for the life of the process.
		static readonly ConditionalWeakTable<IToolbar, StrongBox<bool>> s_state = new();

		/// <summary>
		/// Gets whether the drawer toggle should be shown for <paramref name="toolbar"/>.
		/// </summary>
		public static bool GetDrawerToggleVisible(IToolbar? toolbar)
			=> toolbar is not null && s_state.TryGetValue(toolbar, out StrongBox<bool>? box) && box.Value;

		/// <summary>
		/// Sets whether the drawer toggle should be shown for <paramref name="toolbar"/>.
		/// </summary>
		public static void SetDrawerToggleVisible(IToolbar? toolbar, bool visible)
		{
			if (toolbar is null)
			{
				return;
			}

			s_state.GetValue(toolbar, static _ => new StrongBox<bool>(false)).Value = visible;
		}
	}
}
