using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The NUI navigation drawer used to present an <see cref="IFlyoutView"/> on phone and
	/// wearable profiles.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.MauiFlyoutView</c> in dotnet/maui. Behaviour is
	/// preserved; only type ownership changed.
	/// </para>
	/// <para>
	/// Renamed from <c>MauiFlyoutView</c> so it cannot collide (CS0433) with the <c>net*-tizen</c>
	/// build of <c>Microsoft.Maui.dll</c>, which still exports its own.
	/// </para>
	/// <para>
	/// The toolbar is forwarded to the drawer's <em>content</em> rather than handled here: in a
	/// flyout the toolbar belongs to the detail page, so this type is a pass-through container.
	/// </para>
	/// </remarks>
	public class TizenFlyoutView : NavigationDrawer, ITizenToolbarContainer
	{
		void ITizenToolbarContainer.SetToolbar(TizenToolbarView toolbar)
		{
			if (Content is ITizenToolbarContainer container)
				container.SetToolbar(toolbar);
		}
	}

	/// <summary>
	/// The NUI navigation drawer used to present an <see cref="IFlyoutView"/> on the TV profile.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.MauiTVFlyoutView</c>. Identical in shape to
	/// <see cref="TizenFlyoutView"/> but built on <c>TVNavigationDrawer</c>, which gives the
	/// focus-driven interaction model a TV remote needs instead of touch gestures.
	/// </remarks>
	public class TizenTVFlyoutView : TVNavigationDrawer, ITizenToolbarContainer
	{
		void ITizenToolbarContainer.SetToolbar(TizenToolbarView toolbar)
		{
			if (Content is ITizenToolbarContainer container)
				container.SetToolbar(toolbar);
		}
	}
}
